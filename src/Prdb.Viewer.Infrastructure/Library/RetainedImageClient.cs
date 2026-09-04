using Prdb.Viewer.Infrastructure.Configuration;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>What one attempt at a picture prdb offers produced.</summary>
public sealed record RetainedImage(byte[]? Content, string? ContentType);

public interface IRetainedImageClient
{
    Task<RetainedImage> FetchAsync(string url, CancellationToken cancellationToken = default);
}

/// <summary>
/// Fetches a picture prdb offers — a proposed work's, an Actor's — over the credential-free
/// artwork transport (ADR 0010): one transport attempt, no retry of its own, and no installation
/// credential on the wire, so a redirect may be followed because nothing is carried that could
/// leak.
/// </summary>
public sealed class RetainedImageClient(IHttpMessageHandlerFactory handlers)
    : IRetainedImageClient
{
    public const string TransportName = "prdb-artwork";

    /// <summary>
    /// What one picture is worth holding. A larger one is refused rather than truncated: a
    /// half-written frame is worse than the placeholder the screen already draws.
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

    public async Task<RetainedImage> FetchAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var address) ||
            (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
        {
            return new RetainedImage(null, null);
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
                return new RetainedImage(null, null);
            }

            var type = response.Content.Headers.ContentType?.MediaType;

            if (type is null || !Retainable.Contains(type))
            {
                return new RetainedImage(null, null);
            }

            if (response.Content.Headers.ContentLength > MaximumBytes)
            {
                return new RetainedImage(null, null);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var window = new byte[64 * 1024];
            int read;

            while ((read = await stream.ReadAsync(window, cancellationToken)) > 0)
            {
                if (buffer.Length + read > MaximumBytes)
                {
                    return new RetainedImage(null, null);
                }

                buffer.Write(window, 0, read);
            }

            return buffer.Length == 0
                ? new RetainedImage(null, null)
                : new RetainedImage(buffer.ToArray(), type);
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
        {
            return new RetainedImage(null, null);
        }
    }
}
