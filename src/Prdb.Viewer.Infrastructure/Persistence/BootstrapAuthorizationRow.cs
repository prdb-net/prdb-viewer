namespace Prdb.Viewer.Infrastructure.Persistence;

public sealed class BootstrapAuthorizationRow
{
    public const int TheOnlyRow = 1;

    public int Id { get; set; }

    public required byte[] TokenHash { get; set; }

    public DateTime ExpiresAt { get; set; }

    public required string DeliveryPath { get; set; }
}
