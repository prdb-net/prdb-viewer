using Microsoft.Data.Sqlite;

namespace Prdb.Viewer.Infrastructure.Persistence;

public sealed class ViewerDatabaseLocation
{
    public ViewerDatabaseLocation(string dataDirectory)
    {
        DataDirectory = Path.GetFullPath(dataDirectory);
        FilePath = Path.Combine(DataDirectory, "prdb-viewer.db");
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = FilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            Pooling = true,
        }.ToString();
    }

    public string DataDirectory { get; }

    public string FilePath { get; }

    public string ConnectionString { get; }

    public void EnsureDirectoryExists() => Directory.CreateDirectory(DataDirectory);
}
