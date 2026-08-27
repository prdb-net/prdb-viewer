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
