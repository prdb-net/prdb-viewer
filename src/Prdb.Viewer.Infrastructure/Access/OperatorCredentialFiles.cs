using System.Text;

using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Access;

public sealed class OperatorCredentialFiles(ViewerDatabaseLocation location)
{
    private const UnixFileMode OwnerReadWrite = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public async Task<string> WriteAsync(
        string fileName,
        string value,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(location.DataDirectory, "operator");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, fileName);
        var temporaryPath = Path.Combine(directory, $".{fileName}.{Guid.NewGuid():n}.tmp");

        try
        {
            await WriteRestrictedAsync(temporaryPath, value, cancellationToken);
            RestrictToOwner(temporaryPath);
            File.Move(temporaryPath, path, overwrite: true);
            RestrictToOwner(path);
            return path;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public void Delete(string? path)
    {
        if (path is null)
        {
            return;
        }

        var operatorDirectory = Path.GetFullPath(Path.Combine(location.DataDirectory, "operator"));
        var fullPath = Path.GetFullPath(path);

        if (!fullPath.StartsWith($"{operatorDirectory}{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("An operator credential path escaped application data.");
        }

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    private static void RestrictToOwner(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, OwnerReadWrite);
        }
    }

    private static async Task WriteRestrictedAsync(
        string path,
        string value,
        CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            await File.WriteAllTextAsync(path, $"{value}{Environment.NewLine}", cancellationToken);
            return;
        }

        await using var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous,
            UnixCreateMode = OwnerReadWrite,
        });
        await stream.WriteAsync(
            Encoding.UTF8.GetBytes($"{value}{Environment.NewLine}"),
            cancellationToken);
    }
}
