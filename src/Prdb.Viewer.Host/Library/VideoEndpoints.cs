using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Host.Access;

namespace Prdb.Viewer.Host.Library;

public static class VideoEndpoints
{
    public static void MapVideos(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/library/videos", async (
            VideoCatalog catalog,
            HttpContext http,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await catalog.GetAsync(
                http.User.AccountId()!.Value,
                cancellationToken)))
            .WithTags("Library");

        routes.MapGet("/media/videos/{deliveryId:guid}", async (
            Guid deliveryId,
            VideoDeliveryService delivery,
            PlaybackPressureMonitor playback,
            CancellationToken cancellationToken) =>
        {
            // Interactive playback takes priority over Background Work, so every delivered range
            // tells the lanes to reduce their pressure while a Video is being watched.
            playback.NoteDelivery();
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
