using Prdb.Viewer.Core.Library;

namespace Prdb.Viewer.Infrastructure.Persistence;

public sealed class WorkIssueRow
{
    public Guid Id { get; set; }

    /// <summary>
    /// The stable reference an Administrator quotes and an Operator Handoff carries. It survives
    /// recurrence so an operator and an Administrator always talk about the same obstacle.
    /// </summary>
    public required string Reference { get; set; }

    public Guid BackgroundWorkId { get; set; }

    public BackgroundWorkRow BackgroundWork { get; set; } = null!;

    /// <summary>
    /// Retained on the issue as well as on its run, so an issue keeps its category and scope after
    /// a newer run supersedes the operational detail of the run that first reported it.
    /// </summary>
    public BackgroundWorkCategory Category { get; set; }

    public Guid LibraryDirectoryId { get; set; }

    public int ConfigurationGeneration { get; set; }

    public WorkIssueSeverity Severity { get; set; }

    public WorkIssueCause Cause { get; set; }

    public RemediationOwner RemediationOwner { get; set; }

    public WorkIssueRetryDisposition RetryDisposition { get; set; }

    /// <summary>
    /// Equivalent issues aggregate under this key — cause, work category, and shared scope — so a
    /// systematic obstacle reports one message with a count instead of one alert per item.
    /// </summary>
    public required string AggregationKey { get; set; }

    public required string AffectedScope { get; set; }

    public string? ContainerPath { get; set; }

    public required string Phase { get; set; }

    public required string Summary { get; set; }

    public required string Detail { get; set; }

    public required string Impact { get; set; }

    public required string RequiredAction { get; set; }

    /// <summary>What the application must observe before this issue may be considered resolved.</summary>
    public required string ExpectedResolutionEvidence { get; set; }

    /// <summary>The sanitised operating-system or remote failure class, never a stack trace.</summary>
    public required string SafeCause { get; set; }

    public int OccurrenceCount { get; set; } = 1;

    public int AffectedItemCount { get; set; } = 1;

    public int AttemptedRetries { get; set; }

    public DateTime? NextAttemptAt { get; set; }

    public DateTime FirstOccurredAt { get; set; }

    public DateTime LastOccurredAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    /// <summary>The trustworthy observation that actually closed the issue.</summary>
    public string? ResolutionEvidence { get; set; }

    /// <summary>
    /// The earlier occurrence this one continues after Resolution Evidence had closed it. Linking
    /// rather than reopening keeps the earlier resolution from being rewritten.
    /// </summary>
    public Guid? PreviousOccurrenceId { get; set; }

    /// <summary>
    /// Bumped on every change. Administrative actions carry the version they were shown, so a stale
    /// action cannot commit against detail that another Administrator, a retry, or a restart
    /// already replaced.
    /// </summary>
    public int Version { get; set; } = 1;

    public Guid? VideoId { get; set; }

    public Guid? VideoFileId { get; set; }
}
