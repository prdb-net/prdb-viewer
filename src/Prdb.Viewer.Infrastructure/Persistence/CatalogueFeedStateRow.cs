namespace Prdb.Viewer.Infrastructure.Persistence;

/// <summary>Regenerable seek/progress state. Each page commits this with the facts it applied.</summary>
public sealed class CatalogueFeedStateRow
{
    public required string Feed { get; set; }
    public required string Since { get; set; }
    public Guid? SinceId { get; set; }
    public bool BaselineComplete { get; set; }
    public DateTime? LastAppliedAt { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public sealed class SiteDirectoryStagingRow
{
    public required string SiteKey { get; set; }
    public bool IsDeleted { get; set; }
    public string? FactsJson { get; set; }
}
