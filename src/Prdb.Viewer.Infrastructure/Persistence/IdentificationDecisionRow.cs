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

    /// <summary>
    /// The act this decision was part of, where one decision settled a whole Identification Review
    /// Group. Each case keeps its own record with its own prior and resulting state; this is what
    /// makes them one decision by one Account at one moment, so a group accepted in error can be
    /// found and undone as what it was rather than as four hundred coincidences.
    /// </summary>
    public Guid? GroupDecisionId { get; set; }

    public DateTime CreatedAt { get; set; }
}
