using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Prdb.Viewer.Host.Access;
using Prdb.Viewer.Infrastructure.Configuration;

using Xunit;

namespace Prdb.Viewer.Host.Tests.Configuration;

public sealed class ConfigurationRouteTests
{
    [Fact]
    public async Task Administrator_verifies_prdb_and_explicitly_activates_a_mounted_directory()
    {
        var verifier = new StubPrdbConnectionVerifier();
        using var application = new ViewerApplication(verifier);
        using var administrator = application.CreateClient();
        var authorization = await application.CreateBootstrapAuthorizationAsync();
        var account = await ClaimAsync(administrator, authorization);
        var csrf = account.GetProperty("csrfToken").GetString()!;

        var initial = await administrator.GetFromJsonAsync<JsonElement>(
            "/api/admin/configuration/",
            TestContext.Current.CancellationToken);
        Assert.Equal("ConfigurationRequired", initial.GetProperty("status").GetString());
        Assert.False(initial.GetProperty("hasPrdbCredential").GetBoolean());

        using var unverified = await administrator.PostAsJsonAsync(
            "/api/admin/configuration/prdb-connection",
            new { credential = "test-api-key" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, unverified.StatusCode);
        Assert.Empty(verifier.Credentials);

        using var verify = Post(
            "/api/admin/configuration/prdb-connection",
            new { credential = "test-api-key" },
            csrf);
        using var verified = await administrator.SendAsync(
            verify,
            TestContext.Current.CancellationToken);
        var verifiedBody = await verified.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        Assert.Contains("Verified", verifiedBody);
        Assert.DoesNotContain("test-api-key", verifiedBody);

        var library = Path.Combine(application.LibraryMountRoot, "main");
        Directory.CreateDirectory(library);
        await File.WriteAllTextAsync(
            Path.Combine(library, "source-marker.txt"),
            "source media",
            TestContext.Current.CancellationToken);
        var candidates = await administrator.GetFromJsonAsync<JsonElement>(
            "/api/admin/configuration/library-directory-candidates",
            TestContext.Current.CancellationToken);
        Assert.Contains(library, candidates.GetProperty("containerPaths").EnumerateArray()
            .Select(item => item.GetString()));

        using var stage = Post(
            "/api/admin/configuration/library-directories/stages",
            new { name = "Main Library", containerPath = library },
            csrf);
        using var staged = await administrator.SendAsync(stage, TestContext.Current.CancellationToken);
        var stageBody = await staged.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        Assert.Equal("Staged", stageBody.GetProperty("verdict").GetString());

        using var activate = Post(
            $"/api/admin/configuration/library-directories/stages/{stageBody.GetProperty("stageId").GetGuid()}/activate",
            body: null,
            csrf);
        using var activated = await administrator.SendAsync(
            activate,
            TestContext.Current.CancellationToken);
        var activation = await activated.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        Assert.Equal("Activated", activation.GetProperty("verdict").GetString());

        var current = await administrator.GetFromJsonAsync<JsonElement>(
            "/api/admin/configuration/",
            TestContext.Current.CancellationToken);
        Assert.Equal("Verified", current.GetProperty("prdbConnectionStatus").GetString());
        Assert.Equal("ConfigurationPending", current.GetProperty("status").GetString());
        Assert.Single(current.GetProperty("libraryDirectories").EnumerateArray());
        Assert.DoesNotContain("test-api-key", current.ToString());
    }

    private static HttpRequestMessage Post(string path, object? body, string csrf)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        request.Headers.Add(CsrfEndpointFilter.HeaderName, csrf);
        return request;
    }

    private static async Task<JsonElement> ClaimAsync(HttpClient client, string authorization)
    {
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
        return body.GetProperty("account").Clone();
    }

    private sealed class StubPrdbConnectionVerifier : IPrdbConnectionVerifier
    {
        public List<string> Credentials { get; } = [];

        public Task<PrdbVerificationOutcome> VerifyAsync(
            string credential,
            CancellationToken cancellationToken = default)
        {
            Credentials.Add(credential);
            return Task.FromResult(PrdbVerificationOutcome.Verified);
        }
    }
}
