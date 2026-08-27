using Prdb.Viewer.Infrastructure.Library;

namespace Prdb.Viewer.Host.Library;

public static class VideoEndpoints
{
    public static void MapVideos(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/library/videos", async (
            VideoCatalog catalog,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await catalog.GetAsync(cancellationToken)))
            .WithTags("Library");

        routes.MapGet("/media/videos/{deliveryId:guid}", async (
            Guid deliveryId,
            VideoDeliveryService delivery,
            CancellationToken cancellationToken) =>
        {
            var opened = await delivery.OpenAsync(deliveryId, cancellationToken);

            return opened is null
                ? Results.NotFound()
                : Results.Stream(
                    opened.Content,
                    opened.ContentType,
                    fileDownloadName: null,
                    lastModified: opened.LastModified,
                    enableRangeProcessing: true);
        })
        .WithTags("Video Delivery")
        .AllowAnonymous();
    }
}
