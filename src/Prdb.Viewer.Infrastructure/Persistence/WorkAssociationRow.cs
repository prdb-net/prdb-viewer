using Prdb.Viewer.Core.Library;

namespace Prdb.Viewer.Infrastructure.Persistence;

/// <summary>
/// A provenance-bearing assertion that two Videos carry the same content, without naming what that
/// content is.
///
/// It is not an Identification Claim, because it identifies nothing: a Video that exists because an
/// association merged two of them is still an Unknown Video. What it has instead of a target is an
/// account of itself — which two files were compared, how far apart they were, whether their
/// running times agreed, and whether a rule or a person concluded it. An association nobody can
/// account for is precisely what the evidence principle forbids, so the reading is kept here rather
/// than looked up again from a neighbourhood that a later re-hash may have replaced.
/// </summary>
public sealed class WorkAssociationRow
{
    public Guid Id { get; set; }

    public WorkAssociationStatus Status { get; set; }

    /// <summary>
    /// The Video that survives the association, and the one it absorbs. While the association is
    /// only Proposed they are simply the two sides, in the order the pair is held in.
    /// </summary>
    public Guid VideoId { get; set; }

    public VideoRow Video { get; set; } = null!;

    public Guid OtherVideoId { get; set; }

    public VideoRow OtherVideo { get; set; } = null!;

    /// <summary>The two Video Files the conclusion was drawn from, which is the evidence itself.</summary>
    public Guid VideoFileId { get; set; }

    public Guid OtherVideoFileId { get; set; }

    public int Distance { get; set; }

    public bool DurationsAgree { get; set; }

    public long DurationMilliseconds { get; set; }

    public long OtherDurationMilliseconds { get; set; }

    /// <summary>
    /// Whose conclusion this is: this installation's own inference, or an Administrator's decision.
    /// prdb never says anything about an association — it does not know this library's files.
    /// </summary>
    public IdentificationSource Source { get; set; }

    public Guid? DecidedByAccountId { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? EstablishedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }
}
