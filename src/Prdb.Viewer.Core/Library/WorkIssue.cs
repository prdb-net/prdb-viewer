namespace Prdb.Viewer.Core.Library;

public enum WorkIssueSeverity
{
    ScopedIssue,
    OperationalBlocker,
    SafetyStop,
}

public enum WorkIssueCause
{
    SourceAccess,
    ChangingSource,
    InvalidContent,
    Capacity,
    ExternalAvailability,
    ExternalAuthority,
    Configuration,
    InternalConsistency,
}

public enum RemediationOwner
{
    AutomaticRecovery,
    Administrator,
    InstallationOperator,
}

/// <summary>
/// Whether the product still expects to make progress on a Work Issue by itself. It is the fact
/// that decides who currently owns the issue and which actions an Administrator is offered.
/// </summary>
public enum WorkIssueRetryDisposition
{
    /// <summary>Automatic Recovery holds the issue and a further attempt is scheduled.</summary>
    AutomaticRetryScheduled,

    /// <summary>
    /// No further automatic attempt remains for this obstacle, so a person must act before the
    /// blocked work can continue.
    /// </summary>
    RetriesExhausted,

    /// <summary>
    /// Retrying the same unchanged condition cannot help, so no attempt is scheduled. Deterministic
    /// content outcomes, unchanged authority, unchanged configuration, and Safety Stops use it.
    /// </summary>
    NoAutomaticRetry,
}

/// <summary>
/// The cause-specific actions an Administrator may be offered for a Work Issue. A generic action
/// that cannot advance the issue is deliberately absent rather than shown and refused.
/// </summary>
public enum WorkIssueAction
{
    RetryNow,
    CheckAgain,
    OpenPrdbSettings,
    OpenLibraryDirectory,
    ViewAffectedItems,
    CopyOperatorHandoff,
}
