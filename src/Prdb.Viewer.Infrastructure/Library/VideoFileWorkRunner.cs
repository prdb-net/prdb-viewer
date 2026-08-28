using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// The shared shape of a derived lane that advances one Video File at a time: it claims its own
/// durable run, retries earlier failures when a new run begins, commits after every file so a
/// restart resumes where it stopped, and never writes beneath a Library Directory.
/// </summary>
public abstract class VideoFileWorkRunner(ViewerDbContext database, TimeProvider timeProvider)
{
    protected ViewerDbContext Database { get; } = database;

    protected abstract BackgroundWorkCategory Category { get; }

    public async Task<bool> RunNextSliceAsync(CancellationToken cancellationToken = default)
    {
        var now = Now();
        var work = await Database.BackgroundWork
            .AsTracking()
            .Include(row => row.LibraryDirectory)
            .Where(row => row.Category == Category &&
                          (row.State == BackgroundWorkState.Queued ||
                           row.State == BackgroundWorkState.Running ||
                           (row.State == BackgroundWorkState.Waiting &&
                            row.NextAttemptAt != null &&
                            row.NextAttemptAt <= now)))
            .OrderBy(row => row.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (work is null)
        {
            return false;
        }

        if (work.LibraryDirectory.State != LibraryDirectoryState.Active ||
            work.LibraryDirectory.ConfigurationGeneration != work.ConfigurationGeneration)
        {
            await FinishAsync(work, BackgroundWorkState.Cancelled, cancellationToken);
            return true;
        }

        if (work.State != BackgroundWorkState.Running)
        {
            work.State = BackgroundWorkState.Running;
            work.StartedAt ??= now;
            work.NextAttemptAt = null;
            work.WaitingReason = null;
            work.UpdatedAt = now;
            await RetryEarlierFailuresAsync(work.LibraryDirectoryId, cancellationToken);
            await Database.SaveChangesAsync(cancellationToken);
        }

        var outstanding = await Outstanding(work.LibraryDirectoryId)
            .OrderBy(file => file.RelativePath)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        work.DiscoveredCandidateCount = work.CompletedItemCount + await Outstanding(work.LibraryDirectoryId)
            .CountAsync(cancellationToken);

        if (outstanding.Count == 0)
        {
            var followUp = work.FollowUpRequested;
            await CompleteAsync(work, cancellationToken);
            await FinishAsync(
                work,
                work.IssueCount == 0
                    ? BackgroundWorkState.Completed
                    : BackgroundWorkState.CompletedWithIssues,
                cancellationToken);

            if (followUp)
            {
                await DerivedWorkQueue.QueueAsync(
                    Database,
                    work.LibraryDirectoryId,
                    work.ConfigurationGeneration,
                    Category,
                    Now(),
                    cancellationToken);
                await Database.SaveChangesAsync(cancellationToken);
            }

            return true;
        }

        await AdvanceAsync(work, outstanding, cancellationToken);
        work.UpdatedAt = Now();
        await Database.SaveChangesAsync(cancellationToken);
        return true;
    }

    protected virtual int BatchSize => 1;

    protected abstract IQueryable<VideoFileRow> Outstanding(Guid libraryDirectoryId);

    protected abstract Task RetryEarlierFailuresAsync(
        Guid libraryDirectoryId,
        CancellationToken cancellationToken);

    protected abstract Task AdvanceAsync(
        BackgroundWorkRow work,
        IReadOnlyList<VideoFileRow> files,
        CancellationToken cancellationToken);

    protected virtual Task CompleteAsync(
        BackgroundWorkRow work,
        CancellationToken cancellationToken) => Task.CompletedTask;

    protected void AddIssue(
        BackgroundWorkRow work,
        string scope,
        WorkIssueCause cause,
        WorkIssueSeverity severity,
        RemediationOwner owner,
        string impact,
        string requiredAction)
    {
        work.IssueCount++;
        Database.WorkIssues.Add(new WorkIssueRow
        {
            Id = Guid.CreateVersion7(),
            BackgroundWorkId = work.Id,
            Severity = severity,
            Cause = cause,
            RemediationOwner = owner,
            AffectedScope = scope,
            Impact = impact,
            RequiredAction = requiredAction,
            CreatedAt = Now(),
        });
    }

    /// <summary>
    /// Records a Work Issue unless this run already carries an unresolved one of the same cause,
    /// so a repeatedly waiting lane reports one obstacle rather than a stream of duplicates.
    /// </summary>
    protected async Task AddIssueOnceAsync(
        BackgroundWorkRow work,
        string scope,
        WorkIssueCause cause,
        WorkIssueSeverity severity,
        RemediationOwner owner,
        string impact,
        string requiredAction,
        CancellationToken cancellationToken)
    {
        var open = await Database.WorkIssues.AnyAsync(
            issue => issue.BackgroundWorkId == work.Id &&
                     issue.Cause == cause &&
                     issue.ResolvedAt == null,
            cancellationToken);

        if (!open)
        {
            AddIssue(work, scope, cause, severity, owner, impact, requiredAction);
        }
    }

    /// <summary>
    /// Closes the obstacles this run reported once work continued successfully, which is the
    /// Resolution Evidence the operational model expects before an issue disappears.
    /// </summary>
    protected Task ResolveIssuesAsync(
        BackgroundWorkRow work,
        WorkIssueCause cause,
        CancellationToken cancellationToken)
    {
        var now = Now();

        return Database.WorkIssues
            .Where(issue => issue.BackgroundWorkId == work.Id &&
                            issue.Cause == cause &&
                            issue.ResolvedAt == null)
            .ExecuteUpdateAsync(
                update => update.SetProperty(issue => issue.ResolvedAt, now),
                cancellationToken);
    }

    protected async Task WaitAsync(
        BackgroundWorkRow work,
        string reason,
        TimeSpan retryAfter,
        CancellationToken cancellationToken)
    {
        var now = Now();
        work.State = BackgroundWorkState.Waiting;
        work.WaitingReason = reason;
        work.NextAttemptAt = now + retryAfter;
        work.UpdatedAt = now;
        await Database.SaveChangesAsync(cancellationToken);
    }

    protected async Task FinishAsync(
        BackgroundWorkRow work,
        BackgroundWorkState state,
        CancellationToken cancellationToken)
    {
        var now = Now();
        work.State = state;
        work.UpdatedAt = now;
        work.FinishedAt = now;
        work.FollowUpRequested = false;
        work.NextAttemptAt = null;
        work.WaitingReason = null;
        await Database.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Whether the file on storage still matches what technical inspection committed. Derived work
    /// on a file that is being written would describe content the library never admitted.
    /// </summary>
    protected static bool IsStable(string path, VideoFileRow file)
    {
        try
        {
            var current = new FileInfo(path);

            return current.Exists &&
                   current.Length == file.Size &&
                   current.LastWriteTimeUtc == file.LastWriteTimeUtc;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    protected DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
}
