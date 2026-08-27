using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Viewer.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests;

public sealed class SqlitePersistenceTests
{
    [Fact]
    public async Task Startup_creates_and_migrates_the_database()
    {
        await using var database = await TestDatabase.CreateAsync();

        Assert.True(File.Exists(database.Location.FilePath));

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
        Assert.Contains(
            "20260827000000_Initial",
            await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Every_context_connection_uses_the_required_pragmas()
    {
        await using var database = await TestDatabase.CreateAsync();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var scope = database.Scope();
            var context = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();

            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);

            Assert.Equal("wal", await ScalarAsync(context, "PRAGMA journal_mode;"));
            Assert.Equal(2L, await ScalarAsync(context, "PRAGMA synchronous;"));
            Assert.Equal(5000L, await ScalarAsync(context, "PRAGMA busy_timeout;"));
            Assert.Equal(1L, await ScalarAsync(context, "PRAGMA foreign_keys;"));

            await context.Database.CloseConnectionAsync();
        }
    }

    private static async Task<object?> ScalarAsync(ViewerDbContext context, string sql)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        return value is string text ? text : Convert.ToInt64(value);
    }
}
