using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Viewer.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests;

internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly ServiceProvider provider;

    private TestDatabase(string directory, ServiceProvider provider)
    {
        Directory = directory;
        this.provider = provider;
    }

    public string Directory { get; }

    public ViewerDatabaseLocation Location => provider.GetRequiredService<ViewerDatabaseLocation>();

    public static async Task<TestDatabase> CreateAsync(TimeProvider? timeProvider = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"prdb-viewer-{Guid.NewGuid():n}");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(timeProvider ?? TimeProvider.System);
        services.AddViewerPersistence(directory);

        var database = new TestDatabase(directory, services.BuildServiceProvider());
        await database.provider.PrepareViewerDatabaseAsync(TestContext.Current.CancellationToken);
        return database;
    }

    public AsyncServiceScope Scope() => provider.CreateAsyncScope();

    public async ValueTask DisposeAsync()
    {
        await provider.DisposeAsync();
        SqliteConnection.ClearAllPools();

        if (System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}
