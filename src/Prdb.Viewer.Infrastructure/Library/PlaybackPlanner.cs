using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// One Available Video File as one client sees it: the facts it can be measured by, the evidence
/// this Account's client has produced about it, and where it stands in the order a play action
/// follows.
/// </summary>
public sealed record PlaybackVariantView(
    Guid VideoFileId,
    string DeliveryUrl,
    string ContainerFormat,
    string VideoCodec,
    string? AudioCodec,
    int? Width,
    int? Height,
    double? FrameRate,
    long? Bitrate,
    int? AudioChannels,
    int? AudioSampleRate,
    long? AudioBitrate,
    long Size,
    long DurationMilliseconds,
    DirectPlayClassification DirectPlayClassification,
    string ProfileKey,
    /// <summary>The full RFC 6381 type a client measures with Media Capabilities, when the
    /// inspected facts determine every part of it.</summary>
    string? PreciseVideoContentType,
    string? PreciseAudioContentType,
    /// <summary>The coarser type a client can still test, when the precise one is unavailable.</summary>
    string? BasicContentType,
    ClientPlaybackAssessmentVerdict? Assessment,
    bool? Smooth,
    bool? PowerEfficient,
    ObservedPlaybackOutcome? Outcome,
    bool ReadyForDirectPlay,
    VariantSelectionReason SelectionReason);

/// <summary>
/// What one Account's client may do with one Video: whether it can be played directly here, and in
/// which order its occurrences should be tried.
/// </summary>
public sealed record VideoPlaybackPlan(
    ClientVideoPlayability Playability,
    bool IsUnsupportedVideo,
    IReadOnlyList<PlaybackVariantView> Variants);

/// <summary>
/// Derives Client Video Playability and the variant order for one Account and client context.
///
/// The rules themselves live in <see cref="ClientVideoPlayabilityRule"/> and
/// <see cref="VariantSelectionRule"/>; this loads the evidence they need for a page of Videos in
/// two queries rather than one per Video.
/// </summary>
public sealed class PlaybackPlanner(ViewerDbContext database)
{
    public async Task<IReadOnlyDictionary<Guid, VideoPlaybackPlan>> PlanAsync(
        Guid accountId,
        string clientContextKey,
        IReadOnlyCollection<VideoRow> videos,
        CancellationToken cancellationToken = default)
    {
        if (videos.Count == 0)
        {
            return new Dictionary<Guid, VideoPlaybackPlan>();
        }

        var files = videos
            .SelectMany(video => video.VideoFiles)
            .Where(file => file.Availability == VideoFileAvailability.Available)
            .ToArray();
        var profileKeys = files.Select(file => file.ProfileKey).Distinct().ToArray();
        var fileIds = files.Select(file => file.Id).ToArray();
        var assessments = await database.ClientPlaybackAssessments
            .AsNoTracking()
            .Where(row => row.AccountId == accountId &&
                          row.ClientContextKey == clientContextKey &&
                          profileKeys.Contains(row.ProfileKey))
            .ToDictionaryAsync(row => row.ProfileKey, cancellationToken);
        var outcomes = await database.ObservedPlaybackOutcomes
            .AsNoTracking()
            .Where(row => row.AccountId == accountId &&
                          row.ClientContextKey == clientContextKey &&
                          fileIds.Contains(row.VideoFileId))
            .ToDictionaryAsync(row => row.VideoFileId, cancellationToken);

        return videos.ToDictionary(
            video => video.Id,
            video => Plan(video, assessments, outcomes));
    }

    public async Task<VideoPlaybackPlan> PlanAsync(
        Guid accountId,
        string clientContextKey,
        VideoRow video,
        CancellationToken cancellationToken = default) =>
        (await PlanAsync(accountId, clientContextKey, [video], cancellationToken))[video.Id];

    private static VideoPlaybackPlan Plan(
        VideoRow video,
        IReadOnlyDictionary<string, ClientPlaybackAssessmentRow> assessments,
        IReadOnlyDictionary<Guid, ObservedPlaybackOutcomeRow> outcomes)
    {
        var available = video.VideoFiles
            .Where(file => file.Availability == VideoFileAvailability.Available)
            .ToArray();
        var evidence = available.ToDictionary(
            file => file.Id,
            file => EvidenceOf(file, assessments, outcomes));
        var ordered = VariantSelectionRule
            .Order(available, file => evidence[file.Id])
            .Select(file => View(file, evidence[file.Id], assessments))
            .ToArray();

        return new VideoPlaybackPlan(
            ClientVideoPlayabilityRule.For(evidence.Values),
            ClientVideoPlayabilityRule.IsUnsupportedVideo(
                available.Select(file => file.DirectPlayClassification)),
            ordered);
    }

    /// <summary>
    /// Reads one file's evidence. An outcome observed about different content is not evidence
    /// about this content and is ignored rather than deleted, and only a Media failure is a
    /// judgement about the file at all.
    /// </summary>
    private static VariantEvidence EvidenceOf(
        VideoFileRow file,
        IReadOnlyDictionary<string, ClientPlaybackAssessmentRow> assessments,
        IReadOnlyDictionary<Guid, ObservedPlaybackOutcomeRow> outcomes)
    {
        assessments.TryGetValue(file.ProfileKey, out var assessment);
        outcomes.TryGetValue(file.Id, out var outcome);

        var applicable = outcome is not null &&
                         string.Equals(outcome.ContentSha256, file.Sha256, StringComparison.OrdinalIgnoreCase)
            ? outcome
            : null;

        return new VariantEvidence(
            file.DirectPlayClassification,
            assessment?.Verdict,
            assessment?.Smooth,
            assessment?.PowerEfficient,
            applicable?.Outcome,
            (long)(file.Width ?? 0) * (file.Height ?? 0),
            file.VideoBitrate ?? 0);
    }

    private static PlaybackVariantView View(
        VideoFileRow file,
        VariantEvidence evidence,
        IReadOnlyDictionary<string, ClientPlaybackAssessmentRow> assessments)
    {
        assessments.TryGetValue(file.ProfileKey, out var assessment);
        var media = file.Media;

        return new PlaybackVariantView(
            file.Id,
            $"/media/videos/{file.PublicDeliveryId}",
            file.ContainerFormat,
            file.VideoCodec,
            file.AudioCodec,
            file.Width,
            file.Height,
            file.FrameRate,
            file.VideoBitrate,
            file.AudioChannels,
            file.AudioSampleRate,
            file.AudioBitrate,
            file.Size,
            file.DurationMilliseconds,
            file.DirectPlayClassification,
            file.ProfileKey,
            PlaybackProfileRule.PreciseVideoContentType(media),
            PlaybackProfileRule.PreciseAudioContentType(media),
            PlaybackProfileRule.BasicContentType(media),
            assessment?.Verdict,
            assessment?.Smooth,
            assessment?.PowerEfficient,
            evidence.Outcome,
            evidence.ReadyForDirectPlay,
            VariantSelectionRule.ReasonFor(evidence));
    }
}
