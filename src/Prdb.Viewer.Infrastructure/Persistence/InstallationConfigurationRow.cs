using Prdb.Viewer.Core.Configuration;

namespace Prdb.Viewer.Infrastructure.Persistence;

public sealed class InstallationConfigurationRow
{
    public const int TheOnlyRow = 1;

    public int Id { get; set; } = TheOnlyRow;

    public string? ActivePrdbCredential { get; set; }

    public string? PendingPrdbCredential { get; set; }

    public Guid? PendingCredentialRevision { get; set; }

    public PrdbConnectionStatus PrdbConnectionStatus { get; set; } = PrdbConnectionStatus.Missing;

    public DateTime? LastConnectionAttemptAt { get; set; }

    public DateTime? LastConnectionVerifiedAt { get; set; }

    public PrdbConnectionIssue? LastConnectionIssue { get; set; }

    public DateTime? ConfiguredAt { get; set; }

    public DateTime? FirstPlayableVideoReachedAt { get; set; }

    /// <summary>
    /// When prdb last answered with its list of sites. It records the attempt rather than its
    /// contents, so an answer that happens to be empty is not asked for again every slice. A
    /// Backup Archive carries the configuration, so a Restore clears this and fetches again.
    /// </summary>
    public DateTime? SiteDirectoryFetchedAt { get; set; }

    /// <summary>
    /// The installation-wide Background Work pause. It is a deliberate administrative state rather
    /// than a failure, survives restart, and is carried by a Backup Archive.
    /// </summary>
    public bool BackgroundWorkPaused { get; set; }

    public DateTime? BackgroundWorkPausedAt { get; set; }
}
