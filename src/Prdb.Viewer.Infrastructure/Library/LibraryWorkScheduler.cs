using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

public sealed class LibraryWorkScheduler(ViewerDbContext database, TimeProvider timeProvider)
{
    private static readonly BackgroundWorkState[] ActiveStates =
    [
        BackgroundWorkState.Queued,
        BackgroundWorkState.Running,
        BackgroundWorkState.Waiting,
        BackgroundWorkState.Paused,
    ];

    public void QueueInitialScan(LibraryDirectoryRow directory, DateTime now)
    {
        directory.InitialProcessingStartedAt ??= now;
        directory.NextScanDueAt = LibraryScanSchedule.NextDueAfter(now);
        database.BackgroundWork.Add(
            NewScan(directory, BackgroundWorkTrigger.Activation, now));
    }

    /// <summary>
    /// Queues a Library Scan for every Active Library Directory that has reached the time its next
    /// one was due, so a file added to the mounted library becomes a Video without anyone asking.
    ///
    /// Due-ness is read from durable state rather than from a timer this process holds: an
    /// installation that was down over its period finds the Scan due on the next start, and one
    /// that was restarted twice within it does not scan twice.
    /// </summary>
    public async Task<bool> QueueDueScansAsync(CancellationToken cancellationToken = default)
    {
        var now = Now();
        var due = await database.LibraryDirectories
            .AsNoTracking()
            .Where(row => row.State == LibraryDirectoryState.Active && row.NextScanDueAt <= now)
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);

        foreach (var directory in due)
        {
            await QueueScanAsync(directory, BackgroundWorkTrigger.Periodic, cancellationToken);
        }

        return due.Count > 0;
    }

    public async Task<QueueLibraryScanResult> QueueScanAsync(
        Guid libraryDirectoryId,
        BackgroundWorkTrigger trigger = BackgroundWorkTrigger.Administrator,
        CancellationToken cancellationToken = default)
    {
        var directory = await database.LibraryDirectories
            .AsTracking()
            .SingleOrDefaultAsync(
                row => row.Id == libraryDirectoryId && row.State == LibraryDirectoryState.Active,
                cancellationToken);

        if (directory is null)
        {
            return new QueueLibraryScanResult(QueueLibraryScanVerdict.NotFound);
        }

        var current = await database.BackgroundWork
            .AsTracking()
            .Where(work => work.LibraryDirectoryId == libraryDirectoryId &&
                           work.Category == BackgroundWorkCategory.LibraryScan &&
                           ActiveStates.Contains(work.State))
            .OrderByDescending(work => work.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);

        // Any Scan that starts now is the observation the period is counted from, whatever asked
        // for it. Recording that here rather than only when the Scan settles is what stops a run
        // that never reaches its end — a cancellation, a restart mid-traversal — from leaving a
        // Library Directory permanently due and scanning it in a loop.
        var now = Now();
        directory.NextScanDueAt = LibraryScanSchedule.NextDueAfter(now);

        if (current is not null)
        {
            current.FollowUpRequested = true;
            current.UpdatedAt = now;
            await database.SaveChangesAsync(cancellationToken);
            return new QueueLibraryScanResult(QueueLibraryScanVerdict.Coalesced, current.Id);
        }

        var scan = NewScan(directory, trigger, now);
        directory.InitialProcessingStartedAt ??= scan.RequestedAt;
        database.BackgroundWork.Add(scan);
        await database.SaveChangesAsync(cancellationToken);
        return new QueueLibraryScanResult(QueueLibraryScanVerdict.Queued, scan.Id);
    }

    private static BackgroundWorkRow NewScan(
        LibraryDirectoryRow directory,
        BackgroundWorkTrigger trigger,
        DateTime now)
    {
        var id = Guid.CreateVersion7();
        return new BackgroundWorkRow
        {
            Id = id,
            LibraryScanId = id,
            Category = BackgroundWorkCategory.LibraryScan,
            State = BackgroundWorkState.Queued,
            Trigger = trigger,
            Phase = BackgroundWorkPhases.Queued,
            LibraryDirectoryId = directory.Id,
            LibraryDirectory = directory,
            ConfigurationGeneration = directory.ConfigurationGeneration,
            PendingDirectoriesJson = JsonSerializer.Serialize(new[] { string.Empty }),
            CoverageComplete = true,
            RequestedAt = now,
            UpdatedAt = now,
        };
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
}
