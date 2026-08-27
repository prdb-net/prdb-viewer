namespace Prdb.Viewer.Infrastructure.Persistence;

public sealed class SessionRow
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public AccountRow Account { get; set; } = null!;

    public required byte[] TokenHash { get; set; }

    public required byte[] CsrfTokenHash { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }
}
