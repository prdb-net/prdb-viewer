using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>What one attempt at a proposed work's picture produced.</summary>
public sealed record ArtworkFetch(byte[]? Content, string? ContentType);

public interface IProposedWorkArtworkClient
{
    Task<ArtworkFetch> FetchAsync(string url, CancellationToken cancellationToken = default);
}

/// <summary>
/// Fetches the picture prdb offers for a proposed work over the credential-free artwork transport
/// (ADR 0010): one transport attempt, no retry of its own, and no installation credential on the
/// wire — so a redirect may be followed because nothing is carried that could leak.
/// </summary>
public sealed class ProposedWorkArtworkClient(IHttpMessageHandlerFactory handlers)
    : IProposedWorkArtworkClient
{
    public const string TransportName = "prdb-artwork";

    /// <summary>
    /// What a review case is worth holding. A picture larger than this is refused rather than
    /// truncated: a half-written frame is worse than the placeholder the screen already draws.
    /// </summary>
    private const int MaximumBytes = 8 * 1024 * 1024;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// What may be retained and served back under this installation's own origin. It is a list of
    /// what is allowed rather than of what is not, because the risk is the picture format that
    /// carries markup: an SVG served from our own address is a document in our own origin, and a
    /// catalogue is not a place to accept one from.
    /// </summary>
    private static readonly HashSet<string> Retainable = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif",
        "image/avif",
        "image/bmp",
    };

    public async Task<ArtworkFetch> FetchAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var address) ||
            (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
        {
            return new ArtworkFetch(null, null);
        }

        using var client = new HttpClient(handlers.CreateHandler(TransportName), disposeHandler: false)
        {
            Timeout = RequestTimeout,
        };

        try
        {
            using var response = await client.GetAsync(
                address,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new ArtworkFetch(null, null);
            }

            var type = response.Content.Headers.ContentType?.MediaType;

            if (type is null || !Retainable.Contains(type))
            {
                return new ArtworkFetch(null, null);
            }

            if (response.Content.Headers.ContentLength > MaximumBytes)
            {
                return new ArtworkFetch(null, null);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var window = new byte[64 * 1024];
            int read;

            while ((read = await stream.ReadAsync(window, cancellationToken)) > 0)
            {
                if (buffer.Length + read > MaximumBytes)
                {
                    return new ArtworkFetch(null, null);
                }

                buffer.Write(window, 0, read);
            }

            return buffer.Length == 0
                ? new ArtworkFetch(null, null)
                : new ArtworkFetch(buffer.ToArray(), type);
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
        {
            return new ArtworkFetch(null, null);
        }
    }
}

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
    IProposedWorkArtworkClient client,
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
