using System.Net.Http.Headers;

namespace Prdb.Viewer.Infrastructure.Configuration;

public sealed class ProductUserAgentHandler : DelegatingHandler
{
    private static readonly ProductInfoHeaderValue Product = new(
        "prdb-viewer",
        typeof(ProductUserAgentHandler).Assembly.GetName().Version?.ToString(3) ?? "0.1.0");

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.Headers.UserAgent.Add(Product);
        return base.SendAsync(request, cancellationToken);
    }
}
