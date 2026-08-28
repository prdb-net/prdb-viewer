using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// What a lane observed when work could not advance. The lane supplies the domain facts; the
/// recorder decides how they aggregate, who owns them, and what would resolve them.
/// </summary>
public sealed record WorkIssueReport(
    WorkIssueCause Cause,
    WorkIssueSeverity Severity,
    WorkIssueRetryDisposition RetryDisposition,
    string Scope,
    string AggregationScope,
    string Phase,
    string Summary,
    string Detail,
    string Impact,
    string RequiredAction,
    string SafeCause,
    string ExpectedResolutionEvidence)
{
    public string? ContainerPath { get; init; }

    public Guid? VideoId { get; init; }

    public Guid? VideoFileId { get; init; }

    public DateTime? NextAttemptAt { get; init; }

    /// <summary>
    /// Whether this observation describes one of many items that share a cause. A shared scope such
    /// as a mount root, the prdb connection, or application storage describes itself, so it carries
    /// no affected-item list and is never offered `View affected items`.
    /// </summary>
    public bool AggregatesItems { get; init; } = true;
}

/// <summary>
/// Records and closes Work Issues. Equivalent observations aggregate by cause, work category, and
/// shared scope, recurrence updates the existing occurrence rather than creating another issue, and
/// an issue closes only against explicit Resolution Evidence.
/// </summary>
public sealed class WorkIssueRecorder(ViewerDbContext database, TimeProvider timeProvider)
{
    public async Task<WorkIssueRow> RecordAsync(
        BackgroundWorkRow work,
        WorkIssueReport report,
        CancellationToken cancellationToken = default)
    {
        if (!WorkIssueMessages.IsUsableSummary(report.Summary))
        {
            throw new ArgumentException(
                "A Work Issue summary must state what cannot currently happen.",
                nameof(report));
        }

        var now = Now();
        var key = AggregationKey(report.Cause, work.Category, report.AggregationScope);
        var open = await database.WorkIssues
            .AsTracking()
            .Where(issue => issue.AggregationKey == key && issue.ResolvedAt == null)
            .OrderByDescending(issue => issue.LastOccurredAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (open is null)
        {
            var previous = await database.WorkIssues
                .Where(issue => issue.AggregationKey == key)
                .OrderByDescending(issue => issue.LastOccurredAt)
                .Select(issue => (Guid?)issue.Id)
                .FirstOrDefaultAsync(cancellationToken);
            var id = Guid.CreateVersion7();
            open = new WorkIssueRow
            {
                Id = id,
                Reference = ReferenceFor(id),
                BackgroundWorkId = work.Id,
                Category = work.Category,
                LibraryDirectoryId = work.LibraryDirectoryId,
                ConfigurationGeneration = work.ConfigurationGeneration,
                Severity = report.Severity,
                Cause = report.Cause,
                RetryDisposition = report.RetryDisposition,
                RemediationOwner = WorkIssueRule.OwnerFor(report.Cause, report.RetryDisposition),
                AggregationKey = key,
                AffectedScope = report.Scope,
                ContainerPath = report.ContainerPath,
                Phase = report.Phase,
                Summary = report.Summary,
                Detail = report.Detail,
                Impact = report.Impact,
                RequiredAction = report.RequiredAction,
                SafeCause = report.SafeCause,
                ExpectedResolutionEvidence = report.ExpectedResolutionEvidence,
                VideoId = report.VideoId,
                VideoFileId = report.VideoFileId,
                NextAttemptAt = report.NextAttemptAt,
                OccurrenceCount = 1,
                AffectedItemCount = 0,
                FirstOccurredAt = now,
                LastOccurredAt = now,
                CreatedAt = now,
                PreviousOccurrenceId = previous,
            };
            database.WorkIssues.Add(open);
            work.IssueCount++;
        }
        else
        {
            open.BackgroundWorkId = work.Id;
            open.ConfigurationGeneration = work.ConfigurationGeneration;
            open.Severity = report.Severity;
            open.RetryDisposition = report.RetryDisposition;
            open.RemediationOwner = WorkIssueRule.OwnerFor(report.Cause, report.RetryDisposition);
            open.Phase = report.Phase;
            open.Detail = report.Detail;
            open.Impact = report.Impact;
            open.RequiredAction = report.RequiredAction;
            open.SafeCause = report.SafeCause;
            open.NextAttemptAt = report.NextAttemptAt;
            open.OccurrenceCount++;
            open.LastOccurredAt = now;
            open.Version++;

            if (report.RetryDisposition == WorkIssueRetryDisposition.AutomaticRetryScheduled)
            {
                open.AttemptedRetries++;
            }
        }

        if (report.AggregatesItems)
        {
            await RecordItemAsync(open, report, now, cancellationToken);
        }

        return open;
    }

    /// <summary>
    /// Closes every unresolved issue of one cause for a work category and Library Directory. The
    /// caller passes the observation that disproved the cause, because a resolved issue must always
    /// name the evidence that closed it.
    /// </summary>
    public async Task<int> ResolveAsync(
        Guid libraryDirectoryId,
        BackgroundWorkCategory category,
        WorkIssueCause cause,
        string evidence,
        CancellationToken cancellationToken = default)
    {
        var now = Now();

        return await database.WorkIssues
            .Where(issue => issue.LibraryDirectoryId == libraryDirectoryId &&
                            issue.Category == category &&
                            issue.Cause == cause &&
                            issue.ResolvedAt == null)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(issue => issue.ResolvedAt, now)
                    .SetProperty(issue => issue.ResolutionEvidence, evidence)
                    .SetProperty(issue => issue.RemediationOwner, RemediationOwner.AutomaticRecovery)
                    .SetProperty(issue => issue.Version, issue => issue.Version + 1),
                cancellationToken);
    }

    /// <summary>
    /// Removes one item from an aggregated issue after that item succeeded, and closes the issue
    /// once no affected item is left. This is how a per-file obstacle disappears without an
    /// Administrator ever acknowledging it.
    /// </summary>
    public async Task ResolveItemAsync(
        Guid libraryDirectoryId,
        BackgroundWorkCategory category,
        WorkIssueCause cause,
        string scope,
        string evidence,
        CancellationToken cancellationToken = default)
    {
        var issues = await database.WorkIssues
            .AsTracking()
            .Where(issue => issue.LibraryDirectoryId == libraryDirectoryId &&
                            issue.Category == category &&
                            issue.Cause == cause &&
                            issue.ResolvedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var issue in issues)
        {
            var item = await database.WorkIssueItems
                .AsTracking()
                .SingleOrDefaultAsync(
                    row => row.WorkIssueId == issue.Id && row.Scope == scope,
                    cancellationToken);

            if (item is null)
            {
                continue;
            }

            database.WorkIssueItems.Remove(item);
            issue.AffectedItemCount = Math.Max(0, issue.AffectedItemCount - 1);
            issue.Version++;

            if (issue.AffectedItemCount == 0)
            {
                issue.ResolvedAt = Now();
                issue.ResolutionEvidence = evidence;
            }
            else if (string.Equals(issue.AffectedScope, scope, StringComparison.Ordinal))
            {
                issue.AffectedScope = await database.WorkIssueItems
                    .Where(row => row.WorkIssueId == issue.Id && row.Scope != scope)
                    .OrderBy(row => row.Scope)
                    .Select(row => row.Scope)
                    .FirstAsync(cancellationToken);
            }
        }
    }

    /// <summary>
    /// Whether any unresolved issue currently establishes Operational Attention. It is derived from
    /// severity rather than stored, so no acknowledgement can make it disappear.
    /// </summary>
    public Task<bool> HasOperationalAttentionAsync(CancellationToken cancellationToken = default) =>
        database.WorkIssues.AnyAsync(
            issue => issue.ResolvedAt == null &&
                     (issue.Severity == WorkIssueSeverity.OperationalBlocker ||
                      issue.Severity == WorkIssueSeverity.SafetyStop),
            cancellationToken);

    public static string AggregationKey(
        WorkIssueCause cause,
        BackgroundWorkCategory category,
        string aggregationScope) => $"{cause}|{category}|{aggregationScope}";

    /// <summary>
    /// The stable, quotable reference. It is derived from the random tail of the issue identity,
    /// so it does not depend on a counter that a Restore would have to reproduce and two issues
    /// created in the same millisecond still receive different references.
    /// </summary>
    public static string ReferenceFor(Guid id) =>
        $"WI-{Convert.ToHexString(id.ToByteArray().AsSpan(10, 6))}";

    private async Task RecordItemAsync(
        WorkIssueRow issue,
        WorkIssueReport report,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var existing = await database.WorkIssueItems
            .AsTracking()
            .SingleOrDefaultAsync(
                row => row.WorkIssueId == issue.Id && row.Scope == report.Scope,
                cancellationToken);

        if (existing is not null)
        {
            existing.OccurrenceCount++;
            existing.LastOccurredAt = now;
            return;
        }

        database.WorkIssueItems.Add(new WorkIssueItemRow
        {
            Id = Guid.CreateVersion7(),
            WorkIssueId = issue.Id,
            Scope = report.Scope,
            ContainerPath = report.ContainerPath,
            VideoFileId = report.VideoFileId,
            FirstOccurredAt = now,
            LastOccurredAt = now,
        });
        issue.AffectedItemCount++;
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
}
