namespace Prdb.Viewer.Core.Personal;

/// <summary>
/// What a Playlist's name may be, and what membership is worth to a recommendation.
/// </summary>
public static class PlaylistRule
{
    public const int MaximumNameLength = 120;

    /// <summary>
    /// A name is what the User typed with its surrounding space taken off. Nothing else is
    /// rejected: a Playlist is one Account's own organisation, and refusing a name nobody else
    /// will ever see would be the product having an opinion about somebody's filing.
    /// </summary>
    public static string? Normalize(string? name)
    {
        var trimmed = name?.Trim();

        return string.IsNullOrEmpty(trimmed) || trimmed.Length > MaximumNameLength ? null : trimmed;
    }

    /// <summary>
    /// Whether being in at least one Playlist is positive evidence about a Video. It is, and it is
    /// worth the same whether the Video is in one Playlist or in nine: a playlist may be
    /// exploratory, and somebody who files a Video in several ways has organised it rather than
    /// endorsed it several times over.
    /// </summary>
    public static bool IsPositive(int playlistMemberships) => playlistMemberships > 0;
}
