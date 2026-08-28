namespace Prdb.Viewer.Core.Library;

/// <summary>
/// Whether a Video may appear in Ordinary Discovery. It is derived in one place, because the fact
/// it approximates is not the fact the domain specifies.
/// </summary>
public enum DiscoveryReadiness
{
    /// <summary>Offered in ordinary results without any explicit preference or filter.</summary>
    ReadyForDirectPlay,

    /// <summary>Playable by some clients only; shown when the Account asks for it.</summary>
    CompatibilityUncertain,

    /// <summary>No browser is expected to play it directly.</summary>
    NotDirectlyPlayable,
}

public enum LibrarySortOrder
{
    /// <summary>Discovery Date descending, the default.</summary>
    Newest,

    TitleAscending,
}

/// <summary>
/// The rule that decides admission to Ordinary Discovery.
///
/// The domain specifies admission by Client Video Playability, which is per Account and per client.
/// That layer of the direct-play contract is not built yet, so readiness is derived here from the
/// installation-wide Direct-Play Classification instead. The approximation is deliberate and
/// recorded: a Video this browser cannot in fact play may still appear, so no surface built on this
/// rule may claim that a Video is playable for a particular User. Replacing this one method with
/// the real assessment is what the account-and-client work has to do.
/// </summary>
public static class DiscoveryReadinessRule
{
    public static DiscoveryReadiness For(DirectPlayClassification classification) =>
        classification switch
        {
            DirectPlayClassification.BaselineCandidate => DiscoveryReadiness.ReadyForDirectPlay,
            DirectPlayClassification.ClientDependent => DiscoveryReadiness.CompatibilityUncertain,
            _ => DiscoveryReadiness.NotDirectlyPlayable,
        };

    /// <summary>
    /// A Video is as ready as its most playable Available occurrence. One unsupported variant
    /// beside a baseline one must not make the Video look unplayable.
    /// </summary>
    public static DiscoveryReadiness ForVideo(IEnumerable<DirectPlayClassification> available)
    {
        var readiness = DiscoveryReadiness.NotDirectlyPlayable;

        foreach (var classification in available)
        {
            var candidate = For(classification);

            if (candidate < readiness)
            {
                readiness = candidate;
            }
        }

        return readiness;
    }

    /// <summary>
    /// Whether ordinary results include this readiness. The personal preference widens the set to
    /// everything that is not plainly unplayable; an explicit filter overrides it for one view and
    /// is applied by the caller rather than here.
    /// </summary>
    public static bool IsOrdinarilyDiscoverable(
        DiscoveryReadiness readiness,
        bool includesNotReadyForDirectPlay) =>
        readiness == DiscoveryReadiness.ReadyForDirectPlay || includesNotReadyForDirectPlay;
}
