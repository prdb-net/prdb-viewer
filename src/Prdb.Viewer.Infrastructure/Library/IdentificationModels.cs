using Prdb.Viewer.Core.Library;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>One file offered to the remote identification ladder.</summary>
public sealed record RemoteIdentificationRequest(
    Guid VideoFileId,
    string FileName,
    long FileSize,
    string? OsHash,
    string? PerceptualHash);

public sealed record RemoteSite(string Id, string Title, string? Url);

public sealed record RemoteWork(
    string PrdbVideoId,
    string Title,
    RemoteSite? Site,
    IReadOnlyList<string> Actors,
    string? ArtworkUrl,
    DateTime? ReleaseDate,
    long? DurationMilliseconds);

/// <summary>What the remote ladder made of one Video File.</summary>
public sealed record RemoteIdentification(
    Guid VideoFileId,
    RemoteMatchKind? MatchedBy,
    RemoteMatchConfidence Confidence,
    string? PrdbVideoId,
    IReadOnlyList<string> Candidates,
    RemoteSite? Site,
    RemoteWork? Work);

public enum IdentificationBatchStatus
{
    Identified,
    Rejected,
    Unavailable,
}

public sealed record IdentificationBatchResult(
    IdentificationBatchStatus Status,
    IReadOnlyList<RemoteIdentification> Results,
    string? Detail = null);

public interface IPrdbIdentificationClient
{
    Task<IdentificationBatchResult> IdentifyAsync(
        string credential,
        IReadOnlyList<RemoteIdentificationRequest> files,
        CancellationToken cancellationToken = default);
}

public sealed record IdentificationClaimView(
    IdentificationDimension Dimension,
    IdentificationResolution Resolution,
    IdentificationReviewStatus ReviewStatus,
    string? TargetTitle,
    string? TargetUrl,
    IdentificationSource? Source,
    IdentificationEvidenceClass? EvidenceClass,
    bool AdministrativeOverride,
    DateTimeOffset? EstablishedAt,
    DateTimeOffset? LastConfirmedAt);

public sealed record IdentificationSummary(
    IdentificationClaimView Work,
    IdentificationClaimView Site,
    IReadOnlyList<string> Actors,
    DateTimeOffset? MetadataFetchedAt);

public sealed record IdentificationCandidateView(
    Guid Id,
    IdentificationDimension Dimension,
    IdentificationCandidateStatus Status,
    string TargetTitle,
    string? TargetUrl,
    IdentificationEvidenceClass EvidenceClass,
    IdentificationReviewReason Reason,
    string EvidenceSummary,
    Guid? SupportingVideoFileId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt);

public sealed record IdentificationQueueItem(
    Guid VideoId,
    int CaseVersion,
    string DisplayLabel,
    string? PreviewUrl,
    IdentificationDimension Dimension,
    IdentificationResolution CurrentResolution,
    string? CurrentTargetTitle,
    IdentificationCandidateView Candidate,
    int AffectedVideoFileCount,
    string Reason);

public sealed record IdentificationCaseFile(
    Guid Id,
    string RelativePath,
    VideoFileAvailability Availability,
    DirectPlayClassification DirectPlayClassification,
    string ContainerFormat,
    string VideoCodec,
    string? AudioCodec,
    long DurationMilliseconds,
    string? OsHashSummary,
    string? PerceptualHashSummary,
    VideoFileHashState HashState);

public sealed record IdentificationDecisionView(
    Guid Id,
    IdentificationDimension Dimension,
    IdentificationDecisionAction Action,
    string PriorState,
    string ResultingState,
    bool MergedAnotherVideo,
    string? Note,
    DateTimeOffset CreatedAt);

public sealed record IdentificationCase(
    Guid VideoId,
    int CaseVersion,
    string DisplayLabel,
    string? PreviewUrl,
    IdentificationSummary Identification,
    IReadOnlyList<IdentificationCandidateView> OpenCandidates,
    IReadOnlyList<IdentificationCandidateView> CandidateHistory,
    IReadOnlyList<IdentificationCaseFile> VideoFiles,
    IReadOnlyList<IdentificationDecisionView> Decisions,
    IReadOnlyList<IdentificationDecisionAction> UnavailableSiteActions,
    string Explanation);

public enum IdentificationDecisionVerdict
{
    Applied,
    Preview,
    Stale,
    NotFound,
    NoteRequired,
    InvalidTarget,
    ActionUnavailable,
}

public sealed record IdentificationConsequence(
    string ClaimTransition,
    string CandidateTransition,
    int AffectedVideoFileCount,
    IdentificationReviewStatus ResultingReviewStatus,
    bool MergesAnotherVideo,
    string? MergeSummary,
    bool RequiresNote);

public sealed record IdentificationDecisionRequest(
    IdentificationDecisionAction Action,
    IdentificationDimension Dimension,
    int CaseVersion,
    bool Confirm,
    Guid? CandidateId = null,
    string? TargetKey = null,
    string? TargetTitle = null,
    string? TargetUrl = null,
    string? Note = null,
    IReadOnlyList<Guid>? SeparatedVideoFileIds = null,
    bool RetainPersonalStateWithContinuing = true);

public sealed record IdentificationDecisionResult(
    IdentificationDecisionVerdict Verdict,
    IdentificationConsequence? Consequence = null,
    IdentificationCase? Case = null);
