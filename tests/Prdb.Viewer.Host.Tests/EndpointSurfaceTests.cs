using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Viewer.Host.Access;

using Xunit;

namespace Prdb.Viewer.Host.Tests;

/// <summary>
/// What the whole route table must be true of, rather than what one route answers.
/// </summary>
/// <remarks>
/// Both protections here are opt-in per endpoint: authentication is the default and an endpoint
/// leaves it explicitly, and CSRF is added explicitly. That makes each of them a thing somebody has
/// to remember while adding an endpoint, and a forgotten one is silent — the route works, and only
/// its protection is missing. These assert over every endpoint the application maps, so forgetting
/// is a failed build rather than a discovery.
/// </remarks>
public sealed class EndpointSurfaceTests
{
    /// <summary>
    /// Every anonymous endpoint this product means to have, and why it is one.
    ///
    /// Adding to this list is a decision to widen the unauthenticated surface, which is a decision
    /// worth having to write down. Media delivery is anonymous because a browser's own
    /// <c>video</c> and <c>img</c> elements fetch it without the application's credentials; it is
    /// addressed by a random identifier that is neither a database key nor derived from a path.
    /// </summary>
    private static readonly HashSet<string> DeliberatelyAnonymous =
    [
        "GET /api/health",
        "GET /api/access/state",
        "POST /api/access/bootstrap",
        "POST /api/access/sign-in",
        "POST /api/access/registration-requests",
        "POST /api/access/recover",
        "GET /media/videos/{deliveryId:guid}",
        "GET /media/previews/{previewId:guid}",
        "GET /media/proposals/{artworkId:guid}",
        "GET /media/actors/{imageId:guid}",
        "GET /media/works/{imageId:guid}",
        // The browser application itself, which is a public page that then asks who is reading it.
        "GET {*path:nonfile}",
        "HEAD {*path:nonfile}",
    ];

    private static readonly string[] Changing = ["POST", "PUT", "PATCH", "DELETE"];

    [Fact]
    public void Every_state_changing_endpoint_is_protected_against_cross_site_requests()
    {
        var unprotected = Endpoints()
            .Where(endpoint => endpoint.Methods.Intersect(Changing).Any())
            .Where(endpoint => !endpoint.Anonymous)
            .Where(endpoint => !endpoint.CsrfProtected)
            .Select(endpoint => endpoint.Name)
            .ToArray();

        Assert.Empty(unprotected);
    }

    [Fact]
    public void Nothing_is_anonymous_that_was_not_meant_to_be()
    {
        var anonymous = Endpoints()
            .Where(endpoint => endpoint.Anonymous)
            .Select(endpoint => endpoint.Name)
            .ToHashSet();

        Assert.Empty(anonymous.Except(DeliberatelyAnonymous));
        // The list describes this application rather than outliving what it names, so an entry
        // whose endpoint is gone fails too.
        Assert.Empty(DeliberatelyAnonymous.Except(anonymous));
    }

    private sealed record Route(
        string Name,
        IReadOnlyList<string> Methods,
        bool Anonymous,
        bool CsrfProtected);

    private static IReadOnlyList<Route> Endpoints()
    {
        using var application = new ViewerApplication();
        _ = application.Server;

        return application.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint =>
            {
                var methods = endpoint.Metadata
                    .GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()
                    ?.HttpMethods ?? ["*"];

                return methods.Select(method => new Route(
                    $"{method} {endpoint.RoutePattern.RawText}",
                    methods,
                    endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null,
                    endpoint.Metadata.GetMetadata<CsrfProtectedMetadata>() is not null));
            })
            .ToArray();
    }
}
