using System.Text;

using Microsoft.Extensions.Logging;

using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Access;

public sealed class OperatorCredentialFiles(
    ViewerDatabaseLocation location,
    ILogger<OperatorCredentialFiles> logger)
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

    /// <summary>
    /// Removes a spent single-use credential file. This is cleanup after the durable work already
    /// committed, so a file the application cannot remove — one an Operator generated as another
    /// identity, most often — is reported rather than allowed to undo a completed Account action.
    /// The credential it held is already spent: its record is gone, so the file cannot be redeemed.
    /// </summary>
    public void Delete(string? path)
    {
        if (path is null)
        {
            return;
        }

        var operatorDirectory = Path.GetFullPath(Path.Combine(location.DataDirectory, "operator"));
        var fullPath = Path.GetFullPath(path);

        // A path outside application data is a broken invariant rather than a condition of the
        // host, so it still stops the application rather than being reported and passed over.
        if (!fullPath.StartsWith($"{operatorDirectory}{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("An operator credential path escaped application data.");
        }

        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                "The spent operator credential at {Path} could not be removed. It can no longer be " +
                "redeemed, but delete it. It was most likely created by a different identity than " +
                "the one the application runs as; restarting the container restores ownership of " +
                "the application data directory.",
                fullPath);
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
