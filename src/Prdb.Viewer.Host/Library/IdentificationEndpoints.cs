using Microsoft.AspNetCore.Http.HttpResults;

using Prdb.Viewer.Core.Access;
using Prdb.Viewer.Core.Library;
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

        // ADR 0004: what an Administrator is looking at belongs in the address, so a filtered page
        // of the backlog is a link a colleague can be sent rather than a state of one browser.
        review.MapGet("/queue", async (
            IdentificationReviewService identification,
            int? skip,
            int? take,
            IdentificationDimension? dimension,
            IdentificationReviewReason? reason,
            IdentificationEvidenceClass? evidenceClass,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await identification.GetQueueAsync(
                new IdentificationQueueRequest
                {
                    Skip = skip ?? 0,
                    Take = take ?? 10,
                    Dimension = dimension,
                    Reason = reason,
                    EvidenceClass = evidenceClass,
                },
                cancellationToken)));

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

        // The group about to be decided: what each decision would do to the whole of it, and the
        // cases it would settle with the versions they are being read at. The manifest is fetched
        // only when somebody is about to decide rather than carried by every page of the queue.
        review.MapGet("/groups", async Task<Results<Ok<IdentificationGroupPlan>, NotFound>> (
            string key,
            IdentificationReviewService identification,
            CancellationToken cancellationToken) =>
        {
            var plan = await identification.GetGroupPlanAsync(key, cancellationToken);

            return plan is null ? TypedResults.NotFound() : TypedResults.Ok(plan);
        });

        review.MapPost("/groups/decisions", async (
            IdentificationGroupDecisionRequest request,
            IdentificationReviewService identification,
            HttpContext http,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await identification.DecideGroupAsync(
                http.User.AccountId()!.Value,
                request,
                cancellationToken)))
            .RequireCsrf();

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
