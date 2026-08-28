using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Viewer.Infrastructure.Library;

namespace Prdb.Viewer.Infrastructure.Persistence;

public sealed class DatabaseMigrator(
    ViewerDbContext context,
    ViewerDatabaseLocation location,
    VideoProjection projection,
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
