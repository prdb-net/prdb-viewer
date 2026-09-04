using Prdb.Viewer.Core.Access;
using Prdb.Viewer.Host.Access;
using Prdb.Viewer.Infrastructure.Library;

namespace Prdb.Viewer.Host.Library;

public static class IdentificationEndpoints
{
    public static void MapIdentification(this IEndpointRouteBuilder routes)
    {
        var review = routes.MapGroup("/api/admin/identification")
            .WithTags("Identification Review")
            .RequireAuthorization(policy =>
                policy.RequireRole(AccountAuthority.Administrator.ToString()));

        review.MapGet("/queue", async (
            IdentificationReviewService identification,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await identification.GetQueueAsync(cancellationToken)));

        review.MapGet("/videos/{videoId:guid}", async (
            Guid videoId,
            IdentificationReviewService identification,
            CancellationToken cancellationToken) =>
        {
            var identificationCase = await identification.GetCaseAsync(videoId, cancellationToken);

            return identificationCase is null
                ? Results.NotFound()
                : Results.Ok(identificationCase);
        });

        review.MapPost("/videos/{videoId:guid}/decisions", async (
            Guid videoId,
            IdentificationDecisionRequest request,
            IdentificationReviewService identification,
            HttpContext http,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await identification.DecideAsync(
                http.User.AccountId()!.Value,
                videoId,
                request,
                cancellationToken)))
            .RequireCsrf();

        routes.MapGet("/media/previews/{previewId:guid}", async (
            Guid previewId,
            PreviewDeliveryService previews,
            CancellationToken cancellationToken) =>
        {
            var preview = await previews.OpenAsync(previewId, cancellationToken);

            return preview is null
                ? Results.NotFound()
                : Results.Stream(
                    preview.Content,
                    preview.ContentType,
                    fileDownloadName: null,
                    lastModified: preview.LastModified,
                    enableRangeProcessing: false);
        })
        .WithTags("Preview Delivery")
        .AllowAnonymous();

        // A proposal's picture is served from application storage under a random identifier, the
        // way a preview is, so that the review case is one origin rather than two and prdb never
        // sees which installation opened which case.
        routes.MapGet("/media/proposals/{artworkId:guid}", async (
            Guid artworkId,
            PreviewDeliveryService previews,
            CancellationToken cancellationToken) =>
        {
            var artwork = await previews.OpenProposedWorkArtworkAsync(artworkId, cancellationToken);

            return artwork is null
                ? Results.NotFound()
                : Results.Stream(
                    artwork.Content,
                    artwork.ContentType,
                    fileDownloadName: null,
                    lastModified: artwork.LastModified,
                    enableRangeProcessing: false);
        })
        .WithTags("Preview Delivery")
        .AllowAnonymous();
    }
}
