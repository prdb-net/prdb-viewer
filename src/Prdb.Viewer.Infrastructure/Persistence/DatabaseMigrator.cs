using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Prdb.Viewer.Infrastructure.Persistence;

public sealed class DatabaseMigrator(
    ViewerDbContext context,
    ViewerDatabaseLocation location,
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
