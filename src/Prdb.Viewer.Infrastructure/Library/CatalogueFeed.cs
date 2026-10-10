using System.Globalization;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>The exact seek position for one current-state feed, independent of the local clock.</summary>
public sealed record CatalogueCursor(string Since, Guid? SinceId)
{
    public static readonly CatalogueCursor Baseline = new("0001-01-01T00:00:00Z", null);

    public static CatalogueCursor At(DateTimeOffset timestamp, Guid? id) =>
        new(timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), id);

    public static string Authority(string origin, string credential) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(origin + "\n" + credential)));

    public string Url(string origin, string feed) =>
        $"{origin.TrimEnd('/')}/{feed}/changes?Since={Uri.EscapeDataString(Since)}" +
        (SinceId is { } id ? $"&SinceId={id:D}" : "") + "&PageSize=1000";
}

/// <summary>Observes retry guidance without owning or retrying the shared credentialed handler.</summary>
internal sealed class CatalogueResponseGuidance(HttpMessageHandler handler, TimeProvider timeProvider) : HttpMessageHandler
{
    private readonly HttpMessageInvoker transport = new(handler, disposeHandler: false);
    public TimeSpan? RetryAfter { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await transport.SendAsync(request, cancellationToken);
        RetryAfter = response.Headers.RetryAfter?.Delta ??
            (response.Headers.RetryAfter?.Date is { } at ? at - timeProvider.GetUtcNow() : null);
        return response;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) transport.Dispose();
        base.Dispose(disposing);
    }
}
