using Prdb.Viewer.Core.Library;

namespace Prdb.Viewer.Infrastructure.Persistence;

public sealed class IdentificationCandidateRow
{
    public Guid Id { get; set; }

    public Guid VideoId { get; set; }

    public VideoRow Video { get; set; } = null!;

    public IdentificationDimension Dimension { get; set; }

    public IdentificationCandidateStatus Status { get; set; }

    public required string TargetKey { get; set; }

    public required string TargetTitle { get; set; }

    public string? TargetUrl { get; set; }

    public IdentificationEvidenceClass EvidenceClass { get; set; }

    public IdentificationReviewReason Reason { get; set; }

    /// <summary>
    /// Where the proposal came from. A candidate derived from a Video File's own path is not the
    /// same kind of evidence as one the remote catalogue offered, and an Administrator reviewing it
    /// has to be able to tell them apart.
    /// </summary>
    public IdentificationSource Source { get; set; }

    public string? MatchedBy { get; set; }

    public string? Confidence { get; set; }

    /// <summary>
    /// A stable fingerprint of the material evidence behind this candidate. Rejecting a candidate
    /// suppresses the same proposed target supported by the same fingerprint; materially different
    /// evidence produces a new fingerprint and may therefore return for review.
    /// </summary>
    public required string EvidenceKey { get; set; }

    public Guid? SupportingVideoFileId { get; set; }

    public Guid? PriorRejectionId { get; set; }

    public Guid? DecidedByAccountId { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }
}
