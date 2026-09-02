using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;
using Prdb.Viewer.Infrastructure.Personal;

namespace Prdb.Viewer.Infrastructure.Library;

public sealed record VideoSummary(
    Guid Id,
    string DisplayTitle,
    DateTimeOffset DiscoveryDate,
    VideoAvailability Availability,
    string? PreviewUrl,
    IdentificationSummary Identification,
    /// <summary>Whether this Account's current client can play the Video directly.</summary>
    ClientVideoPlayability Playability,
    /// <summary>Whether every Available occurrence is statically Unsupported, whatever the client.</summary>
    bool IsUnsupportedVideo,
    /// <summary>The Available occurrences, in the order one play action would try them.</summary>
    IReadOnlyList<PlaybackVariantView> VideoFiles,
    PersonalVideoStateSummary PersonalState);

public sealed class VideoCatalog(ViewerDbContext database, PlaybackPlanner planner)
{
    public async Task<IReadOnlyList<VideoSummary>> GetAsync(
        Guid accountId,
        string clientContextKey,
        CancellationToken cancellationToken = default)
    {
        var videos = await QueryForAccount(accountId)
            .Where(video => video.SurvivingVideoId == null &&
                            video.VideoFiles.Any(file =>
                                file.Availability == VideoFileAvailability.Available))
            .OrderByDescending(video => video.DiscoveryDate)
            .ToListAsync(cancellationToken);
        var plans = await planner.PlanAsync(accountId, clientContextKey, videos, cancellationToken);

        return videos.Select(video => Map(video, accountId, plans[video.Id])).ToArray();
    }

    private IQueryable<VideoRow> QueryForAccount(Guid accountId) =>
        database.Videos
            .AsNoTracking()
            .Include(video => video.Metadata)
            .Include(video => video.VideoFiles)
            .Include(video => video.IdentificationClaims)
            .Include(video => video.IdentificationCandidates)
            .Include(video => video.PersonalStates.Where(state => state.AccountId == accountId));

    internal static VideoSummary Map(VideoRow video, Guid accountId, VideoPlaybackPlan plan)
    {
        var trackedFiles = video.VideoFiles.OrderBy(file => file.RelativePath).ToArray();
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
            plan.Playability,
            plan.IsUnsupportedVideo,
            plan.Variants,
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
}
