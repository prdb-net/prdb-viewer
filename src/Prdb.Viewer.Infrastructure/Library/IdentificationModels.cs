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

/// <summary>
/// One Actor Credit as prdb sends it with a work: the name, and the Actor's own identity where
/// prdb has one. The identity is prdb's and is never minted here (ADR 0020).
/// </summary>
public sealed record RemoteActor(string Name, string? Id);

public sealed record RemoteWork(
    string PrdbVideoId,
    string Title,
    RemoteSite? Site,
    IReadOnlyList<RemoteActor> Actors,
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

/// <summary>
/// One Actor Credit as a screen shows it: the name this Video's metadata spells, and the Actor it
/// resolves to where prdb sent an identity. A credit with no identity is still shown and still
/// filters the Library; it simply has nothing to open (ADR 0020).
/// </summary>
public sealed record ActorCreditView(string Name, string? ActorId);

public sealed record IdentificationSummary(
    IdentificationClaimView Work,
    IdentificationClaimView Site,
    IReadOnlyList<ActorCreditView> Actors,
    DateTimeOffset? MetadataFetchedAt);

/// <summary>
/// What the proposal is being compared against: the work prdb says this is, in its own terms.
/// Comparing two pictures is the decision; comparing a file name to a title is a guess.
/// </summary>
public sealed record IdentificationProposalView(
    string Title,
    string? SiteTitle,
    string? SiteUrl,
    IReadOnlyList<string> Actors,
    // An address in this installation, never at prdb. Null unless a picture is actually held.
    string? ArtworkUrl,
    ProposedWorkArtworkState ArtworkState,
    DateTimeOffset? ReleaseDate,
    long? DurationMilliseconds,
    DateTimeOffset FetchedAt);

/// <summary>
/// One decision this case offers for one candidate, and what the installation looks like once it
/// is taken — said before it is taken rather than in a preview it is too late to read.
/// </summary>
/// <remarks>
/// A refused decision carries its reason here instead of leaving it to be read off a disabled
/// button, so a reason belongs to the control it locks rather than to the row.
/// </remarks>
public sealed record IdentificationDecisionOutlook(
    IdentificationDecisionAction Action,
    string? Refusal,
    string Outcome);

public sealed record IdentificationCandidateView(
    Guid Id,
    IdentificationDimension Dimension,
    IdentificationCandidateStatus Status,
    string TargetTitle,
    string? TargetUrl,
    IdentificationEvidenceClass EvidenceClass,
    IdentificationReviewReason Reason,
    IdentificationSource Source,
    string EvidenceSummary,
    Guid? SupportingVideoFileId,
    IdentificationProposalView? Proposal,
    IReadOnlyList<IdentificationDecisionOutlook> Decisions,
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
