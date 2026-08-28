using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Library;

namespace Prdb.Viewer.Infrastructure.Persistence;

public sealed class DatabaseMigrator(
    ViewerDbContext context,
    ViewerDatabaseLocation location,
    VideoProjection projection,
    LibraryWorkScheduler scheduler,
    ILogger<DatabaseMigrator> logger)
{
    public async Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            location.EnsureDirectoryExists();
            await EstablishWriteAheadLoggingAsync(cancellationToken);
            await context.Database.MigrateAsync(cancellationToken);
            location.RestrictDatabaseFiles();
            await BuildOutstandingProjectionsAsync(cancellationToken);
            await ScheduleReinspectionAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogCritical(
                exception,
                "The database at {Database} could not be migrated. The application will stop.",
                location.FilePath);

            throw new DatabaseMigrationException(
                $"The database at {location.FilePath} could not be migrated.",
                exception);
        }
    }

    /// <summary>
    /// Builds the discovery projections an upgrade or a Restore left outstanding, in bounded
    /// batches. Serving discovery from a half-built projection would hide Videos that exist, so
    /// this finishes before the application starts answering.
    /// </summary>
    private async Task BuildOutstandingProjectionsAsync(CancellationToken cancellationToken)
    {
        var batches = 0;

        while (await projection.RefreshOutstandingAsync(cancellationToken: cancellationToken))
        {
            batches++;
            context.ChangeTracker.Clear();
        }

        if (batches > 0)
        {
            logger.LogInformation(
                "Built discovery projections for {Batches} batch(es) of Videos.",
                batches);
        }
    }

    /// <summary>
    /// Queues one Library Scan per Active Library Directory when Video Files are missing the exact
    /// media configuration a client is qualified against.
    ///
    /// The direct-play contract requires classifications to be reconsidered when the supported-
    /// client rules change, and the facts the new rules need — profile, level, bit depth, frame
    /// rate — were never inspected before. Until a file is inspected again it keeps the
    /// classification it already had, so nothing disappears from the library while this runs.
    /// </summary>
    private async Task ScheduleReinspectionAsync(CancellationToken cancellationToken)
    {
        var outstanding = await context.VideoFiles.AnyAsync(
            file => file.Availability == VideoFileAvailability.Available && file.ProfileKey == "",
            cancellationToken);

        if (!outstanding)
        {
            return;
        }

        var directories = await context.LibraryDirectories
            .AsNoTracking()
            .Where(directory => directory.State == LibraryDirectoryState.Active)
            .Select(directory => directory.Id)
            .ToListAsync(cancellationToken);

        foreach (var directory in directories)
        {
            await scheduler.QueueScanAsync(
                directory,
                BackgroundWorkTrigger.FollowUpWork,
                cancellationToken);
        }

        if (directories.Count > 0)
        {
            logger.LogInformation(
                "Queued {Count} Library Scan(s) to inspect the media facts client qualification " +
                "needs.",
                directories.Count);
        }
    }

    private async Task EstablishWriteAheadLoggingAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(location.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await SqlitePragmas.ApplyAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL;";
        var mode = await command.ExecuteScalarAsync(cancellationToken) as string;

        if (!string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"SQLite selected journal mode '{mode ?? "unknown"}' instead of WAL.");
        }
    }
}
