using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;
using Prdb.Viewer.Infrastructure.Personal;

namespace Prdb.Viewer.Infrastructure.Library;

public sealed record VideoFileSummary(
    Guid Id,
    string RelativePath,
    long Size,
    long DurationMilliseconds,
    string ContainerFormat,
    string VideoCodec,
    string? AudioCodec,
    int? Width,
    int? Height,
    VideoFileAvailability Availability,
    DirectPlayClassification DirectPlayClassification,
    string DeliveryUrl);

public sealed record VideoSummary(
    Guid Id,
    string DisplayTitle,
    DateTimeOffset DiscoveryDate,
    VideoAvailability Availability,
    string? PreviewUrl,
    IdentificationSummary Identification,
    IReadOnlyList<VideoFileSummary> VideoFiles,
    PersonalVideoStateSummary PersonalState);

public sealed record PersonalLibrarySummary(
    IReadOnlyList<VideoSummary> ContinueWatching,
    IReadOnlyList<VideoSummary> Favourites,
    IReadOnlyList<VideoSummary> WatchLater);

public sealed class VideoCatalog(ViewerDbContext database)
{
    public async Task<IReadOnlyList<VideoSummary>> GetAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var videos = await QueryForAccount(accountId)
            .Where(video => video.SurvivingVideoId == null &&
                            video.VideoFiles.Any(file =>
                                file.Availability == VideoFileAvailability.Available))
            .OrderByDescending(video => video.DiscoveryDate)
            .ToListAsync(cancellationToken);

        return videos.Select(video => Map(video, accountId)).ToArray();
    }

    public async Task<PersonalLibrarySummary> GetPersonalLibraryAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var videos = await QueryForAccount(accountId)
            .Where(video =>
                video.SurvivingVideoId == null &&
                video.VideoFiles.Any(file => file.Availability != VideoFileAvailability.Removed) &&
                video.PersonalStates.Any(state =>
                    state.AccountId == accountId &&
                    (state.LastQualifiedActivityAt != null ||
                     state.FavouriteAddedAt != null ||
                     state.WatchLaterAddedAt != null)))
            .ToListAsync(cancellationToken);
        var entries = videos
            .Select(video => new PersonalEntry(
                Map(video, accountId),
                video.PersonalStates.Single(state => state.AccountId == accountId)))
            .ToArray();

        return new PersonalLibrarySummary(
            entries
                .Where(entry => entry.Video.PersonalState.ContinueWatching)
                .OrderByDescending(entry => entry.State.LastQualifiedActivityAt)
                .Select(entry => entry.Video)
                .ToArray(),
            entries
                .Where(entry => entry.Video.PersonalState.Favourite)
                .OrderByDescending(entry => entry.State.FavouriteAddedAt)
                .Select(entry => entry.Video)
                .ToArray(),
            entries
                .Where(entry => entry.Video.PersonalState.WatchLater)
                .OrderBy(entry => entry.State.WatchLaterAddedAt)
                .Select(entry => entry.Video)
                .ToArray());
    }

    private IQueryable<VideoRow> QueryForAccount(Guid accountId) =>
        database.Videos
            .AsNoTracking()
            .Include(video => video.Metadata)
            .Include(video => video.VideoFiles)
            .Include(video => video.IdentificationClaims)
            .Include(video => video.IdentificationCandidates)
            .Include(video => video.PersonalStates.Where(state => state.AccountId == accountId));

    internal static VideoSummary Map(VideoRow video, Guid accountId)
    {
        var trackedFiles = video.VideoFiles.OrderBy(file => file.RelativePath).ToArray();
        var availableFiles = trackedFiles
            .Where(file => file.Availability == VideoFileAvailability.Available)
            .Select(file => new VideoFileSummary(
                file.Id,
                file.RelativePath,
                file.Size,
                file.DurationMilliseconds,
                file.ContainerFormat,
                file.VideoCodec,
                file.AudioCodec,
                file.Width,
                file.Height,
                file.Availability,
                file.DirectPlayClassification,
                $"/media/videos/{file.PublicDeliveryId}"))
            .ToArray();
        var state = video.PersonalStates.SingleOrDefault(candidate => candidate.AccountId == accountId);
        var progressDuration = state?.ProgressVideoFileId is null
            ? 0
            : trackedFiles
                .SingleOrDefault(file => file.Id == state.ProgressVideoFileId)
                ?.DurationMilliseconds ?? 0;

        return new VideoSummary(
            video.Id,
            VideoPresentation.DisplayLabel(video),
            AsOffset(video.DiscoveryDate),
            AvailabilityOf(trackedFiles),
            VideoPresentation.PreviewUrl(video),
            VideoPresentation.Summarize(video),
            availableFiles,
            state is null
                ? PersonalStateService.EmptySummary()
                : PersonalStateService.ToSummary(state, progressDuration));
    }

    private static VideoAvailability AvailabilityOf(IReadOnlyCollection<VideoFileRow> files)
    {
        if (files.Any(file => file.Availability == VideoFileAvailability.Available))
        {
            return VideoAvailability.Available;
        }

        return files.All(file => file.Availability == VideoFileAvailability.Removed)
            ? VideoAvailability.Removed
            : VideoAvailability.Unavailable;
    }

    private static DateTimeOffset AsOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed record PersonalEntry(VideoSummary Video, PersonalVideoStateRow State);
}
