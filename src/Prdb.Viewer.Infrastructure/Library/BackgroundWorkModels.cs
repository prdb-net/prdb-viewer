using Prdb.Viewer.Core.Library;

namespace Prdb.Viewer.Infrastructure.Library;

public sealed record BackgroundWorkSummary(
    Guid Id,
    BackgroundWorkCategory Category,
    BackgroundWorkState State,
    BackgroundWorkTrigger Trigger,
    string Phase,
    Guid LibraryDirectoryId,
    string LibraryDirectoryName,
    int DiscoveredCandidateCount,
    int CompletedItemCount,
    int IssueCount,
    // Only present when a stable denominator and a credible estimate exist. Open-ended discovery
    // reports its concrete counts and phase instead of a fabricated percentage.
    int? CompletedPercent,
    string? WaitingReason,
    DateTimeOffset? NextAttemptAt,
    bool CancellationRequested,
    bool Cancellable,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? LastActivityAt,
    DateTimeOffset? FinishedAt);

public sealed record WorkIssueSummary(
    Guid Id,
    string Reference,
    Guid BackgroundWorkId,
    BackgroundWorkCategory Category,
    Guid LibraryDirectoryId,
    WorkIssueSeverity Severity,
    WorkIssueCause Cause,
    RemediationOwner RemediationOwner,
    WorkIssueRetryDisposition RetryDisposition,
    string Phase,
    string Summary,
    string Detail,
    string AffectedScope,
    string? ContainerPath,
    string Impact,
    string RequiredAction,
    string ExpectedResolutionEvidence,
    int OccurrenceCount,
    int AffectedItemCount,
    int Version,
    IReadOnlyList<WorkIssueAction> Actions,
    string? OperatorHandoff,
    Guid? VideoId,
    Guid? VideoFileId,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset FirstOccurredAt,
    DateTimeOffset LastOccurredAt,
    DateTimeOffset? ResolvedAt,
    string? ResolutionEvidence);

public sealed record WorkIssueAffectedItem(
    string Scope,
    string? ContainerPath,
    Guid? VideoFileId,
    int OccurrenceCount,
    DateTimeOffset FirstOccurredAt,
    DateTimeOffset LastOccurredAt);

public sealed record BackgroundWorkStatus(
    IReadOnlyList<BackgroundWorkSummary> Work,
    IReadOnlyList<WorkIssueSummary> Issues,
    IReadOnlyList<WorkIssueSummary> RecentlyResolvedIssues,
    // True while at least one unresolved Operational Blocker or Safety Stop exists. It is derived
    // from severity, so no acknowledgement can hide it.
    bool OperationalAttention,
    int OperationalAttentionCount,
    bool Paused,
    DateTimeOffset? PausedAt);

public enum QueueLibraryScanVerdict
{
    Queued,
    Coalesced,
    NotFound,
}

public sealed record QueueLibraryScanResult(QueueLibraryScanVerdict Verdict, Guid? WorkId = null);

public enum BackgroundWorkActionVerdict
{
    Accepted,
    NotFound,

    /// <summary>The run already settled, so there is nothing left to stop.</summary>
    AlreadySettled,

    /// <summary>
    /// The displayed version, work scope, or Library Directory generation is no longer current, so
    /// the action was refused rather than committed against stale detail.
    /// </summary>
    Stale,

    /// <summary>The issue's cause cannot be advanced by this action.</summary>
    NotApplicable,
}

public sealed record BackgroundWorkActionResult(
    BackgroundWorkActionVerdict Verdict,
    WorkIssueSummary? Issue = null);

public sealed record BackgroundWorkPauseResult(bool Paused, DateTimeOffset? PausedAt);
