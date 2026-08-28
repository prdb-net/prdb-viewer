using System.Net.Http.Json;

using Xunit;

namespace Prdb.Viewer.Host.Tests.Access;

public sealed class ReverseProxyTests
{
    [Fact]
    public async Task Forwarded_headers_are_ignored_until_an_operator_trusts_the_proxy()
    {
        Assert.DoesNotContain(
            "secure",
            await SetCookieAsync(behindReverseProxy: false),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_trusted_proxy_keeps_the_session_cookie_secure_behind_tls_termination()
    {
        Assert.Contains(
            "secure",
            await SetCookieAsync(behindReverseProxy: true),
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> SetCookieAsync(bool behindReverseProxy)
    {
        using var application = new ViewerApplication(behindReverseProxy: behindReverseProxy);
        using var client = application.CreateClient();
        var authorization = await application.CreateBootstrapAuthorizationAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/access/bootstrap")
        {
            Content = JsonContent.Create(new
            {
                authorization,
                username = "administrator",
                password = "administrator password",
                email = (string?)null,
            }),
        };

        // The proxy states that the client spoke TLS even though this hop did not.
        request.Headers.Add("X-Forwarded-Proto", "https");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return string.Join(' ', response.Headers.GetValues("Set-Cookie"));
    }
}
