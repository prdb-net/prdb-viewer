using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// The installation-wide controls every lane obeys before it claims more work: the administrative
/// pause and the cancellation of one bounded run. Both take effect at a safe durable boundary, so
/// results already committed are kept and no incomplete observation becomes a settled outcome.
/// </summary>
internal static class BackgroundWorkGate
{
    private static readonly BackgroundWorkState[] Stoppable =
    [
        BackgroundWorkState.Queued,
        BackgroundWorkState.Running,
        BackgroundWorkState.Waiting,
    ];

    public static Task<bool> IsPausedAsync(
        ViewerDbContext database,
        CancellationToken cancellationToken) =>
        database.InstallationConfigurations.AnyAsync(
            configuration => configuration.BackgroundWorkPaused,
            cancellationToken);

    /// <summary>
    /// Parks this category's runs in Paused, remembering the lifecycle each one had. Resuming
    /// therefore continues from retained checkpoints instead of starting a duplicate run.
    /// </summary>
    public static async Task<bool> ParkAsync(
        ViewerDbContext database,
        BackgroundWorkCategory category,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var running = await database.BackgroundWork
            .AsTracking()
            .Where(work => work.Category == category && Stoppable.Contains(work.State))
            .ToListAsync(cancellationToken);

        if (running.Count == 0)
        {
            return false;
        }

        foreach (var work in running)
        {
            work.StateBeforePause = work.State == BackgroundWorkState.Running
                ? BackgroundWorkState.Queued
                : work.State;
            work.State = BackgroundWorkState.Paused;
            work.UpdatedAt = now;
        }

        await database.SaveChangesAsync(cancellationToken);
        return true;
    }
}
