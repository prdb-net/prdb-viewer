using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Xunit;

namespace Prdb.Viewer.Host.Tests;

public sealed class HealthRouteTests
{
    [Fact]
    public async Task Health_is_a_public_liveness_answer()
    {
        using var application = new ViewerApplication();
        using var client = application.CreateClient();

        using var response = await client.GetAsync(
            "/api/health",
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Unknown_api_routes_are_not_an_anonymous_surface()
    {
        using var application = new ViewerApplication();
        using var client = application.CreateClient();

        using var response = await client.GetAsync(
            "/api/not-a-route",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
