using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

public sealed class BackgroundWorkQuery(ViewerDbContext database)
{
    public async Task<BackgroundWorkStatus> GetAsync(CancellationToken cancellationToken = default)
    {
        var work = await database.BackgroundWork
            .AsNoTracking()
            .Include(row => row.LibraryDirectory)
            .OrderByDescending(row => row.RequestedAt)
            .Take(50)
            .ToListAsync(cancellationToken);
        var workIds = work.Select(row => row.Id).ToArray();
        var issues = await database.WorkIssues
            .AsNoTracking()
            .Where(row => workIds.Contains(row.BackgroundWorkId) && row.ResolvedAt == null)
            .OrderByDescending(row => row.CreatedAt)
            .ToListAsync(cancellationToken);

        return new BackgroundWorkStatus(
            work.Select(row => new BackgroundWorkSummary(
                row.Id,
                row.Category,
                row.State,
                row.LibraryDirectoryId,
                row.LibraryDirectory.Name,
                row.DiscoveredCandidateCount,
                row.CompletedItemCount,
                row.IssueCount,
                AsOffset(row.RequestedAt),
                AsNullableOffset(row.StartedAt),
                AsNullableOffset(row.FinishedAt))).ToArray(),
            issues.Select(row => new WorkIssueSummary(
                row.Id,
                row.BackgroundWorkId,
                row.Severity,
                row.Cause,
                row.RemediationOwner,
                row.AffectedScope,
                row.Impact,
                row.RequiredAction,
                AsOffset(row.CreatedAt))).ToArray());
    }

    private static DateTimeOffset AsOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? AsNullableOffset(DateTime? value) =>
        value is null ? null : AsOffset(value.Value);
}
