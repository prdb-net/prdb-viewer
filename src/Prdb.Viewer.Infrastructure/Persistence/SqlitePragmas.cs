using System.Data.Common;

namespace Prdb.Viewer.Infrastructure.Persistence;

internal static class SqlitePragmas
{
    private const string ConnectionPragmas = """
        PRAGMA synchronous=FULL;
        PRAGMA busy_timeout=5000;
        PRAGMA foreign_keys=ON;
        """;

    public static void Apply(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = ConnectionPragmas;
        command.ExecuteNonQuery();
    }

    public static async Task ApplyAsync(
        DbConnection connection,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = ConnectionPragmas;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
