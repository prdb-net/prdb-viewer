namespace Prdb.Viewer.Core.Library;

public enum LibrarySortOrder
{
    /// <summary>Discovery Date descending, the default.</summary>
    Newest,

    TitleAscending,

    /// <summary>Video Quality descending, with the newest first inside one band.</summary>
    QualityDescending,

    /// <summary>
    /// The longest runtime among a Video's Available occurrences first. Videos whose runtime
    /// inspection never established come last, newest first among themselves.
    /// </summary>
    LongestFirst,

    /// <summary>
    /// The Videos this Account played most recently first, by when their Personal Play State last
    /// changed. Videos the Account never played come last, newest first among themselves.
    /// </summary>
    RecentlyPlayed,

    /// <summary>
    /// The highest Personal Rating first. Videos this Account has not rated come last, newest
    /// first among themselves.
    /// </summary>
    BestRated,

    /// <summary>
    /// The order a Personal Shelf keeps: Continue Watching by latest qualifying activity, Favourites
    /// by latest addition, Watch Later by earliest addition, because it is a queue. With several
    /// shelves chosen, the latest entry into any of them leads; with none chosen it is Newest.
    /// </summary>
    ShelfOrder,
}

/// <summary>
/// The rule that decides admission to Ordinary Discovery. Admission is by Client Video Playability,
/// which is derived per Account and per client from the evidence that Account's client has
/// produced, so the same library admits different Videos on different devices.
/// </summary>
public static class LibraryAdmissionRule
{
    /// <summary>
    /// Whether ordinary results include this playability. The personal preference widens the set to
    /// everything the client has not ruled out and everything it cannot play at all; an explicit
    /// filter overrides it for one view and is applied by the caller rather than here.
    /// </summary>
    public static bool IsOrdinarilyDiscoverable(
        ClientVideoPlayability playability,
        bool includesNotReadyForDirectPlay) =>
        playability == ClientVideoPlayability.ReadyForDirectPlay || includesNotReadyForDirectPlay;
}
