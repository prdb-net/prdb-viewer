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

    /// <summary>
    /// Set by an Administrator who cancels this bounded run. The lane observes it at its next safe
    /// durable boundary, so every trustworthy result committed so far is kept.
    /// </summary>
    public bool CancellationRequested { get; set; }

    /// <summary>
    /// How this run came to exist, so administrative status can say whether the installation, an
    /// Administrator, or an earlier lane asked for it.
    /// </summary>
    public BackgroundWorkTrigger Trigger { get; set; }

    /// <summary>The phase currently being advanced, reported instead of a fabricated percentage.</summary>
    public string Phase { get; set; } = string.Empty;

    /// <summary>
    /// The state this run had when an installation-wide pause stopped it, so resuming returns it to
    /// exactly the lifecycle it was in rather than restarting it.
    /// </summary>
    public BackgroundWorkState? StateBeforePause { get; set; }

    public int SkippedItemCount { get; set; }

    public DateTime? LastActivityAt { get; set; }

    public int DiscoveredCandidateCount { get; set; }

    public int CompletedItemCount { get; set; }

    public int IssueCount { get; set; }

    /// <summary>
    /// When a Waiting run may be attempted again. It always accompanies a waiting condition so the
    /// run continues by itself once the condition can change.
    /// </summary>
    public DateTime? NextAttemptAt { get; set; }

    /// <summary>
    /// The condition a Waiting run needs before it can continue.
    /// </summary>
    public string? WaitingReason { get; set; }

    public DateTime RequestedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? FinishedAt { get; set; }
}
