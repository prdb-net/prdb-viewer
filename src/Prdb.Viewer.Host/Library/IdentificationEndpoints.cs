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
    }
}
