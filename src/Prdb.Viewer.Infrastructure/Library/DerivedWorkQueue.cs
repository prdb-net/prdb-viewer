using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// Queues the derived lanes that follow technical inspection. Each lane is durable, bounded, and
/// visible on its own, so hashing, preview generation, and identification can fail, wait, or be
/// retried without holding up browsing or direct playback.
/// </summary>
internal static class DerivedWorkQueue
{
    private static readonly BackgroundWorkState[] ActiveStates =
    [
        BackgroundWorkState.Queued,
        BackgroundWorkState.Running,
        BackgroundWorkState.Waiting,
        BackgroundWorkState.Paused,
    ];

    public static async Task QueueAsync(
        ViewerDbContext database,
        Guid libraryDirectoryId,
        int configurationGeneration,
        BackgroundWorkCategory category,
        BackgroundWorkTrigger trigger,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var running = await database.BackgroundWork
            .AsTracking()
            .Where(work => work.LibraryDirectoryId == libraryDirectoryId &&
                           work.Category == category &&
                           ActiveStates.Contains(work.State))
            .OrderByDescending(work => work.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (running is not null)
        {
            running.FollowUpRequested = true;
            running.ConfigurationGeneration = configurationGeneration;

            if (running.State == BackgroundWorkState.Waiting)
            {
                running.State = BackgroundWorkState.Queued;
                running.NextAttemptAt = null;
                running.WaitingReason = null;
            }

            running.UpdatedAt = now;
            return;
        }

        database.BackgroundWork.Add(new BackgroundWorkRow
        {
            Id = Guid.CreateVersion7(),
            Category = category,
            State = BackgroundWorkState.Queued,
            Trigger = trigger,
            Phase = BackgroundWorkPhases.Queued,
            LibraryDirectoryId = libraryDirectoryId,
            ConfigurationGeneration = configurationGeneration,
            RequestedAt = now,
            UpdatedAt = now,
        });
    }
}
