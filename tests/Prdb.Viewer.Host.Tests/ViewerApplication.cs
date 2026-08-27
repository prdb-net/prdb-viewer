using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Viewer.Infrastructure.Access;

using Xunit;

namespace Prdb.Viewer.Host.Tests;

internal sealed class ViewerApplication : WebApplicationFactory<Program>
{
    private readonly string dataDirectory = Path.Combine(
        Path.GetTempPath(),
        $"prdb-viewer-host-{Guid.NewGuid():n}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("VIEWER_DATA_DIRECTORY", dataDirectory);
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
    }
}
