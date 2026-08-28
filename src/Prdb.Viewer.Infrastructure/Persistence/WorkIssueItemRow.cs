namespace Prdb.Viewer.Infrastructure.Persistence;

/// <summary>
/// One item affected by an aggregated Work Issue. Keeping the complete list lets a systematic
/// obstacle report one message with a count while an Administrator can still see every item it
/// covers.
/// </summary>
public sealed class WorkIssueItemRow
{
    public Guid Id { get; set; }

    public Guid WorkIssueId { get; set; }

    public WorkIssueRow WorkIssue { get; set; } = null!;

    public required string Scope { get; set; }

    public string? ContainerPath { get; set; }

    public Guid? VideoFileId { get; set; }

    public int OccurrenceCount { get; set; } = 1;

    public DateTime FirstOccurredAt { get; set; }

    public DateTime LastOccurredAt { get; set; }
}
