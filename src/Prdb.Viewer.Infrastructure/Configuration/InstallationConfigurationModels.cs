using Prdb.Viewer.Core.Configuration;

namespace Prdb.Viewer.Infrastructure.Configuration;

public enum PrdbVerificationOutcome
{
    Verified,
    Rejected,
    Unavailable,
}

public enum PrdbConnectionUpdateVerdict
{
    Verified,
    Rejected,
    VerificationPending,
    Missing,
    InvalidInput,
    Superseded,
}

public enum LibraryDirectoryStageVerdict
{
    Staged,

    /// <summary>The display name is missing, too long, or carries a control character.</summary>
    InvalidName,

    /// <summary>The container path is not an absolute path the container could resolve.</summary>
    InvalidPath,

    OutsideMountArea,
    Missing,
    Unreadable,
    AlreadyConfigured,
}

public enum LibraryDirectoryActivationVerdict
{
    Activated,
    NotFound,
    Expired,
    NoLongerValid,
    AlreadyConfigured,
}

public sealed record PrdbConnectionUpdateResult(PrdbConnectionUpdateVerdict Verdict);

public sealed record LibraryDirectoryStageResult(
    LibraryDirectoryStageVerdict Verdict,
    Guid? StageId = null,
    string? Name = null,
    string? ContainerPath = null,
    DateTimeOffset? ExpiresAt = null);

public sealed record LibraryDirectoryActivationResult(
    LibraryDirectoryActivationVerdict Verdict,
    LibraryDirectorySummary? Directory = null);

public enum LibraryDirectoryRemovalVerdict
{
    Removed,
    NotFound,
    AlreadyRemoved,
}

public sealed record LibraryDirectoryRemovalResult(
    LibraryDirectoryRemovalVerdict Verdict,
    int RemovedVideoFileCount = 0,
    int AffectedVideoCount = 0);

public sealed record LibraryDirectorySummary(
    Guid Id,
    string Name,
    string ContainerPath,
    LibraryDirectoryState State,
    LibraryDirectoryHealth Health,
    int ConfigurationGeneration,
    DateTimeOffset ActivatedAt,
    bool InitialProcessingStarted,
    int AvailableVideoFileCount,
    DateTimeOffset? LastScanCompletedAt,
    DateTimeOffset? LastScanStartedAt,
    int LastScanCandidateCount,
    bool LastScanCoveredEverything,
    // When this Library Directory is next scanned without anyone asking, or null while nothing is
    // scheduled for it.
    DateTimeOffset? NextScanDueAt);

public sealed record InstallationConfigurationSummary(
    InstallationConfigurationStatus Status,
    PrdbConnectionStatus PrdbConnectionStatus,
    bool HasPrdbCredential,
    bool CredentialReplacementPending,
    DateTimeOffset? LastConnectionAttemptAt,
    DateTimeOffset? LastConnectionVerifiedAt,
    PrdbConnectionIssue? LastConnectionIssue,
    DateTimeOffset? ConfiguredAt,
    DateTimeOffset? FirstPlayableVideoReachedAt,
    string LibraryMountRoot,
    IReadOnlyList<LibraryDirectorySummary> LibraryDirectories);

public interface IPrdbConnectionVerifier
{
    Task<PrdbVerificationOutcome> VerifyAsync(
        string credential,
        CancellationToken cancellationToken = default);
}
