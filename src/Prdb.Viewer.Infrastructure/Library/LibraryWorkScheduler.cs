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
        database.BackgroundWork.Add(
            NewScan(directory, BackgroundWorkTrigger.Activation, now));
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

        if (current is not null)
        {
            current.FollowUpRequested = true;
            current.UpdatedAt = Now();
            await database.SaveChangesAsync(cancellationToken);
            return new QueueLibraryScanResult(QueueLibraryScanVerdict.Coalesced, current.Id);
        }

        var scan = NewScan(directory, trigger, Now());
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
