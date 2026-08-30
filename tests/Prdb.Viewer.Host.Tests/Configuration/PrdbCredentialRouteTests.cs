using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Prdb.FakeCatalogue;

using Prdb.Viewer.Host.Access;

using Xunit;

namespace Prdb.Viewer.Host.Tests.Configuration;

/// <summary>
/// What the Installation screen is told about a prdb credential, decided by what prdb actually
/// answered.
///
/// Elsewhere these routes are given a stand-in verifier that was told what to conclude, so the
/// whole question — which reply means the key, which means the service, and which means neither —
/// was settled by the test rather than by the code. 0.6.0 fixed a defect squarely in that gap: a
/// 200 carrying JSON with none of the documented fields counted as proof, and the screen showed a
/// verified connection that had been checked against nothing. The fix was covered at the verifier;
/// what the Administrator ends up reading was not.
///
/// So these keep the verifier that ships and replace only the socket beneath it.
/// </summary>
public sealed class PrdbCredentialRouteTests
{
    [Fact]
    public async Task An_answered_rate_limit_is_what_the_screen_calls_verified()
    {
        await using var installation = await ClaimedAsync(new FakePrdb());

        var verdict = await installation.VerifyAsync("test-api-key");

        Assert.Equal("Verified", verdict.GetProperty("verdict").GetString());
        var screen = await installation.ScreenAsync();
        Assert.Equal("Verified", screen.GetProperty("prdbConnectionStatus").GetString());
        Assert.True(screen.GetProperty("hasPrdbCredential").GetBoolean());
        Assert.Null(screen.GetProperty("lastConnectionIssue").GetString());

        // The stored key is never shown again, whatever else the screen says.
        Assert.DoesNotContain("test-api-key", screen.ToString());
    }

    /// <summary>
    /// The distinction the whole screen turns on: a refused key is the Administrator's to replace,
    /// an unreachable service is nobody's and resolves itself. Both arrive as a failed request.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_refused_key_is_named_as_the_key(HttpStatusCode status)
    {
        var prdb = new FakePrdb { Failure = status };
        await using var installation = await ClaimedAsync(prdb);

        var verdict = await installation.VerifyAsync("test-api-key");

        Assert.Equal("Rejected", verdict.GetProperty("verdict").GetString());
        var screen = await installation.ScreenAsync();
        Assert.Equal("Rejected", screen.GetProperty("prdbConnectionStatus").GetString());
        Assert.Equal("ExternalAuthority", screen.GetProperty("lastConnectionIssue").GetString());
        Assert.False(screen.GetProperty("hasPrdbCredential").GetBoolean());
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task An_unreachable_service_leaves_the_key_pending_rather_than_refused(
        HttpStatusCode status)
    {
        var prdb = new FakePrdb { Failure = status };
        await using var installation = await ClaimedAsync(prdb);

        var verdict = await installation.VerifyAsync("test-api-key");

        Assert.Equal("VerificationPending", verdict.GetProperty("verdict").GetString());
        var screen = await installation.ScreenAsync();
        Assert.Equal("VerificationPending", screen.GetProperty("prdbConnectionStatus").GetString());
        Assert.Equal("ExternalAvailability", screen.GetProperty("lastConnectionIssue").GetString());
    }

    /// <summary>
    /// The 0.6.0 defect, at the surface that showed it. A proxy, a gateway, or an endpoint that
    /// has moved can answer 200 with JSON that deserialises into an object holding nothing. The
    /// screen must not call that a verified connection.
    ///
    /// Such a reply leaves the credential unjudged rather than refused, so the key stays held for
    /// a retry and the screen goes on reporting that one is stored. What must not happen is that
    /// it is promoted to the active credential — which is what a status short of Verified, and a
    /// replacement still pending, together say.
    /// </summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("""{"isEnforced":true}""")]
    [InlineData("""{"hourly":null,"monthly":null}""")]
    [InlineData("<html>Sign in to continue</html>")]
    public async Task A_reply_carrying_no_rate_limit_verifies_nothing(string body)
    {
        var prdb = new FakePrdb { RawBody = body };
        await using var installation = await ClaimedAsync(prdb);

        var verdict = await installation.VerifyAsync("test-api-key");

        Assert.NotEqual("Verified", verdict.GetProperty("verdict").GetString());
        var screen = await installation.ScreenAsync();
        Assert.NotEqual("Verified", screen.GetProperty("prdbConnectionStatus").GetString());
        Assert.True(screen.GetProperty("credentialReplacementPending").GetBoolean());
        Assert.Equal("ConfigurationRequired", screen.GetProperty("status").GetString());
    }

    /// <summary>
    /// A replacement that prdb refuses says nothing about the key that is already working, so the
    /// installation keeps identifying while the Administrator tries again. The screen has to say
    /// which of the two was refused, or it reads as an outage the Administrator caused.
    /// </summary>
    [Fact]
    public async Task A_refused_replacement_leaves_the_working_key_in_place()
    {
        var prdb = new FakePrdb();
        await using var installation = await ClaimedAsync(prdb);
        await installation.VerifyAsync("first-api-key");

        prdb.Failure = HttpStatusCode.Unauthorized;
        var verdict = await installation.VerifyAsync("second-api-key");

        Assert.Equal("Rejected", verdict.GetProperty("verdict").GetString());
        var screen = await installation.ScreenAsync();
        Assert.Equal("Verified", screen.GetProperty("prdbConnectionStatus").GetString());
        Assert.True(screen.GetProperty("hasPrdbCredential").GetBoolean());
        Assert.Equal("ReplacementRejected", screen.GetProperty("lastConnectionIssue").GetString());
    }

    /// <summary>
    /// A retry re-offers the key the installation is holding, so an Administrator who was told the
    /// service was unreachable can ask again without typing it out a second time.
    /// </summary>
    [Fact]
    public async Task A_retry_re_offers_the_pending_key_once_the_service_answers()
    {
        var prdb = new FakePrdb { Failure = HttpStatusCode.ServiceUnavailable };
        await using var installation = await ClaimedAsync(prdb);
        await installation.VerifyAsync("test-api-key");

        prdb.Failure = null;
        var verdict = await installation.PostAsync("/api/admin/configuration/prdb-connection/retry");

        Assert.Equal("Verified", verdict.GetProperty("verdict").GetString());
        var screen = await installation.ScreenAsync();
        Assert.Equal("Verified", screen.GetProperty("prdbConnectionStatus").GetString());
        Assert.True(screen.GetProperty("hasPrdbCredential").GetBoolean());
    }

    /// <summary>
    /// Where the credential is sent, and how. A key on the query string would reach logs and
    /// proxies that a header does not.
    /// </summary>
    [Fact]
    public async Task The_key_travels_in_a_header_to_the_rate_limit_endpoint()
    {
        var prdb = new FakePrdb();
        await using var installation = await ClaimedAsync(prdb);

        await installation.VerifyAsync("test-api-key");

        var asked = Assert.Single(prdb.Requested);
        Assert.Equal("/rate-limit", asked.AbsolutePath);
        Assert.DoesNotContain("test-api-key", asked.ToString());
    }

    private static async Task<Installation> ClaimedAsync(FakePrdb prdb)
    {
        var application = new ViewerApplication(prdb: prdb);
        var client = application.CreateClient();
        var authorization = await application.CreateBootstrapAuthorizationAsync();

        using var response = await client.PostAsJsonAsync(
            "/api/access/bootstrap",
            new
            {
                authorization,
                username = "administrator",
                password = "administrator password",
                email = (string?)null,
            },
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);

        return new Installation(
            application,
            client,
            body.GetProperty("account").GetProperty("csrfToken").GetString()!);
    }

    /// <summary>A claimed installation with an Administrator signed in, and nothing configured.</summary>
    private sealed class Installation(
        ViewerApplication application,
        HttpClient client,
        string csrf) : IAsyncDisposable
    {
        public Task<JsonElement> VerifyAsync(string credential) =>
            PostAsync("/api/admin/configuration/prdb-connection", new { credential });

        public async Task<JsonElement> PostAsync(string path, object? body = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = body is null ? null : JsonContent.Create(body),
            };
            request.Headers.Add(CsrfEndpointFilter.HeaderName, csrf);
            using var response = await client.SendAsync(
                request,
                TestContext.Current.CancellationToken);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<JsonElement>(
                TestContext.Current.CancellationToken);
        }

        /// <summary>What the Installation screen reads, which is the point of all of this.</summary>
        public async Task<JsonElement> ScreenAsync() =>
            await client.GetFromJsonAsync<JsonElement>(
                "/api/admin/configuration/",
                TestContext.Current.CancellationToken);

        public ValueTask DisposeAsync()
        {
            client.Dispose();
            application.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
