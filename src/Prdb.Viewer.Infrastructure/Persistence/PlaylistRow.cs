namespace Prdb.Viewer.Infrastructure.Persistence;

/// <summary>
/// One Account's named, ordered set of Videos. It belongs to the Account rather than to the
/// installation: no other Account and no Administrator reaches one, and deleting it deletes the
/// organisation rather than anything it organised.
/// </summary>
public sealed class PlaylistRow
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public AccountRow Account { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ICollection<PlaylistEntryRow> Entries { get; set; } = [];
}

/// <summary>
/// One Video's place in one Playlist. The position is the order the User arranged by hand, kept
/// contiguous from zero so that a move is a renumbering rather than a search for a gap.
/// </summary>
public sealed class PlaylistEntryRow
{
    public Guid PlaylistId { get; set; }

    public PlaylistRow Playlist { get; set; } = null!;

    public Guid VideoId { get; set; }

    public VideoRow Video { get; set; } = null!;

    public int Position { get; set; }

    public DateTime AddedAt { get; set; }
}
