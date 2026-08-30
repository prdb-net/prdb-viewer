namespace Prdb.Viewer.Core.Library;

public enum LibrarySortOrder
{
    /// <summary>Discovery Date descending, the default.</summary>
    Newest,

    TitleAscending,

    /// <summary>Video Quality descending, with the newest first inside one band.</summary>
    QualityDescending,
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
