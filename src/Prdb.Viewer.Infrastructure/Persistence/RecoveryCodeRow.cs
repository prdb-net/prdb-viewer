namespace Prdb.Viewer.Infrastructure.Persistence;

public sealed class RecoveryCodeRow
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public AccountRow Account { get; set; } = null!;

    public required byte[] TokenHash { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? ConsumedAt { get; set; }

    public string? DeliveryPath { get; set; }
}
