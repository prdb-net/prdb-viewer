using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

public sealed record PreviewDelivery(Stream Content, string ContentType, DateTimeOffset LastModified);

/// <summary>
/// Serves the locally generated preview images. Like video delivery, a preview is addressed by a
/// random, non-enumerable identifier rather than by a path or a database key.
/// </summary>
public sealed class PreviewDeliveryService(
    ViewerDbContext database,
    DerivedArtifactStore artifacts)
{
    public async Task<PreviewDelivery?> OpenAsync(
        Guid publicPreviewId,
        CancellationToken cancellationToken = default)
    {
        var preview = await database.VideoFiles
            .AsNoTracking()
            .Where(file => file.PublicPreviewId == publicPreviewId &&
                           file.PreviewState == VideoFilePreviewState.Generated &&
                           file.PreviewRelativePath != null)
            .Select(file => new
            {
                RelativePath = file.PreviewRelativePath!,
                file.PreviewGeneratedAt,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (preview is null)
        {
            return null;
        }

        var path = Path.GetFullPath(artifacts.PreviewFullPath(preview.RelativePath));
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(artifacts.PreviewsRoot));

        if (!path.StartsWith($"{root}{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return new PreviewDelivery(
                new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    1024 * 32,
                    FileOptions.Asynchronous | FileOptions.SequentialScan),
                "image/jpeg",
                VideoPresentation.AsOffset(preview.PreviewGeneratedAt) ?? DateTimeOffset.MinValue);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
