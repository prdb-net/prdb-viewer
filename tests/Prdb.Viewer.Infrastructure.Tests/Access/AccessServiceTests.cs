using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Prdb.Viewer.Core.Access;
using Prdb.Viewer.Infrastructure.Access;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Access;

public sealed class AccessServiceTests
{
    private const string AdministratorPassword = "administrator password";

    [Fact]
    public async Task A_credential_file_it_cannot_remove_does_not_undo_a_completed_claim()
    {
        Assert.SkipWhen(
            OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess,
            "The test needs an unprivileged process on a Unix-like filesystem.");

        if (!OperatingSystem.IsWindows())
        {
            await ClaimSurvivesAnUndeletableCredentialAsync();
        }
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static async Task ClaimSurvivesAnUndeletableCredentialAsync()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var scope = database.Scope();
        var access = scope.ServiceProvider.GetRequiredService<AccessService>();
        var authorization = await access.CreateBootstrapAuthorizationAsync(
            TestContext.Current.CancellationToken);
        var value = (await File.ReadAllTextAsync(
            authorization.DeliveryPath!,
            TestContext.Current.CancellationToken)).Trim();

        // An Operator who generated the credential as a different identity leaves a directory
        // this process cannot delete from — exactly what a `docker exec` as root produces.
        var operatorDirectory = Path.GetDirectoryName(authorization.DeliveryPath!)!;
        File.SetUnixFileMode(
            operatorDirectory,
            UnixFileMode.UserRead | UnixFileMode.UserExecute);

        try
        {
            var claimed = await access.ClaimAsync(
                value,
                "administrator",
                AdministratorPassword,
                email: null,
                TestContext.Current.CancellationToken);

            // Removing the spent file is cleanup after the durable work committed. The Account
            // exists, so failing the claim over it would report a lie and leave the Operator
            // retrying something that can only answer AlreadyClaimed.
            Assert.Equal(BootstrapClaimVerdict.Created, claimed.Verdict);
            Assert.True(await access.IsClaimedAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            File.SetUnixFileMode(
                operatorDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public async Task Bootstrap_authorization_is_delivered_once_and_claims_the_first_administrator()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var scope = database.Scope();
        var access = scope.ServiceProvider.GetRequiredService<AccessService>();

        var authorization = await access.CreateBootstrapAuthorizationAsync(
            TestContext.Current.CancellationToken);

        Assert.True(authorization.Created);
        Assert.NotNull(authorization.DeliveryPath);
        Assert.True(File.Exists(authorization.DeliveryPath));

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(authorization.DeliveryPath));
        }

        var value = (await File.ReadAllTextAsync(
            authorization.DeliveryPath,
            TestContext.Current.CancellationToken)).Trim();
        var claimed = await access.ClaimAsync(
            value,
            "administrator",
            AdministratorPassword,
            email: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(BootstrapClaimVerdict.Created, claimed.Verdict);
        Assert.Equal(AccountAuthority.Administrator, claimed.Session!.Account.Authority);
        Assert.False(File.Exists(authorization.DeliveryPath));
        Assert.NotNull(await access.AuthenticateAsync(
            claimed.Session.SessionToken,
            TestContext.Current.CancellationToken));

        var repeated = await access.ClaimAsync(
            value,
            "another-admin",
            AdministratorPassword,
            email: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(BootstrapClaimVerdict.AlreadyClaimed, repeated.Verdict);
    }

    [Fact]
    public async Task Superseded_and_expired_bootstrap_authorizations_cannot_claim_the_installation()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 27, 18, 0, 0, TimeSpan.Zero));
        await using var database = await TestDatabase.CreateAsync(time);
        await using var scope = database.Scope();
        var access = scope.ServiceProvider.GetRequiredService<AccessService>();

        var firstDelivery = await access.CreateBootstrapAuthorizationAsync(
            TestContext.Current.CancellationToken);
        var firstAuthorization = (await File.ReadAllTextAsync(
            firstDelivery.DeliveryPath!,
            TestContext.Current.CancellationToken)).Trim();

        var secondDelivery = await access.CreateBootstrapAuthorizationAsync(
            TestContext.Current.CancellationToken);
        var secondAuthorization = (await File.ReadAllTextAsync(
            secondDelivery.DeliveryPath!,
            TestContext.Current.CancellationToken)).Trim();

        Assert.Equal(
            BootstrapClaimVerdict.InvalidAuthorization,
            (await access.ClaimAsync(
                firstAuthorization,
                "administrator",
                AdministratorPassword,
                email: null,
                TestContext.Current.CancellationToken)).Verdict);

        time.Advance(AccessLifetimes.BootstrapAuthorization + TimeSpan.FromSeconds(1));

        Assert.Equal(
            BootstrapClaimVerdict.InvalidAuthorization,
            (await access.ClaimAsync(
                secondAuthorization,
                "administrator",
                AdministratorPassword,
                email: null,
                TestContext.Current.CancellationToken)).Verdict);
    }

    [Fact]
    public async Task Registration_does_not_grant_access_before_administrator_approval()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var scope = database.Scope();
        var access = scope.ServiceProvider.GetRequiredService<AccessService>();
        await ClaimAdministratorAsync(access);

        var submitted = await access.SubmitRegistrationRequestAsync(
            "second-user",
            "second user password",
            "user@example.test",
            TestContext.Current.CancellationToken);
        var repeated = await access.SubmitRegistrationRequestAsync(
            "SECOND-USER",
            "a different password",
            email: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(RegistrationRequestVerdict.Submitted, submitted);
        Assert.Equal(RegistrationRequestVerdict.Submitted, repeated);

        var beforeApproval = await access.SignInAsync(
            "second-user",
            "second user password",
            TestContext.Current.CancellationToken);
        Assert.Equal(SignInVerdict.ApprovalPending, beforeApproval.Verdict);

        var accounts = await access.ListAccountsAsync(TestContext.Current.CancellationToken);
        var applicant = Assert.Single(accounts, account => account.Username == "second-user");
        Assert.Equal(AccountState.PendingApproval, applicant.State);

        Assert.Equal(
            AccountActionVerdict.Completed,
            await access.ApproveAsync(applicant.Id, TestContext.Current.CancellationToken));

        var afterApproval = await access.SignInAsync(
            "second-user",
            "second user password",
            TestContext.Current.CancellationToken);
        Assert.Equal(SignInVerdict.SignedIn, afterApproval.Verdict);

        Assert.Equal(
            AccountActionVerdict.Completed,
            await access.DisableAsync(applicant.Id, TestContext.Current.CancellationToken));
        Assert.Null(await access.AuthenticateAsync(
            afterApproval.Session!.SessionToken,
            TestContext.Current.CancellationToken));

        var administrator = Assert.Single(accounts, account =>
            account.Authority == AccountAuthority.Administrator);
        Assert.Equal(
            AccountActionVerdict.LastAdministrator,
            await access.DisableAsync(administrator.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Recovery_code_is_single_use_expires_and_ends_existing_sessions()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 27, 18, 0, 0, TimeSpan.Zero));
        await using var database = await TestDatabase.CreateAsync(time);
        await using var scope = database.Scope();
        var access = scope.ServiceProvider.GetRequiredService<AccessService>();
        var originalSession = await ClaimAdministratorAsync(access);

        var delivery = await access.IssueAdministratorRecoveryCodeAsync(
            "administrator",
            TestContext.Current.CancellationToken);
        var code = (await File.ReadAllTextAsync(
            delivery.DeliveryPath!,
            TestContext.Current.CancellationToken)).Trim();

        Assert.Equal(
            RecoveryVerdict.PasswordReplaced,
            await access.RecoverAsync(
                "administrator",
                code,
                "replacement password",
                TestContext.Current.CancellationToken));
        Assert.False(File.Exists(delivery.DeliveryPath));
        Assert.Null(await access.AuthenticateAsync(
            originalSession.SessionToken,
            TestContext.Current.CancellationToken));
        Assert.Equal(
            SignInVerdict.InvalidCredentials,
            (await access.SignInAsync(
                "administrator",
                AdministratorPassword,
                TestContext.Current.CancellationToken)).Verdict);
        Assert.Equal(
            SignInVerdict.SignedIn,
            (await access.SignInAsync(
                "administrator",
                "replacement password",
                TestContext.Current.CancellationToken)).Verdict);
        Assert.Equal(
            RecoveryVerdict.InvalidCode,
            await access.RecoverAsync(
                "administrator",
                code,
                "another replacement",
                TestContext.Current.CancellationToken));

        var expiring = await access.IssueAdministratorRecoveryCodeAsync(
            "administrator",
            TestContext.Current.CancellationToken);
        var expiringCode = (await File.ReadAllTextAsync(
            expiring.DeliveryPath!,
            TestContext.Current.CancellationToken)).Trim();
        time.Advance(TimeSpan.FromMinutes(31));

        Assert.Equal(
            RecoveryVerdict.InvalidCode,
            await access.RecoverAsync(
                "administrator",
                expiringCode,
                "expired replacement",
                TestContext.Current.CancellationToken));
    }

    private static async Task<SessionGrant> ClaimAdministratorAsync(AccessService access)
    {
        var delivery = await access.CreateBootstrapAuthorizationAsync(
            TestContext.Current.CancellationToken);
        var authorization = (await File.ReadAllTextAsync(
            delivery.DeliveryPath!,
            TestContext.Current.CancellationToken)).Trim();
        var claimed = await access.ClaimAsync(
            authorization,
            "administrator",
            AdministratorPassword,
            email: null,
            TestContext.Current.CancellationToken);

        return claimed.Session!;
    }
}
