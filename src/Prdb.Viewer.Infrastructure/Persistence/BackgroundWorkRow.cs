using Prdb.Viewer.Core.Library;

namespace Prdb.Viewer.Infrastructure.Persistence;

public sealed class BackgroundWorkRow
{
    public Guid Id { get; set; }

    public BackgroundWorkCategory Category { get; set; }

    public BackgroundWorkState State { get; set; }

    public Guid LibraryDirectoryId { get; set; }

    public LibraryDirectoryRow LibraryDirectory { get; set; } = null!;

    public int ConfigurationGeneration { get; set; }

    public Guid? LibraryScanId { get; set; }

    public string? PendingDirectoriesJson { get; set; }

    public bool CoverageComplete { get; set; } = true;

    public bool FollowUpRequested { get; set; }

    public int DiscoveredCandidateCount { get; set; }

    public int CompletedItemCount { get; set; }

    public int IssueCount { get; set; }

    public DateTime RequestedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? FinishedAt { get; set; }
}
