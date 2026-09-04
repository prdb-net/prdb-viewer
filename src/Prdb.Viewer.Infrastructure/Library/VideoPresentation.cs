using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// How a Video presents itself once its claims are taken into account. Established knowledge
/// supplies the label; an Unknown Video keeps its local facts. Candidate contents never appear
/// here, so nothing under review can leak into ordinary browsing.
/// </summary>
internal static class VideoPresentation
{
    public static string DisplayLabel(VideoRow video)
    {
        var work = IdentificationService.Current(video, IdentificationDimension.WorkIdentification);

        if (work is not null)
        {
            return RetainedTitle(video, work) ?? work.TargetTitle;
        }

        var file = video.VideoFiles
            .Where(candidate => candidate.Availability == VideoFileAvailability.Available)
            .OrderBy(candidate => candidate.InspectedAt)
            .ThenBy(candidate => candidate.RelativePath)
            .FirstOrDefault() ??
            video.VideoFiles
                .OrderBy(candidate => candidate.InspectedAt)
                .ThenBy(candidate => candidate.RelativePath)
                .FirstOrDefault();
        var label = file is null ? null : Path.GetFileNameWithoutExtension(file.RelativePath);

        return string.IsNullOrWhiteSpace(label) ? "Unknown Video" : label;
    }

    public static string? PreviewUrl(VideoRow video)
    {
        var file = video.VideoFiles
            .Where(candidate => candidate.PublicPreviewId is not null &&
                                candidate.PreviewState == VideoFilePreviewState.Generated)
            .OrderBy(candidate => candidate.Availability == VideoFileAvailability.Available ? 0 : 1)
            .ThenBy(candidate => candidate.RelativePath)
            .FirstOrDefault();

        return file is null ? null : $"/media/previews/{file.PublicPreviewId}";
    }

    public static IdentificationSummary Summarize(VideoRow video) =>
        new(
            ClaimView(video, IdentificationDimension.WorkIdentification),
            ClaimView(video, IdentificationDimension.SiteRecognition),
            ActorCredits(video)
                .Select(actor => new ActorCreditView(actor.Name, actor.ActorId))
                .ToArray(),
            AsOffset(video.Metadata?.FetchedAt));

    public static IdentificationClaimView ClaimView(
        VideoRow video,
        IdentificationDimension dimension)
    {
        var claim = IdentificationService.Current(video, dimension);
        var review = IdentificationService.ReviewStatusOf(video, dimension);

        return claim is null
            ? new IdentificationClaimView(
                dimension,
                IdentificationResolution.Unknown,
                review,
                null,
                null,
                null,
                null,
                false,
                null,
                null)
            : new IdentificationClaimView(
                dimension,
                IdentificationResolution.Established,
                review,
                dimension == IdentificationDimension.WorkIdentification
                    ? RetainedTitle(video, claim) ?? claim.TargetTitle
                    : claim.TargetTitle,
                claim.TargetUrl,
                claim.Source,
                claim.EvidenceClass,
                claim.IsAdministrativeOverride,
                AsOffset(claim.EstablishedAt),
                AsOffset(claim.LastConfirmedAt));
    }

    /// <summary>
    /// The retained prdb title, but only while the metadata still describes the current claim. A
    /// corrected identification must not keep presenting the superseded work's fields.
    /// </summary>
    private static string? RetainedTitle(VideoRow video, IdentificationClaimRow claim) =>
        video.Metadata is { } metadata &&
        string.Equals(metadata.PrdbVideoId, claim.TargetKey, StringComparison.OrdinalIgnoreCase)
            ? metadata.Title
            : null;

    /// <summary>
    /// The Actor Credits of the Video's Established Work Identification, with the identity each
    /// one resolves to where the retained document carries one.
    /// </summary>
    public static IReadOnlyList<RetainedActor> ActorCredits(VideoRow video)
    {
        var work = IdentificationService.Current(video, IdentificationDimension.WorkIdentification);

        if (work is null ||
            RetainedTitle(video, work) is null ||
            video.Metadata?.ActorsJson is not { } json)
        {
            return [];
        }

        return RetainedActors.Deserialize(json);
    }

    public static IReadOnlyList<string> Actors(VideoRow video) =>
        ActorCredits(video).Select(actor => actor.Name).ToArray();

    public static DateTimeOffset? AsOffset(DateTime? value) =>
        value is null ? null : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
}
