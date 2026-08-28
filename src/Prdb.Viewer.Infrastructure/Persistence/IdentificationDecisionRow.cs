using Prdb.Viewer.Core.Library;

namespace Prdb.Viewer.Infrastructure.Persistence;

public sealed class IdentificationDecisionRow
{
    public Guid Id { get; set; }

    public Guid VideoId { get; set; }

    public IdentificationDimension Dimension { get; set; }

    public IdentificationDecisionAction Action { get; set; }

    public Guid DecidedByAccountId { get; set; }

    public Guid? CandidateId { get; set; }

    public string? TargetKey { get; set; }

    public required string PriorState { get; set; }

    public required string ResultingState { get; set; }

    public bool MergedAnotherVideo { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }
}
