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
}
