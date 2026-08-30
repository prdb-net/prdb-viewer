using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Prdb.FakeCatalogue;

using Prdb.Viewer.Infrastructure.Access;
using Prdb.Viewer.Infrastructure.Configuration;

using Xunit;

namespace Prdb.Viewer.Host.Tests;

internal sealed class ViewerApplication(
    IPrdbConnectionVerifier? prdbConnectionVerifier = null,
    bool behindReverseProxy = false,
    FakePrdb? prdb = null)
    : WebApplicationFactory<Program>
{
    private readonly string dataDirectory = Path.Combine(
        Path.GetTempPath(),
        $"prdb-viewer-host-{Guid.NewGuid():n}");
    private readonly string libraryMountRoot = Path.Combine(
        Path.GetTempPath(),
        $"prdb-viewer-libraries-{Guid.NewGuid():n}");

    public string LibraryMountRoot => libraryMountRoot;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("VIEWER_DATA_DIRECTORY", dataDirectory);
        builder.UseSetting("VIEWER_BACKGROUND_WORK_ENABLED", "false");
        builder.UseSetting(
            "VIEWER_BEHIND_REVERSE_PROXY",
            behindReverseProxy ? "true" : "false");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<LibraryMountRoot>();
            services.AddSingleton(new LibraryMountRoot(libraryMountRoot));

            if (prdbConnectionVerifier is not null)
            {
                services.RemoveAll<IPrdbConnectionVerifier>();
                services.AddSingleton(prdbConnectionVerifier);
            }

            if (prdb is not null)
            {
                // The verifier stays the one that ships and only the socket beneath it is
                // replaced, so what a route answers is decided by a status code and a body rather
                // than by a stand-in that was told what to conclude.
                services.RemoveAll<IHttpMessageHandlerFactory>();
                services.AddSingleton<IHttpMessageHandlerFactory>(new FakePrdbTransport(prdb));
            }
        });
    }

    public async Task<string> CreateBootstrapAuthorizationAsync()
    {
        _ = Server;
        await using var scope = Services.CreateAsyncScope();
        var result = await scope.ServiceProvider
            .GetRequiredService<AccessService>()
            .CreateBootstrapAuthorizationAsync(TestContext.Current.CancellationToken);

        return (await File.ReadAllTextAsync(
            result.DeliveryPath!,
            TestContext.Current.CancellationToken)).Trim();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && Directory.Exists(dataDirectory))
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dataDirectory, recursive: true);
        }

        if (disposing && Directory.Exists(libraryMountRoot))
        {
            Directory.Delete(libraryMountRoot, recursive: true);
        }
    }
}
