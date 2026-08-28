using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// The administrative status surface. It groups bounded runs by category and scope, reports
/// trustworthy observed counts rather than an invented completion percentage, and presents every
/// unresolved Work Issue with the action that can actually advance it.
/// </summary>
public sealed class BackgroundWorkQuery(ViewerDbContext database)
{
    private static readonly BackgroundWorkState[] Cancellable =
    [
        BackgroundWorkState.Queued,
        BackgroundWorkState.Running,
        BackgroundWorkState.Waiting,
        BackgroundWorkState.Paused,
    ];

    public async Task<BackgroundWorkStatus> GetAsync(CancellationToken cancellationToken = default)
    {
        var configuration = await database.InstallationConfigurations
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        var work = await database.BackgroundWork
            .AsNoTracking()
            .Include(row => row.LibraryDirectory)
            .OrderByDescending(row => row.RequestedAt)
            .Take(50)
            .ToListAsync(cancellationToken);
        var unresolved = await database.WorkIssues
            .AsNoTracking()
            .Where(row => row.ResolvedAt == null)
            .ToListAsync(cancellationToken);
        var resolved = await database.WorkIssues
            .AsNoTracking()
            .Where(row => row.ResolvedAt != null)
            .OrderByDescending(row => row.ResolvedAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        return new BackgroundWorkStatus(
            work.Select(Summarize).ToArray(),
            unresolved
                .OrderBy(issue => WorkIssueRule.SeverityRank(issue.Severity))
                .ThenByDescending(issue => issue.LastOccurredAt)
                .Select(Describe)
                .ToArray(),
            resolved.Select(Describe).ToArray(),
            unresolved.Any(issue => WorkIssueRule.EstablishesOperationalAttention(issue.Severity)),
            unresolved.Count(issue => WorkIssueRule.EstablishesOperationalAttention(issue.Severity)),
            configuration.BackgroundWorkPaused,
            AsNullableOffset(configuration.BackgroundWorkPausedAt));
    }

    public async Task<IReadOnlyList<WorkIssueAffectedItem>?> GetAffectedItemsAsync(
        Guid workIssueId,
        CancellationToken cancellationToken = default)
    {
        if (!await database.WorkIssues.AnyAsync(issue => issue.Id == workIssueId, cancellationToken))
        {
            return null;
        }

        return await database.WorkIssueItems
            .AsNoTracking()
            .Where(item => item.WorkIssueId == workIssueId)
            .OrderBy(item => item.Scope)
            .Select(item => new WorkIssueAffectedItem(
                item.Scope,
                item.ContainerPath,
                item.VideoFileId,
                item.OccurrenceCount,
                new DateTimeOffset(DateTime.SpecifyKind(item.FirstOccurredAt, DateTimeKind.Utc)),
                new DateTimeOffset(DateTime.SpecifyKind(item.LastOccurredAt, DateTimeKind.Utc))))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// A percentage is offered only where the denominator is stable: an inspection or derived lane
    /// knows how many admitted items it must still advance, while an open-ended traversal does not.
    /// </summary>
    private static int? Percent(BackgroundWorkRow row) =>
        row.Category != BackgroundWorkCategory.LibraryScan &&
        row.DiscoveredCandidateCount > 0 &&
        row.CompletedItemCount <= row.DiscoveredCandidateCount
            ? (int)Math.Round(100d * row.CompletedItemCount / row.DiscoveredCandidateCount)
            : null;

    private static BackgroundWorkSummary Summarize(BackgroundWorkRow row) =>
        new(row.Id,
            row.Category,
            row.State,
            row.Trigger,
            row.Phase,
            row.LibraryDirectoryId,
            row.LibraryDirectory.Name,
            row.DiscoveredCandidateCount,
            row.CompletedItemCount,
            row.IssueCount,
            Percent(row),
            row.WaitingReason,
            AsNullableOffset(row.NextAttemptAt),
            row.CancellationRequested,
            Cancellable.Contains(row.State) && !row.CancellationRequested,
            AsOffset(row.RequestedAt),
            AsNullableOffset(row.StartedAt),
            AsNullableOffset(row.LastActivityAt),
            AsNullableOffset(row.FinishedAt));

    public static WorkIssueSummary Describe(WorkIssueRow row)
    {
        var actions = row.ResolvedAt is null
            ? WorkIssueRule.ActionsFor(
                row.Cause,
                row.Severity,
                row.RetryDisposition,
                row.AffectedItemCount > 0)
            : [];

        return new WorkIssueSummary(
            row.Id,
            row.Reference,
            row.BackgroundWorkId,
            row.Category,
            row.LibraryDirectoryId,
            row.Severity,
            row.Cause,
            row.RemediationOwner,
            row.RetryDisposition,
            row.Phase,
            row.Summary,
            row.Detail,
            row.AffectedScope,
            row.ContainerPath,
            row.Impact,
            row.RequiredAction,
            row.ExpectedResolutionEvidence,
            row.OccurrenceCount,
            row.AffectedItemCount,
            row.Version,
            actions,
            actions.Contains(WorkIssueAction.CopyOperatorHandoff) ? Handoff(row) : null,
            row.VideoId,
            row.VideoFileId,
            AsNullableOffset(row.NextAttemptAt),
            AsOffset(row.FirstOccurredAt),
            AsOffset(row.LastOccurredAt),
            AsNullableOffset(row.ResolvedAt),
            row.ResolutionEvidence);
    }

    private static string Handoff(WorkIssueRow row) =>
        OperatorHandoff.Compose(new OperatorHandoffFacts(
            row.Reference,
            row.Severity,
            row.Cause,
            row.Category,
            row.Phase,
            row.AffectedScope,
            row.ContainerPath,
            row.SafeCause,
            row.OccurrenceCount,
            row.AttemptedRetries,
            AsOffset(row.FirstOccurredAt),
            AsOffset(row.LastOccurredAt),
            row.RequiredAction,
            row.ExpectedResolutionEvidence));

    private static DateTimeOffset AsOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? AsNullableOffset(DateTime? value) =>
        value is null ? null : AsOffset(value.Value);
}
