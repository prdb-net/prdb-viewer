namespace Prdb.Viewer.Core.Library;

/// <summary>
/// The rules that turn a Work Issue's cause, severity, and retry disposition into its current
/// Remediation Owner, its offered actions, and whether it establishes Operational Attention. They
/// are the reason routine diagnosis never depends on container logs.
/// </summary>
public static class WorkIssueRule
{
    /// <summary>
    /// Only an Operational Blocker or a Safety Stop establishes Operational Attention. One
    /// content-specific item, however often it recurs, never does so by itself.
    /// </summary>
    public static bool EstablishesOperationalAttention(WorkIssueSeverity severity) =>
        severity is WorkIssueSeverity.OperationalBlocker or WorkIssueSeverity.SafetyStop;

    /// <summary>
    /// The single party currently expected to advance the issue. An eligible automatic retry owns
    /// it first; exhausted retries transfer it to whoever can change the condition.
    /// </summary>
    public static RemediationOwner OwnerFor(
        WorkIssueCause cause,
        WorkIssueRetryDisposition disposition) =>
        disposition == WorkIssueRetryDisposition.AutomaticRetryScheduled
            ? RemediationOwner.AutomaticRecovery
            : cause switch
            {
                WorkIssueCause.SourceAccess => RemediationOwner.InstallationOperator,
                WorkIssueCause.ChangingSource => RemediationOwner.InstallationOperator,
                WorkIssueCause.Capacity => RemediationOwner.InstallationOperator,
                WorkIssueCause.InvalidContent => RemediationOwner.Administrator,
                WorkIssueCause.ExternalAvailability => RemediationOwner.Administrator,
                WorkIssueCause.ExternalAuthority => RemediationOwner.Administrator,
                WorkIssueCause.Configuration => RemediationOwner.Administrator,
                WorkIssueCause.InternalConsistency => RemediationOwner.Administrator,
                _ => RemediationOwner.Administrator,
            };

    /// <summary>
    /// Whether the operator, rather than the application, has to change a deployment, mount,
    /// permission, storage, or host condition before the issue can resolve.
    /// </summary>
    public static bool NeedsOperatorHandoff(
        WorkIssueCause cause,
        WorkIssueRetryDisposition disposition) =>
        disposition != WorkIssueRetryDisposition.AutomaticRetryScheduled &&
        cause is WorkIssueCause.SourceAccess
            or WorkIssueCause.ChangingSource
            or WorkIssueCause.InvalidContent
            or WorkIssueCause.Capacity
            or WorkIssueCause.InternalConsistency;

    /// <summary>
    /// The actions offered for an unresolved issue. A Safety Stop offers no blind retry, an issue
    /// still owned by Automatic Recovery offers no manual attempt, and every action that cannot
    /// advance this cause is left out.
    /// </summary>
    public static IReadOnlyList<WorkIssueAction> ActionsFor(
        WorkIssueCause cause,
        WorkIssueSeverity severity,
        WorkIssueRetryDisposition disposition,
        bool hasAffectedItems)
    {
        var actions = new List<WorkIssueAction>();

        if (severity != WorkIssueSeverity.SafetyStop &&
            disposition != WorkIssueRetryDisposition.AutomaticRetryScheduled)
        {
            actions.Add(cause switch
            {
                WorkIssueCause.SourceAccess => WorkIssueAction.CheckAgain,
                WorkIssueCause.ChangingSource => WorkIssueAction.CheckAgain,
                WorkIssueCause.Capacity => WorkIssueAction.CheckAgain,
                WorkIssueCause.Configuration => WorkIssueAction.CheckAgain,
                WorkIssueCause.ExternalAuthority => WorkIssueAction.CheckAgain,
                _ => WorkIssueAction.RetryNow,
            });
        }

        if (cause is WorkIssueCause.ExternalAuthority or WorkIssueCause.ExternalAvailability)
        {
            actions.Add(WorkIssueAction.OpenPrdbSettings);
        }

        if (cause is WorkIssueCause.Configuration or WorkIssueCause.SourceAccess)
        {
            actions.Add(WorkIssueAction.OpenLibraryDirectory);
        }

        if (hasAffectedItems)
        {
            actions.Add(WorkIssueAction.ViewAffectedItems);
        }

        if (NeedsOperatorHandoff(cause, disposition))
        {
            actions.Add(WorkIssueAction.CopyOperatorHandoff);
        }

        return actions;
    }

    /// <summary>
    /// The order the administrative surface uses: Safety Stops first, then Operational Blockers,
    /// then Scoped Issues, so the most consequential obstacle is never buried under item noise.
    /// </summary>
    public static int SeverityRank(WorkIssueSeverity severity) => severity switch
    {
        WorkIssueSeverity.SafetyStop => 0,
        WorkIssueSeverity.OperationalBlocker => 1,
        _ => 2,
    };
}
