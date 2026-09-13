namespace Prdb.Viewer.Infrastructure.Personal;

/// <summary>
/// One Playlist as a list of them shows it. <paramref name="Contains"/> answers a question about
/// one named Video and is false where no Video was named, so a screen offering "add to a Playlist"
/// reads the state of every Playlist at once rather than asking each of them.
/// </summary>
public sealed record PlaylistSummary(
    Guid Id,
    string Name,
    int VideoCount,
    bool Contains,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public enum PlaylistVerdict
{
    Updated,
    NotFound,
    VideoNotFound,
    InvalidName,
}

public sealed record PlaylistResult(PlaylistVerdict Verdict, PlaylistSummary? Playlist);
