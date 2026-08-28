using Prdb.Viewer.Core.Library;

namespace Prdb.Viewer.Infrastructure.Persistence;

public sealed class IdentificationClaimRow
{
    public Guid Id { get; set; }

    public Guid VideoId { get; set; }

    public VideoRow Video { get; set; } = null!;

    public IdentificationDimension Dimension { get; set; }

    public IdentificationClaimStatus Status { get; set; }

    public required string TargetKey { get; set; }

    public required string TargetTitle { get; set; }

    public string? TargetUrl { get; set; }

    public IdentificationSource Source { get; set; }

    public IdentificationEvidenceClass EvidenceClass { get; set; }

    public string? MatchedBy { get; set; }

    public bool IsAdministrativeOverride { get; set; }

    public Guid? SupportingVideoFileId { get; set; }

    public Guid? DecidedByAccountId { get; set; }

    public string? Note { get; set; }

    public DateTime EstablishedAt { get; set; }

    public DateTime? LastConfirmedAt { get; set; }

    public DateTime? EndedAt { get; set; }
}
