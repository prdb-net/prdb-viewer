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

    public static RemoteWork? Of(VideoDetailDto? video) =>
        video?.Id is null || string.IsNullOrWhiteSpace(video.Title)
            ? null
            : new RemoteWork(
                video.Id.Value.ToString(),
                video.Title,
                Site(video.Site),
                Actors(video.Actors),
                (video.Images ?? []).Select(image => image.Url).FirstOrDefault(),
                video.ReleaseDate?.DateTime,
                video.DurationMs);

    private static IReadOnlyList<RemoteActor> Actors(List<VideoDetailActorDto>? actors) =>
        (actors ?? [])
            .Where(actor => !string.IsNullOrWhiteSpace(actor.Name))
            .Select(actor => new RemoteActor(actor.Name!, actor.Id?.ToString()))
            .ToArray();
}
