using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// The administrative controls over Background Work: the installation-wide pause, the cancellation
/// of one bounded run, and the cause-specific retry or recheck of an unresolved Work Issue. Every
/// action is bound to the state it was shown against, so a stale action is refused rather than
/// committed against detail that has since changed.
/// </summary>
public sealed class BackgroundWorkOperations(
    ViewerDbContext database,
    DerivedArtifactStore artifacts,
    LibraryWorkScheduler scheduler,
    TimeProvider timeProvider)
{
    public async Task<BackgroundWorkPauseResult> SetPausedAsync(
        bool paused,
        CancellationToken cancellationToken = default)
    {
        var configuration = await database.InstallationConfigurations
            .AsTracking()
            .SingleAsync(cancellationToken);
        var now = Now();

        if (configuration.BackgroundWorkPaused != paused)
        {
            configuration.BackgroundWorkPaused = paused;
            configuration.BackgroundWorkPausedAt = paused ? now : null;

            if (!paused)
            {
                await ReleaseParkedWorkAsync(now, cancellationToken);
            }

            await database.SaveChangesAsync(cancellationToken);
        }

        return new BackgroundWorkPauseResult(
            configuration.BackgroundWorkPaused,
            configuration.BackgroundWorkPausedAt is { } at
                ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc))
                : null);
    }

    /// <summary>
    /// Resuming returns every parked run to the lifecycle it had, so work continues from its
    /// retained checkpoints instead of starting a duplicate run or resetting its progress.
    /// </summary>
    private async Task ReleaseParkedWorkAsync(DateTime now, CancellationToken cancellationToken)
    {
        var parked = await database.BackgroundWork
            .AsTracking()
            .Where(work => work.State == BackgroundWorkState.Paused)
            .ToListAsync(cancellationToken);

        foreach (var work in parked)
        {
            work.State = work.StateBeforePause ?? BackgroundWorkState.Queued;
            work.StateBeforePause = null;
            work.UpdatedAt = now;
        }
    }

    public async Task<BackgroundWorkActionResult> CancelAsync(
        Guid workId,
        CancellationToken cancellationToken = default)
    {
        var work = await database.BackgroundWork
            .AsTracking()
            .SingleOrDefaultAsync(row => row.Id == workId, cancellationToken);

        if (work is null)
        {
            return new BackgroundWorkActionResult(BackgroundWorkActionVerdict.NotFound);
        }

        if (work.State is BackgroundWorkState.Completed
            or BackgroundWorkState.CompletedWithIssues
            or BackgroundWorkState.Cancelled)
        {
            return new BackgroundWorkActionResult(BackgroundWorkActionVerdict.AlreadySettled);
        }

        var now = Now();
        work.CancellationRequested = true;
        work.FollowUpRequested = false;
        work.UpdatedAt = now;

        // A parked or waiting run is not being advanced by its lane, so there is no later safe
        // boundary to wait for. Its committed observations stay, and its unvisited scope stays
        // unobserved.
        if (work.State is BackgroundWorkState.Paused or BackgroundWorkState.Waiting)
        {
            work.State = BackgroundWorkState.Cancelled;
            work.Phase = BackgroundWorkPhases.Settled;
            work.CoverageComplete = false;
            work.StateBeforePause = null;
            work.WaitingReason = null;
            work.NextAttemptAt = null;
            work.FinishedAt = now;
        }

        await database.SaveChangesAsync(cancellationToken);
        return new BackgroundWorkActionResult(BackgroundWorkActionVerdict.Accepted);
    }

    /// <summary>
    /// Applies `Retry now` or `Check again` to one unresolved Work Issue. The issue is not closed
    /// here: ownership returns to Automatic Recovery and the blocked work is queued again, and only
    /// its successful continuation supplies the Resolution Evidence that closes it.
    /// </summary>
    public async Task<BackgroundWorkActionResult> AdvanceIssueAsync(
        Guid workIssueId,
        int version,
        WorkIssueAction action,
        CancellationToken cancellationToken = default)
    {
        var issue = await database.WorkIssues
            .AsTracking()
            .SingleOrDefaultAsync(row => row.Id == workIssueId, cancellationToken);

        if (issue is null)
        {
            return new BackgroundWorkActionResult(BackgroundWorkActionVerdict.NotFound);
        }

        var directory = await database.LibraryDirectories
            .SingleOrDefaultAsync(row => row.Id == issue.LibraryDirectoryId, cancellationToken);

        if (issue.ResolvedAt is not null ||
            issue.Version != version ||
            directory is null ||
            directory.ConfigurationGeneration != issue.ConfigurationGeneration)
        {
            return new BackgroundWorkActionResult(
                BackgroundWorkActionVerdict.Stale,
                BackgroundWorkQuery.Describe(issue));
        }

        var offered = WorkIssueRule.ActionsFor(
            issue.Cause,
            issue.Severity,
            issue.RetryDisposition,
            issue.AffectedItemCount > 0);

        if (!offered.Contains(action) ||
            action is not (WorkIssueAction.RetryNow or WorkIssueAction.CheckAgain))
        {
            return new BackgroundWorkActionResult(
                BackgroundWorkActionVerdict.NotApplicable,
                BackgroundWorkQuery.Describe(issue));
        }

        var now = Now();

        if (issue.Cause == WorkIssueCause.Capacity)
        {
            var storage = artifacts.CheckWritable();

            if (!storage.Succeeded)
            {
                issue.SafeCause = storage.SafeCause ?? issue.SafeCause;
                issue.OccurrenceCount++;
                issue.LastOccurredAt = now;
                issue.Version++;
                await database.SaveChangesAsync(cancellationToken);
                return new BackgroundWorkActionResult(
                    BackgroundWorkActionVerdict.Accepted,
                    BackgroundWorkQuery.Describe(issue));
            }
        }

        issue.RemediationOwner = RemediationOwner.AutomaticRecovery;
        issue.RetryDisposition = WorkIssueRetryDisposition.AutomaticRetryScheduled;
        issue.AttemptedRetries++;
        issue.NextAttemptAt = now;
        issue.Version++;
        await RequeueAsync(issue, directory, now, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        return new BackgroundWorkActionResult(
            BackgroundWorkActionVerdict.Accepted,
            BackgroundWorkQuery.Describe(issue));
    }

    /// <summary>
    /// Queues the work the issue actually blocks. Discovery and inspection depend on a fresh
    /// traversal, so they are reattempted through a Library Scan, while a derived lane can be
    /// offered its own outstanding items again.
    /// </summary>
    private async Task RequeueAsync(
        WorkIssueRow issue,
        LibraryDirectoryRow directory,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (issue.Category is BackgroundWorkCategory.LibraryScan
            or BackgroundWorkCategory.TechnicalInspection)
        {
            await scheduler.QueueScanAsync(
                directory.Id,
                BackgroundWorkTrigger.IssueRetry,
                cancellationToken);
            return;
        }

        await DerivedWorkQueue.QueueAsync(
            database,
            directory.Id,
            directory.ConfigurationGeneration,
            issue.Category,
            BackgroundWorkTrigger.IssueRetry,
            now,
            cancellationToken);
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
}
