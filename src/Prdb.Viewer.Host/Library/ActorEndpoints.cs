namespace Prdb.Viewer.Host.Library;

using Microsoft.AspNetCore.Http.HttpResults;

using Prdb.Viewer.Host.Access;
using Prdb.Viewer.Infrastructure.Library;

public static class ActorEndpoints
{
    public static void MapActors(this IEndpointRouteBuilder routes)
    {
        var library = routes.MapGroup("/api/library").WithTags("Library");

        library.MapGet("/actors", async (
            ActorDiscovery actors,
            CancellationToken cancellationToken,
            string? query = null,
            ActorSortOrder sort = ActorSortOrder.Name,
            int skip = 0,
            int take = LibraryPaging.DefaultPageSize) =>
            TypedResults.Ok(await actors.IndexAsync(
                new ActorIndexRequest
                {
                    Query = query,
                    Sort = sort,
                    Skip = skip,
                    Take = take,
                },
                cancellationToken)));

        // An Actor is addressed by the identity prdb holds for them, which is the only identity
        // there is (ADR 0020). A credit that resolves to nobody has no address, and the Library's
        // Actor facet is where that name is still reached.
        library.MapGet("/actors/{actorId:guid}", async Task<Results<Ok<ActorDetail>, NotFound>> (
            Guid actorId,
            HttpContext http,
            ActorDiscovery actors,
            CancellationToken cancellationToken) =>
        {
            var actor = await actors.GetAsync(
                actorId.ToString(),
                http.User.AccountId()!.Value,
                http.ClientContextKey(),
                cancellationToken);

            return actor is null ? TypedResults.NotFound() : TypedResults.Ok(actor);
        });

        // An Actor's picture is served from application storage under a random, non-enumerable
        // identifier, the way a preview is, so an Actor's page is one origin rather than two and
        // prdb never sees which installation looked at whom (ADR 0020).
        routes.MapGet("/media/actors/{imageId:guid}", async (
            Guid imageId,
            PreviewDeliveryService previews,
            CancellationToken cancellationToken) =>
        {
            var image = await previews.OpenActorImageAsync(imageId, cancellationToken);

            return image is null
                ? Results.NotFound()
                : Results.Stream(
                    image.Content,
                    image.ContentType,
                    fileDownloadName: null,
                    lastModified: image.LastModified,
                    enableRangeProcessing: false);
        })
        .WithTags("Preview Delivery")
        .AllowAnonymous();
    }
}
