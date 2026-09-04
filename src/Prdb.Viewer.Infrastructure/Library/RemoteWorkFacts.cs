using Prdb.Sdk.Generated.Models;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// Reads what prdb says about one work into the records the rest of the application holds.
/// </summary>
/// <remarks>
/// Two requests answer with the same <c>VideoDetailDto</c> — the identification ladder and the
/// Enrichment lane's batch — so the reading of it lives in one place. What a work carries is a
/// question this application has already answered once by throwing most of it away; keeping the
/// answer here means it is extended once rather than twice.
/// </remarks>
public static class RemoteWorkFacts
{
    public static RemoteSite? Site(Guid? id, string? title, string? url) =>
        id is null || string.IsNullOrWhiteSpace(title)
            ? null
            : new RemoteSite(id.Value.ToString(), title, url);

    public static RemoteSite? Site(VideoDetailSiteDto? site) =>
        Site(site?.Id, site?.Title, site?.Url);

    public static RemoteWork? Of(VideoDetailDto? video)
    {
        if (video?.Id is null || string.IsNullOrWhiteSpace(video.Title))
        {
            return null;
        }

        var images = (video.Images ?? [])
            .Where(image => image.Id is not null && !string.IsNullOrWhiteSpace(image.Url))
            .Select(image => new RemoteWorkImage(image.Id!.Value.ToString(), image.Url!))
            .ToArray();

        return new RemoteWork(
            video.Id.Value.ToString(),
            video.Title,
            Site(video.Site),
            Actors(video.Actors),
            images.FirstOrDefault()?.Url,
            video.ReleaseDate?.DateTime,
            video.DurationMs)
        {
            Network = Network(video.Site?.Network),
            ReleaseNames = (video.PreNames ?? [])
                .Select(name => name.Title)
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .Select(title => title!)
                .ToArray(),
            Images = images,
            Duration = video.DurationSpreadMs is null && video.DurationFileCount is null
                ? null
                : new RemoteDurationConsensus(video.DurationSpreadMs, video.DurationFileCount),
            QualityOverview = Quality(video.QualityOverview),
        };
    }

    private static RemoteNetwork? Network(VideoDetailNetworkDto? network) =>
        network is null || string.IsNullOrWhiteSpace(network.Title)
            ? null
            : new RemoteNetwork(network.Title, network.Url);

    /// <summary>
    /// What prdb knows the work in, as words rather than as counts. The counts are prdb's view of
    /// its own catalogue; what a reader here wants is whether a better copy exists than the one
    /// this library holds.
    /// </summary>
    private static RemoteQualityOverview? Quality(VideoQualityOverviewDto? overview)
    {
        if (overview is null)
        {
            return null;
        }

        var resolutions = (overview.Resolutions ?? [])
            .Where(resolution => resolution.Width > 0 && resolution.Height > 0)
            .Select(resolution => $"{resolution.Width}×{resolution.Height}")
            .Distinct()
            .ToArray();
        var codecs = (overview.VideoCodecs ?? [])
            .Select(codec => codec.Codec)
            .Where(codec => !string.IsNullOrWhiteSpace(codec))
            .Select(codec => codec!)
            .Distinct()
            .ToArray();

        return resolutions.Length == 0 && codecs.Length == 0
            ? null
            : new RemoteQualityOverview(resolutions, codecs);
    }

    private static IReadOnlyList<RemoteActor> Actors(List<VideoDetailActorDto>? actors) =>
        (actors ?? [])
            .Where(actor => !string.IsNullOrWhiteSpace(actor.Name))
            .Select(actor => new RemoteActor(actor.Name!, actor.Id?.ToString()))
            .ToArray();
}
