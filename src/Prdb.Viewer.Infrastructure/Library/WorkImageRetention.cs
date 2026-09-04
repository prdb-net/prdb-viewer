using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// Brings the pictures prdb offers for an Established Work into application storage, a bounded few
/// at a time, so a Video's page can show what the catalogue holds of it beside the preview this
/// installation generated from the file it actually has.
/// </summary>
/// <remarks>
/// It is <see cref="ActorImageRetention"/> against a different table, deliberately rather than
/// generically: the two differ in nothing but where the rows live, and a shared abstraction over
/// two callers would cost more to read than the twenty lines it saved.
/// </remarks>
public sealed class WorkImageRetention(
    ViewerDbContext database,
    IRetainedImageClient client,
    DerivedArtifactStore artifacts,
    TimeProvider timeProvider)
{
    private const int BatchSize = 25;

    public async Task RetainAsync(CancellationToken cancellationToken = default)
    {
        var outstanding = await database.VideoImages
            .AsTracking()
            .Where(image => image.State == ActorImageState.Pending)
            .OrderBy(image => image.VideoId)
            .ThenBy(image => image.Position)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var image in outstanding)
        {
            var fetched = await client.FetchAsync(image.SourceUrl, cancellationToken);

            if (fetched.Content is null)
            {
                image.State = ActorImageState.Unavailable;
                continue;
            }

            var contentType = fetched.ContentType ?? "image/jpeg";
            var relativePath = DerivedArtifactStore.WorkImageRelativePath(image.Id, contentType);

            try
            {
                artifacts.EnsureWorkImageDirectory(relativePath);
                await File.WriteAllBytesAsync(
                    artifacts.WorkImageFullPath(relativePath),
                    fetched.Content,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                image.State = ActorImageState.Unavailable;
                continue;
            }

            image.RelativePath = relativePath;
            image.ContentType = contentType;
            image.PublicImageId ??= Guid.CreateVersion7();
            image.RetainedAt = timeProvider.GetUtcNow().UtcDateTime;
            image.State = ActorImageState.Retained;
        }

        if (outstanding.Count > 0)
        {
            await database.SaveChangesAsync(cancellationToken);
        }
    }
}
