using Prdb.Viewer.Core.Library;

namespace Prdb.Viewer.Infrastructure.Library;

public sealed record BackgroundWorkSummary(
    Guid Id,
    BackgroundWorkCategory Category,
    BackgroundWorkState State,
    Guid LibraryDirectoryId,
    string LibraryDirectoryName,
    int DiscoveredCandidateCount,
    int CompletedItemCount,
    int IssueCount,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt);

public sealed record WorkIssueSummary(
    Guid Id,
    Guid BackgroundWorkId,
    WorkIssueSeverity Severity,
    WorkIssueCause Cause,
    RemediationOwner RemediationOwner,
    string AffectedScope,
    string Impact,
    string RequiredAction,
    DateTimeOffset CreatedAt);

public sealed record BackgroundWorkStatus(
    IReadOnlyList<BackgroundWorkSummary> Work,
    IReadOnlyList<WorkIssueSummary> Issues);

public enum QueueLibraryScanVerdict
{
    Queued,
    Coalesced,
    NotFound,
}

public sealed record QueueLibraryScanResult(QueueLibraryScanVerdict Verdict, Guid? WorkId = null);
