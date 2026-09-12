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

    /// <summary>
    /// What prdb says about the work this candidate proposes, when the answer that produced it
    /// carried details for that work. It is what the review compares the Video against: a title
    /// beside a file name is a guess, and the Site, the Actors and the picture are the difference.
    /// </summary>
    public Guid? ProposedWorkId { get; set; }

    public ProposedWorkRow? ProposedWork { get; set; }

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

    /// <summary>
    /// The other Video File of this installation that this proposal came from, when it came from a
    /// Perceptual Neighbourhood. What the review compares is then two files of this library rather
    /// than a file against a work in prdb's catalogue, and it has to be able to show the other one.
    /// </summary>
    public Guid? NeighbourVideoFileId { get; set; }

    /// <summary>
    /// The reading the proposal was made on: how far apart the two Perceptual Hashes were, and
    /// whether the two running times agreed. They are retained on the candidate rather than looked
    /// up again, because a rejected proposal outlives the neighbourhood it was drawn from and the
    /// question it answers later is whether a new reading is materially stronger than this one.
    /// </summary>
    public int? NeighbourDistance { get; set; }

    public bool? NeighbourDurationsAgree { get; set; }

    public Guid? PriorRejectionId { get; set; }

    public Guid? DecidedByAccountId { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }
}
