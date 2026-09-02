using Microsoft.AspNetCore.Http.HttpResults;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Core.Personal;
using Prdb.Viewer.Host.Access;
using Prdb.Viewer.Infrastructure.Library;

namespace Prdb.Viewer.Host.Library;

public static class VideoEndpoints
{
    public static void MapVideos(this IEndpointRouteBuilder routes)
    {
        var library = routes.MapGroup("/api/library").WithTags("Library");

        library.MapGet("/videos", async (
            HttpContext http,
            LibraryDiscovery discovery,
            CancellationToken cancellationToken,
            string? query = null,
            LibrarySortOrder sort = LibrarySortOrder.Newest,
            string? sites = null,
            string? actors = null,
            bool unknownSite = false,
            string? work = null,
            string? review = null,
            string? playability = null,
            string? availability = null,
            string? quality = null,
            string? playState = null,
            string? shelf = null,
            int skip = 0,
            int take = LibraryPaging.DefaultPageSize) =>
            TypedResults.Ok(await discovery.GetAsync(
                http.User.AccountId()!.Value,
                http.ClientContextKey(),
                Request(
                    query,
                    sort,
                    sites,
                    actors,
                    unknownSite,
                    work,
                    review,
                    playability,
                    availability,
                    quality,
                    playState,
                    shelf) with { Skip = skip, Take = take },
                cancellationToken)));

        library.MapGet("/videos/{videoId:guid}", async Task<Results<Ok<VideoDetail>, NotFound>> (
            Guid videoId,
            HttpContext http,
            LibraryDiscovery discovery,
            CancellationToken cancellationToken) =>
        {
            var video = await discovery.GetVideoAsync(
                http.User.AccountId()!.Value,
                http.ClientContextKey(),
                videoId,
                cancellationToken);

            return video is null ? TypedResults.NotFound() : TypedResults.Ok(video);
        });

        // The facets take the same narrowing the Videos do, so a count says what choosing that
        // value would leave rather than what the whole library holds.
        library.MapGet("/facets", async (
            HttpContext http,
            LibraryDiscovery discovery,
            CancellationToken cancellationToken,
            string? query = null,
            string? sites = null,
            string? actors = null,
            bool unknownSite = false,
            string? work = null,
            string? review = null,
            string? playability = null,
            string? availability = null,
            string? quality = null,
            string? playState = null,
            string? shelf = null) =>
            TypedResults.Ok(await discovery.GetFacetsAsync(
                http.User.AccountId()!.Value,
                Request(
                    query,
                    LibrarySortOrder.Newest,
                    sites,
                    actors,
                    unknownSite,
                    work,
                    review,
                    playability,
                    availability,
                    quality,
                    playState,
                    shelf),
                cancellationToken)));

        library.MapPut("/preferences/include-not-ready", async (
            IncludeNotReadyRequest request,
            HttpContext http,
            LibraryPreferences preferences,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await preferences.SetIncludesNotReadyForDirectPlayAsync(
                http.User.AccountId()!.Value,
                request.Included,
                cancellationToken)))
            .RequireCsrf();

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

    private static LibraryDiscoveryRequest Request(
        string? query,
        LibrarySortOrder sort,
        string? sites,
        string? actors,
        bool unknownSite,
        string? work,
        string? review,
        string? playability,
        string? availability,
        string? quality,
        string? playState,
        string? shelf) =>
        new()
        {
            Query = query,
            Sort = sort,
            Sites = Values(sites),
            Actors = Values(actors),
            UnknownSite = unknownSite,
            WorkIdentification = Parsed<IdentificationResolution>(work),
            ReviewStatus = Parsed<IdentificationReviewStatus>(review),
            Playability = Parsed<ClientVideoPlayability>(playability),
            Availability = Parsed<VideoAvailability>(availability),
            Quality = Parsed<VideoQualityBand>(quality),
            PlayState = Parsed<PersonalPlayState>(playState),
            Shelf = Parsed<PersonalShelf>(shelf),
        };

    /// <summary>
    /// A facet arrives as one comma-separated parameter, because values inside a facet combine
    /// with OR and repeating the parameter would suggest they combine some other way.
    /// </summary>
    private static IReadOnlyList<string> Values(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IReadOnlyList<TValue> Parsed<TValue>(string? value)
        where TValue : struct, Enum =>
        Values(value)
            .Select(item => Enum.TryParse<TValue>(item, ignoreCase: true, out var parsed)
                ? parsed
                : (TValue?)null)
            .Where(parsed => parsed is not null)
            .Select(parsed => parsed!.Value)
            .Distinct()
            .ToArray();
}

public sealed record IncludeNotReadyRequest(bool Included);
