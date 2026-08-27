using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

public sealed record VideoDelivery(
    Stream Content,
    string ContentType,
    DateTimeOffset LastModified,
    string DownloadName);

public sealed class VideoDeliveryService(ViewerDbContext database)
{
    public async Task<VideoDelivery?> OpenAsync(
        Guid publicDeliveryId,
        CancellationToken cancellationToken = default)
    {
        var videoFile = await database.VideoFiles
            .AsNoTracking()
            .Include(file => file.LibraryDirectory)
            .SingleOrDefaultAsync(
                file => file.PublicDeliveryId == publicDeliveryId &&
                        file.Availability == VideoFileAvailability.Available,
                cancellationToken);

        if (videoFile is null)
        {
            return null;
        }

        var path = SafePath(videoFile.LibraryDirectory.ContainerPath, videoFile.RelativePath);

        if (path is null)
        {
            return null;
        }

        try
        {
            var file = new FileInfo(path);

            if (file.Length != videoFile.Size || file.LastWriteTimeUtc != videoFile.LastWriteTimeUtc)
            {
                return null;
            }

            var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                1024 * 128,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return new VideoDelivery(
                stream,
                ContentType(videoFile.ContainerFormat, videoFile.RelativePath),
                new DateTimeOffset(DateTime.SpecifyKind(videoFile.LastWriteTimeUtc, DateTimeKind.Utc)),
                Path.GetFileName(videoFile.RelativePath));
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static string? SafePath(string root, string relativePath)
    {
        try
        {
            var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            var path = Path.GetFullPath(Path.Combine(
                normalizedRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            return path.StartsWith($"{normalizedRoot}{Path.DirectorySeparatorChar}", comparison) &&
                   File.Exists(path) &&
                   (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0
                ? path
                : null;
        }
        catch (Exception exception) when (exception is ArgumentException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static string ContentType(string containerFormat, string relativePath)
    {
        var formats = containerFormat.Split(',', StringSplitOptions.TrimEntries);

        if (formats.Contains("webm"))
        {
            return "video/webm";
        }

        if (formats.Any(format => format is "mov" or "mp4" or "m4a" or "3gp" or "3g2" or "mj2"))
        {
            return "video/mp4";
        }

        return Path.GetExtension(relativePath).ToLowerInvariant() switch
        {
            ".mkv" => "video/x-matroska",
            ".avi" => "video/x-msvideo",
            ".mpeg" or ".mpg" => "video/mpeg",
            ".ts" or ".m2ts" => "video/mp2t",
            ".wmv" => "video/x-ms-wmv",
            _ => "application/octet-stream",
        };
    }
}
