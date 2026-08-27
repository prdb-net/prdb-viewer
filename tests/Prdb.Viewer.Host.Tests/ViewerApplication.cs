using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

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
