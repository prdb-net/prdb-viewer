using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// Brings the pictures of proposed works into application storage, a bounded few at a time, so
/// that a review case shows what it is comparing against without a live prdb request.
/// </summary>
/// <remarks>
/// A picture that does not arrive is not a Work Issue. Identification itself has succeeded — the
/// proposal exists and can be decided on its words — so an Operational Blocker here would call an
/// Administrator to a lane that is not blocked. The candidate records that its picture is
/// Unavailable, and the review screen says so beside the proposal, which is where it matters.
/// </remarks>
public sealed class ProposedWorkArtworkRetention(
    ViewerDbContext database,
    IRetainedImageClient client,
    DerivedArtifactStore artifacts,
    TimeProvider timeProvider)
{
    /// <summary>How many pictures one bounded run brings in before it gives the lane back.</summary>
    private const int BatchSize = 25;

    public async Task RetainAsync(CancellationToken cancellationToken = default)
    {
        var outstanding = await database.ProposedWorks
            .AsTracking()
            .Where(work => work.ArtworkState == ProposedWorkArtworkState.Pending &&
                           work.ArtworkUrl != null)
            .OrderBy(work => work.FetchedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var work in outstanding)
        {
            var fetched = await client.FetchAsync(work.ArtworkUrl!, cancellationToken);

            if (fetched.Content is null)
            {
                work.ArtworkState = ProposedWorkArtworkState.Unavailable;
                continue;
            }

            var contentType = fetched.ContentType ?? "image/jpeg";
            var relativePath = DerivedArtifactStore.ArtworkRelativePath(work.Id, contentType);

            try
            {
                artifacts.EnsureArtworkDirectory(relativePath);
                await File.WriteAllBytesAsync(
                    artifacts.ArtworkFullPath(relativePath),
                    fetched.Content,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Storage refusing a regenerable artefact is the Preview Generation lane's issue to
                // raise, not this one's; it writes far more, and far more often, than this does.
                work.ArtworkState = ProposedWorkArtworkState.Unavailable;
                continue;
            }

            work.ArtworkRelativePath = relativePath;
            work.ArtworkContentType = contentType;
            work.PublicArtworkId ??= Guid.CreateVersion7();
            work.ArtworkRetainedAt = timeProvider.GetUtcNow().UtcDateTime;
            work.ArtworkState = ProposedWorkArtworkState.Retained;
        }

        if (outstanding.Count > 0)
        {
            await database.SaveChangesAsync(cancellationToken);
        }
    }
}
