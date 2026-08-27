using Prdb.Viewer.Core.Access;

namespace Prdb.Viewer.Infrastructure.Access;

public enum BootstrapClaimVerdict
{
    Created,
    InvalidInput,
    InvalidAuthorization,
    AlreadyClaimed,
}

public enum SignInVerdict
{
    SignedIn,
    InvalidCredentials,
    ApprovalPending,
    Disabled,
}

public enum RegistrationRequestVerdict
{
    Submitted,
    InvalidInput,
    InstallationUnclaimed,
}

public enum AccountActionVerdict
{
    Completed,
    NotFound,
    InvalidState,
    LastAdministrator,
}

public enum RecoveryVerdict
{
    PasswordReplaced,
    InvalidInput,
    InvalidCode,
}

public sealed record AccountIdentity(
    Guid Id,
    string Username,
    string? Email,
    AccountAuthority Authority);

public sealed record AccountSummary(
    Guid Id,
    string Username,
    string? Email,
    AccountAuthority Authority,
    AccountState State,
    DateTimeOffset RegisteredAt);

public sealed record SessionGrant(
    Guid SessionId,
    string SessionToken,
    string CsrfToken,
    DateTimeOffset ExpiresAt,
    AccountIdentity Account);

public sealed record AuthenticatedSession(
    Guid SessionId,
    DateTimeOffset ExpiresAt,
    AccountIdentity Account);

public sealed record BootstrapClaimResult(
    BootstrapClaimVerdict Verdict,
    SessionGrant? Session = null);

public sealed record SignInResult(SignInVerdict Verdict, SessionGrant? Session = null);

public sealed record OperatorCredentialResult(bool Created, string? DeliveryPath, string? Reason);

public sealed record IssuedRecoveryCode(
    AccountActionVerdict Verdict,
    string? RecoveryCode = null,
    DateTimeOffset? ExpiresAt = null);
