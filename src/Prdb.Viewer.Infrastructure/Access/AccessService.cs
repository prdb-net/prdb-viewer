using System.Data;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Access;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Access;

public sealed class AccessService(
    ViewerDbContext database,
    ViewerDatabaseLocation location,
    IPasswordHasher<AccountRow> passwordHasher,
    TimeProvider timeProvider,
    OperatorCredentialFiles credentialFiles)
{
    public async Task<bool> IsClaimedAsync(CancellationToken cancellationToken = default) =>
        await database.Accounts.AnyAsync(
            account => account.Authority == AccountAuthority.Administrator &&
                       account.State == AccountState.Approved,
            cancellationToken);

    public async Task<OperatorCredentialResult> CreateBootstrapAuthorizationAsync(
        CancellationToken cancellationToken = default)
    {
        if (await IsClaimedAsync(cancellationToken))
        {
            return new OperatorCredentialResult(
                Created: false,
                DeliveryPath: null,
                Reason: "The installation already has an Administrator.");
        }

        var token = NewToken();
        var expiresAt = Now() + AccessLifetimes.BootstrapAuthorization;
        const string fileName = "bootstrap-authorization.txt";
        var expectedPath = Path.Combine(location.DataDirectory, "operator", fileName);

        var existing = await database.BootstrapAuthorizations
            .AsTracking()
            .SingleOrDefaultAsync(cancellationToken);

        if (existing is null)
        {
            database.BootstrapAuthorizations.Add(new BootstrapAuthorizationRow
            {
                Id = BootstrapAuthorizationRow.TheOnlyRow,
                TokenHash = Hash(token),
                ExpiresAt = expiresAt,
                DeliveryPath = expectedPath,
            });
        }
        else
        {
            existing.TokenHash = Hash(token);
            existing.ExpiresAt = expiresAt;
            existing.DeliveryPath = expectedPath;
        }

        await database.SaveChangesAsync(cancellationToken);

        try
        {
            var deliveryPath = await credentialFiles.WriteAsync(fileName, token, cancellationToken);
            return new OperatorCredentialResult(true, deliveryPath, null);
        }
        catch
        {
            database.BootstrapAuthorizations.Remove(
                await database.BootstrapAuthorizations.AsTracking().SingleAsync(cancellationToken));
            await database.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task<BootstrapClaimResult> ClaimAsync(
        string authorization,
        string username,
        string password,
        string? email,
        CancellationToken cancellationToken = default)
    {
        if (!ValidAccountInput(username, password, email))
        {
            return new BootstrapClaimResult(BootstrapClaimVerdict.InvalidInput);
        }

        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        if (await database.Accounts.AnyAsync(cancellationToken))
        {
            return new BootstrapClaimResult(BootstrapClaimVerdict.AlreadyClaimed);
        }

        var bootstrap = await database.BootstrapAuthorizations
            .AsTracking()
            .SingleOrDefaultAsync(cancellationToken);
        var now = Now();

        if (bootstrap is null || bootstrap.ExpiresAt <= now || !TokenMatches(authorization, bootstrap.TokenHash))
        {
            return new BootstrapClaimResult(BootstrapClaimVerdict.InvalidAuthorization);
        }

        var account = NewAccount(
            username,
            email,
            AccountAuthority.Administrator,
            AccountState.Approved,
            now);
        account.ApprovedAt = now;
        account.PasswordHash = passwordHasher.HashPassword(account, password);
        database.Accounts.Add(account);

        var grant = IssueSession(account, now);
        database.BootstrapAuthorizations.Remove(bootstrap);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        credentialFiles.Delete(bootstrap.DeliveryPath);
        return new BootstrapClaimResult(BootstrapClaimVerdict.Created, grant);
    }

    public async Task<SignInResult> SignInAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (!UsernameRule.IsValid(username) || password.Length > PasswordRule.MaximumLength)
        {
            return new SignInResult(SignInVerdict.InvalidCredentials);
        }

        var account = await database.Accounts
            .AsTracking()
            .SingleOrDefaultAsync(
                row => row.NormalizedUsername == UsernameRule.Normalize(username),
                cancellationToken);

        if (account is null)
        {
            return new SignInResult(SignInVerdict.InvalidCredentials);
        }

        var passwordResult = passwordHasher.VerifyHashedPassword(account, account.PasswordHash, password);

        if (passwordResult == PasswordVerificationResult.Failed)
        {
            return new SignInResult(SignInVerdict.InvalidCredentials);
        }

        if (account.State == AccountState.PendingApproval)
        {
            return new SignInResult(SignInVerdict.ApprovalPending);
        }

        if (account.State == AccountState.Disabled)
        {
            return new SignInResult(SignInVerdict.Disabled);
        }

        if (passwordResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            account.PasswordHash = passwordHasher.HashPassword(account, password);
        }

        var grant = IssueSession(account, Now());
        await database.SaveChangesAsync(cancellationToken);
        return new SignInResult(SignInVerdict.SignedIn, grant);
    }

    public async Task<AuthenticatedSession?> AuthenticateAsync(
        string sessionToken,
        CancellationToken cancellationToken = default)
    {
        if (!TokenCanBeValid(sessionToken))
        {
            return null;
        }

        var tokenHash = Hash(sessionToken);
        var now = Now();
        var session = await database.Sessions
            .Include(row => row.Account)
            .SingleOrDefaultAsync(row => row.TokenHash == tokenHash, cancellationToken);

        if (session is null ||
            session.ExpiresAt <= now ||
            session.Account.State != AccountState.Approved)
        {
            return null;
        }

        return new AuthenticatedSession(
            session.Id,
            AsOffset(session.ExpiresAt),
            Identity(session.Account));
    }

    public async Task SignOutAsync(
        Guid sessionId,
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        await database.Sessions
            .Where(row => row.Id == sessionId && row.AccountId == accountId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<RegistrationRequestVerdict> SubmitRegistrationRequestAsync(
        string username,
        string password,
        string? email,
        CancellationToken cancellationToken = default)
    {
        if (!ValidAccountInput(username, password, email))
        {
            return RegistrationRequestVerdict.InvalidInput;
        }

        if (!await IsClaimedAsync(cancellationToken))
        {
            return RegistrationRequestVerdict.InstallationUnclaimed;
        }

        var normalized = UsernameRule.Normalize(username);

        if (await database.Accounts.AnyAsync(
                account => account.NormalizedUsername == normalized,
                cancellationToken))
        {
            return RegistrationRequestVerdict.Submitted;
        }

        var account = NewAccount(
            username,
            email,
            AccountAuthority.User,
            AccountState.PendingApproval,
            Now());
        account.PasswordHash = passwordHasher.HashPassword(account, password);
        database.Accounts.Add(account);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();

            if (!await database.Accounts.AnyAsync(
                    account => account.NormalizedUsername == normalized,
                    cancellationToken))
            {
                throw;
            }
        }

        return RegistrationRequestVerdict.Submitted;
    }

    public async Task<IReadOnlyList<AccountSummary>> ListAccountsAsync(
        CancellationToken cancellationToken = default)
    {
        var accounts = await database.Accounts
            .OrderBy(account => account.RegisteredAt)
            .ToListAsync(cancellationToken);

        return accounts
            .Select(account => new AccountSummary(
                account.Id,
                account.Username,
                account.Email,
                account.Authority,
                account.State,
                AsOffset(account.RegisteredAt)))
            .ToArray();
    }

    public async Task<AccountActionVerdict> ApproveAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var account = await database.Accounts.AsTracking().SingleOrDefaultAsync(
            row => row.Id == accountId,
            cancellationToken);

        if (account is null)
        {
            return AccountActionVerdict.NotFound;
        }

        if (account.State != AccountState.PendingApproval)
        {
            return AccountActionVerdict.InvalidState;
        }

        account.State = AccountState.Approved;
        account.ApprovedAt = Now();
        await database.SaveChangesAsync(cancellationToken);
        return AccountActionVerdict.Completed;
    }

    /// <summary>
    /// Returns a disabled Account to Approved.
    ///
    /// Disabling is a decision an Administrator can be wrong about, or right about only for a while,
    /// and until now it was the one Account state with no way out: approval requires a request
    /// waiting for it, and a disabled Account has none. Everything the Account established — its
    /// viewing, its organisation, its identity — was retained the whole time, so reinstating it
    /// restores access rather than creating an Account.
    ///
    /// Its sessions are not restored. They were deleted when it was disabled, which is what made
    /// disabling take effect, so the person signs in again.
    /// </summary>
    public async Task<AccountActionVerdict> ReinstateAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var account = await database.Accounts.AsTracking().SingleOrDefaultAsync(
            row => row.Id == accountId,
            cancellationToken);

        if (account is null)
        {
            return AccountActionVerdict.NotFound;
        }

        if (account.State != AccountState.Disabled)
        {
            return AccountActionVerdict.InvalidState;
        }

        account.State = AccountState.Approved;
        account.ApprovedAt = Now();
        account.DisabledAt = null;
        await database.SaveChangesAsync(cancellationToken);
        return AccountActionVerdict.Completed;
    }

    public async Task<AccountActionVerdict> DisableAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var account = await database.Accounts.AsTracking().SingleOrDefaultAsync(
            row => row.Id == accountId,
            cancellationToken);

        if (account is null)
        {
            return AccountActionVerdict.NotFound;
        }

        if (account.State == AccountState.Disabled)
        {
            return AccountActionVerdict.InvalidState;
        }

        if (account.Authority == AccountAuthority.Administrator &&
            await database.Accounts.CountAsync(
                row => row.Authority == AccountAuthority.Administrator &&
                       row.State == AccountState.Approved,
                cancellationToken) <= 1)
        {
            return AccountActionVerdict.LastAdministrator;
        }

        account.State = AccountState.Disabled;
        account.DisabledAt = Now();
        await database.Sessions
            .Where(row => row.AccountId == accountId)
            .ExecuteDeleteAsync(cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AccountActionVerdict.Completed;
    }

    public async Task<IssuedRecoveryCode> IssueRecoveryCodeAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var account = await database.Accounts.SingleOrDefaultAsync(
            row => row.Id == accountId && row.State == AccountState.Approved,
            cancellationToken);

        if (account is null)
        {
            return new IssuedRecoveryCode(AccountActionVerdict.NotFound);
        }

        var token = await PersistRecoveryCodeAsync(account, deliveryPath: null, cancellationToken);
        return new IssuedRecoveryCode(
            AccountActionVerdict.Completed,
            token.Value,
            AsOffset(token.ExpiresAt));
    }

    public async Task<OperatorCredentialResult> IssueAdministratorRecoveryCodeAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        if (!UsernameRule.IsValid(username))
        {
            return new OperatorCredentialResult(false, null, "No approved Administrator matched.");
        }

        var account = await database.Accounts.SingleOrDefaultAsync(
            row => row.NormalizedUsername == UsernameRule.Normalize(username) &&
                   row.Authority == AccountAuthority.Administrator &&
                   row.State == AccountState.Approved,
            cancellationToken);

        if (account is null)
        {
            return new OperatorCredentialResult(false, null, "No approved Administrator matched.");
        }

        var fileName = $"recovery-{account.NormalizedUsername.ToLowerInvariant()}.txt";
        var deliveryPath = Path.Combine(location.DataDirectory, "operator", fileName);
        var token = await PersistRecoveryCodeAsync(account, deliveryPath, cancellationToken);

        try
        {
            var path = await credentialFiles.WriteAsync(fileName, token.Value, cancellationToken);
            return new OperatorCredentialResult(true, path, null);
        }
        catch
        {
            await database.RecoveryCodes
                .Where(row => row.Id == token.Id)
                .ExecuteDeleteAsync(cancellationToken);
            throw;
        }
    }

    public async Task<RecoveryVerdict> RecoverAsync(
        string username,
        string recoveryCode,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        if (!UsernameRule.IsValid(username) || !PasswordRule.IsValid(newPassword))
        {
            return RecoveryVerdict.InvalidInput;
        }

        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var account = await database.Accounts.AsTracking().SingleOrDefaultAsync(
            row => row.NormalizedUsername == UsernameRule.Normalize(username) &&
                   row.State == AccountState.Approved,
            cancellationToken);

        if (account is null || !TokenCanBeValid(recoveryCode))
        {
            return RecoveryVerdict.InvalidCode;
        }

        var hash = Hash(recoveryCode);
        var now = Now();
        var code = await database.RecoveryCodes.AsTracking().SingleOrDefaultAsync(
            row => row.AccountId == account.Id &&
                   row.TokenHash == hash &&
                   row.ConsumedAt == null &&
                   row.ExpiresAt > now,
            cancellationToken);

        if (code is null)
        {
            return RecoveryVerdict.InvalidCode;
        }

        code.ConsumedAt = now;
        account.PasswordHash = passwordHasher.HashPassword(account, newPassword);
        await database.Sessions
            .Where(row => row.AccountId == account.Id)
            .ExecuteDeleteAsync(cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        credentialFiles.Delete(code.DeliveryPath);
        return RecoveryVerdict.PasswordReplaced;
    }

    private SessionGrant IssueSession(AccountRow account, DateTime now)
    {
        var sessionToken = NewToken();
        var expiresAt = now + AccessLifetimes.Session;
        var row = new SessionRow
        {
            Id = Guid.CreateVersion7(),
            Account = account,
            AccountId = account.Id,
            TokenHash = Hash(sessionToken),
            CreatedAt = now,
            ExpiresAt = expiresAt,
        };
        database.Sessions.Add(row);

        return new SessionGrant(
            row.Id,
            sessionToken,
            CsrfToken.For(sessionToken),
            AsOffset(expiresAt),
            Identity(account));
    }

    private async Task<(Guid Id, string Value, DateTime ExpiresAt)> PersistRecoveryCodeAsync(
        AccountRow account,
        string? deliveryPath,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        await database.RecoveryCodes
            .Where(row => row.AccountId == account.Id && row.ConsumedAt == null)
            .ExecuteDeleteAsync(cancellationToken);

        var value = NewToken();
        var row = new RecoveryCodeRow
        {
            Id = Guid.CreateVersion7(),
            AccountId = account.Id,
            TokenHash = Hash(value),
            ExpiresAt = Now() + AccessLifetimes.RecoveryCode,
            DeliveryPath = deliveryPath,
        };
        database.RecoveryCodes.Add(row);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (row.Id, value, row.ExpiresAt);
    }

    private static AccountRow NewAccount(
        string username,
        string? email,
        AccountAuthority authority,
        AccountState state,
        DateTime now) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            Username = username,
            NormalizedUsername = UsernameRule.Normalize(username),
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            PasswordHash = string.Empty,
            Authority = authority,
            State = state,
            RegisteredAt = now,
        };

    private static bool ValidAccountInput(string username, string password, string? email) =>
        UsernameRule.IsValid(username) &&
        PasswordRule.IsValid(password) &&
        ValidEmail(email);

    private static bool ValidEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ||
        (email.Length <= 254 && MailAddress.TryCreate(email, out _));

    private static AccountIdentity Identity(AccountRow account) =>
        new(account.Id, account.Username, account.Email, account.Authority);

    private static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] Hash(string token) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static bool TokenMatches(string token, byte[] expected) =>
        TokenCanBeValid(token) && CryptographicOperations.FixedTimeEquals(Hash(token), expected);

    private static bool TokenCanBeValid(string? token) =>
        token is { Length: >= 32 and <= 128 };

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    private static DateTimeOffset AsOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
