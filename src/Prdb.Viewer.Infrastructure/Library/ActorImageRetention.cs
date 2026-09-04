using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// Brings the pictures prdb offers for an Actor into application storage, a bounded few at a time,
/// so that an Actor's page shows them from this installation's own origin.
/// </summary>
/// <remarks>
/// A page about a person that draws a placeholder is a page nobody opens twice, which is why every
/// picture is held rather than only the Portrait. A picture that does not arrive is not a Work
/// Issue for the same reason a proposed work's is not: the Actor is named, their Videos are
/// listed, and the gallery says what has not arrived.
/// </remarks>
public sealed class ActorImageRetention(
    ViewerDbContext database,
    IRetainedImageClient client,
    DerivedArtifactStore artifacts,
    TimeProvider timeProvider)
{
    /// <summary>How many pictures one bounded run brings in before it gives the lane back.</summary>
    private const int BatchSize = 25;

    public async Task RetainAsync(CancellationToken cancellationToken = default)
    {
        var outstanding = await database.ActorImages
            .AsTracking()
            .Where(image => image.State == ActorImageState.Pending)
            .OrderBy(image => image.ActorId)
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
            var relativePath = DerivedArtifactStore.ActorImageRelativePath(image.Id, contentType);

            try
            {
                artifacts.EnsureActorImageDirectory(relativePath);
                await File.WriteAllBytesAsync(
                    artifacts.ActorImageFullPath(relativePath),
                    fetched.Content,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Storage refusing a regenerable artefact is the Preview Generation lane's issue to
                // raise, not this one's; it writes far more, and far more often, than this does.
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

    /// <summary>Whether any picture is still waiting to be brought in.</summary>
    public Task<bool> AnyOutstandingAsync(CancellationToken cancellationToken = default) =>
        database.ActorImages.AnyAsync(
            image => image.State == ActorImageState.Pending,
            cancellationToken);
}
