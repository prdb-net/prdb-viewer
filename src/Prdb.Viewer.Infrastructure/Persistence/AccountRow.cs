using Prdb.Viewer.Core.Access;

namespace Prdb.Viewer.Infrastructure.Persistence;

public sealed class AccountRow
{
    public Guid Id { get; set; }

    public required string Username { get; set; }

    public required string NormalizedUsername { get; set; }

    public string? Email { get; set; }

    public required string PasswordHash { get; set; }

    public AccountAuthority Authority { get; set; }

    public AccountState State { get; set; }

    public DateTime RegisteredAt { get; set; }

    public DateTime? ApprovedAt { get; set; }

    public DateTime? DisabledAt { get; set; }

    public ICollection<PersonalVideoStateRow> PersonalVideoStates { get; set; } = [];

    public ICollection<PlaybackAttemptRow> PlaybackAttempts { get; set; } = [];
}
