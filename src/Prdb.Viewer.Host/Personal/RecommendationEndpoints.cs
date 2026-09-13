using Microsoft.AspNetCore.Http.HttpResults;

using Prdb.Viewer.Host.Access;
using Prdb.Viewer.Host.Library;
using Prdb.Viewer.Infrastructure.Personal;

namespace Prdb.Viewer.Host.Personal;

/// <summary>
/// The Recommendations page's own API.
///
/// Every route takes the Account from the authenticated session, so there is no identifier a
/// caller could put in one to read somebody else's recommendations, their evidence, or what they
/// have put aside. An Administrator reaches these no more than anybody else does.
/// </summary>
public static class RecommendationEndpoints
{
    public static void MapRecommendations(this IEndpointRouteBuilder routes)
    {
        var recommendations = routes
            .MapGroup("/api/personal/recommendations")
            .WithTags("Personal State");

        // The seed is answered as well as taken. A screen renders and pages against the selection
        // it was given, and asks for a different one by sending a different seed — which is what
        // Other suggestions is.
        recommendations.MapGet("", async (
            RecommendationService service,
            HttpContext http,
            CancellationToken cancellationToken,
            int? seed = null,
            int take = RecommendationService.DefaultSectionSize) =>
            TypedResults.Ok(await service.GetAsync(
                http.User.AccountId()!.Value,
                http.ClientContextKey(),
                seed,
                take,
                cancellationToken)));

        recommendations.MapPost("/videos/{videoId:guid}/not-today", async Task<Results<Ok<DismissalResult>, NotFound>> (
            Guid videoId,
            RecommendationService service,
            HttpContext http,
            CancellationToken cancellationToken) =>
            await service.DismissAsync(http.User.AccountId()!.Value, videoId, cancellationToken)
                == DismissalVerdict.Updated
                ? TypedResults.Ok(new DismissalResult(true))
                : TypedResults.NotFound())
            .RequireCsrf();

        recommendations.MapDelete("/videos/{videoId:guid}/not-today", async (
            Guid videoId,
            RecommendationService service,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            await service.UndoDismissalAsync(
                http.User.AccountId()!.Value,
                videoId,
                cancellationToken);

            return TypedResults.Ok(new DismissalResult(false));
        })
            .RequireCsrf();
    }
}

/// <summary>Whether this Video is now put aside.</summary>
public sealed record DismissalResult(bool Dismissed);
