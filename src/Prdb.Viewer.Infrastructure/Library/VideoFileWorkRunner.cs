using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// The shared shape of a derived lane that advances one Video File at a time: it claims its own
/// durable run, retries earlier failures when a new run begins, commits after every file so a
/// restart resumes where it stopped, and never writes beneath a Library Directory. It also obeys
/// the installation-wide pause and the cancellation of its own bounded run.
/// </summary>
public abstract class VideoFileWorkRunner(
    ViewerDbContext database,
    WorkIssueRecorder issues,
    TimeProvider timeProvider)
{
    protected ViewerDbContext Database { get; } = database;

    protected WorkIssueRecorder Issues { get; } = issues;

    protected abstract BackgroundWorkCategory Category { get; }

    protected abstract string Phase { get; }

    public async Task<bool> RunNextSliceAsync(CancellationToken cancellationToken = default)
    {
        if (await BackgroundWorkGate.IsPausedAsync(Database, cancellationToken))
        {
            return await BackgroundWorkGate.ParkAsync(
                Database,
                Category,
                Now(),
                cancellationToken);
        }

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

        if (work.CancellationRequested)
        {
            await FinishAsync(work, BackgroundWorkState.Cancelled, cancellationToken);
            return true;
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
            work.Phase = Phase;
            work.LastActivityAt = now;
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
                await SettledStateAsync(work, cancellationToken),
                cancellationToken);

            if (followUp)
            {
                await DerivedWorkQueue.QueueAsync(
                    Database,
                    work.LibraryDirectoryId,
                    work.ConfigurationGeneration,
                    Category,
                    BackgroundWorkTrigger.FollowUpWork,
                    Now(),
                    cancellationToken);
                await Database.SaveChangesAsync(cancellationToken);
            }

            return true;
        }

        await AdvanceAsync(work, outstanding, cancellationToken);
        work.LastActivityAt = Now();
        work.UpdatedAt = work.LastActivityAt.Value;
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

    /// <summary>
    /// A settled run reports Completed only when this Library Directory carries no unresolved
    /// obstacle of its own category, so an item that is still explained does not vanish behind a
    /// clean-looking outcome.
    /// </summary>
    private async Task<BackgroundWorkState> SettledStateAsync(
        BackgroundWorkRow work,
        CancellationToken cancellationToken) =>
        await Database.WorkIssues.AnyAsync(
            issue => issue.LibraryDirectoryId == work.LibraryDirectoryId &&
                     issue.Category == Category &&
                     issue.ResolvedAt == null,
            cancellationToken)
            ? BackgroundWorkState.CompletedWithIssues
            : BackgroundWorkState.Completed;

    protected Task ReportAsync(
        BackgroundWorkRow work,
        WorkIssueReport report,
        CancellationToken cancellationToken) =>
        Issues.RecordAsync(work, report, cancellationToken);

    protected Task ResolveAsync(
        BackgroundWorkRow work,
        WorkIssueCause cause,
        string evidence,
        CancellationToken cancellationToken) =>
        Issues.ResolveAsync(
            work.LibraryDirectoryId,
            Category,
            cause,
            evidence,
            cancellationToken);

    protected Task ResolveItemAsync(
        BackgroundWorkRow work,
        WorkIssueCause cause,
        string scope,
        string evidence,
        CancellationToken cancellationToken) =>
        Issues.ResolveItemAsync(
            work.LibraryDirectoryId,
            Category,
            cause,
            scope,
            evidence,
            cancellationToken);

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

    /// <summary>
    /// Leaves the run Waiting with its condition but without a scheduled attempt, so nothing
    /// retries against an unchanged prerequisite until an Administrator explicitly asks.
    /// </summary>
    protected async Task HoldAsync(
        BackgroundWorkRow work,
        string reason,
        CancellationToken cancellationToken)
    {
        var now = Now();
        work.State = BackgroundWorkState.Waiting;
        work.WaitingReason = reason;
        work.NextAttemptAt = null;
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
        work.Phase = BackgroundWorkPhases.Settled;
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
