using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Prdb.Viewer.Infrastructure.Persistence;
using Prdb.Viewer.Infrastructure.Configuration;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Tests.Library;

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

    public static async Task<TestDatabase> CreateAsync(
        TimeProvider? timeProvider = null,
        IPrdbConnectionVerifier? prdbConnectionVerifier = null,
        IMediaProbe? mediaProbe = null,
        IVideoFileHasher? hasher = null,
        IPreviewImageGenerator? previewGenerator = null,
        IPrdbIdentificationClient? identificationClient = null,
        IPrdbSiteDirectoryClient? siteDirectoryClient = null,
        FakePrdb? prdb = null,
        string? targetMigration = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"prdb-viewer-{Guid.NewGuid():n}");
        var libraryMountRoot = Path.Combine(directory, "libraries");
        System.IO.Directory.CreateDirectory(libraryMountRoot);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(timeProvider ?? TimeProvider.System);
        services.AddViewerPersistence(directory, libraryMountRoot);

        if (prdbConnectionVerifier is not null)
        {
            services.AddSingleton(prdbConnectionVerifier);
        }

        if (mediaProbe is not null)
        {
            services.RemoveAll<IMediaProbe>();
            services.AddSingleton(mediaProbe);
        }

        if (hasher is not null)
        {
            services.RemoveAll<IVideoFileHasher>();
            services.AddSingleton(hasher);
        }

        if (previewGenerator is not null)
        {
            services.RemoveAll<IPreviewImageGenerator>();
            services.AddSingleton(previewGenerator);
        }

        if (identificationClient is not null)
        {
            services.RemoveAll<IPrdbIdentificationClient>();
            services.AddSingleton(identificationClient);
        }

        if (prdb is not null)
        {
            // Both prdb clients stay the ones that ship, and only the socket underneath them is
            // replaced. A test that asks for this is testing the seam between the lanes and the
            // wire — what the SDK sends, and what the lanes make of a real reply — which a
            // hand-written stand-in for the client interface cannot reach.
            services.RemoveAll<IHttpMessageHandlerFactory>();
            services.AddSingleton<IHttpMessageHandlerFactory>(new FakePrdbTransport(prdb));
        }

        if (siteDirectoryClient is not null || prdb is null)
        {
            // No test ever reaches prdb.net: a test that says nothing about the Site Directory
            // gets an installation whose directory is simply empty.
            services.RemoveAll<IPrdbSiteDirectoryClient>();
            services.AddSingleton(siteDirectoryClient ?? new FixtureSiteDirectoryClient());
        }

        var database = new TestDatabase(directory, services.BuildServiceProvider());
        if (targetMigration is null)
        {
            await database.provider.PrepareViewerDatabaseAsync(TestContext.Current.CancellationToken);
        }
        else
        {
            await database.MigrateAsync(targetMigration);
        }

        return database;
    }

    public AsyncServiceScope Scope() => provider.CreateAsyncScope();

    public LibraryMountRoot LibraryMountRoot => provider.GetRequiredService<LibraryMountRoot>();

    public async Task MigrateAsync(string? targetMigration = null)
    {
        await using var scope = Scope();
        var context = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        await context.GetService<IMigrator>().MigrateAsync(
            targetMigration,
            TestContext.Current.CancellationToken);
    }

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
