namespace Prdb.Viewer.Core.Personal;

/// <summary>
/// How Videos are chosen for Long unseen and Not yet discovered.
///
/// As with <see cref="ReturnInterestPolicy"/>, the semantics are ADR 0022's and the numbers are
/// initial tuning. The two sections have opposite problems — one is about forgetting, the other
/// about never having looked — and the rules below are what keeps either from turning into a
/// narrow loop of the same Actors.
/// </summary>
public static class ResurfacingPolicy
{
    /// <summary>
    /// How long since the last confirmed Active Watching before a Video counts as long unseen.
    /// </summary>
    public static readonly TimeSpan LongUnseenAfter = TimeSpan.FromDays(14);

    /// <summary>
    /// How many Videos an Actor or a Site must have positive evidence on before this Account is
    /// taken to have any affinity with them at all. One is never enough: watching a single Video
    /// says something about that Video, and nothing whatever about everybody in it.
    /// </summary>
    public const int MinimumAffinityEvidence = 2;

    /// <summary>
    /// Added to an Actor's or a Site's library count when their affinity is worked out, so that a
    /// name with two Videos and two positives does not outrank everything on a perfect record.
    /// </summary>
    public const double AffinitySmoothing = 5.0;

    /// <summary>What an explicitly kept Favourite Actor is worth, which is more than any inference.</summary>
    public const double FavouriteActorAffinity = 0.5;

    /// <summary>The most affinity one candidate can accumulate, however many names it shares.</summary>
    public const double AffinityCeiling = 1.0;

    /// <summary>
    /// How much of Not yet discovered is led by affinity. The rest is chosen without reference to
    /// any inferred taste, which is the part that can still surprise somebody — and the part that
    /// reaches the Videos with barely any metadata to infer from.
    /// </summary>
    public const double AffinityShare = 2.0 / 3.0;

    /// <summary>
    /// How much an Actor or a Site is liked, as a rate rather than as a count.
    /// </summary>
    /// <remarks>
    /// Counting would hand the section to whoever appears most often: an Actor with two hundred
    /// Videos here accumulates positives simply by being everywhere. Dividing by how many Videos
    /// they have asks the better question — of the ones this Account met, how many landed — and
    /// the smoothing term keeps a two-for-two record from beating a twenty-for-forty one.
    /// </remarks>
    public static double Affinity(int positivelyEvidenced, int inLibrary) =>
        positivelyEvidenced < MinimumAffinityEvidence
            ? 0
            : positivelyEvidenced / (Math.Max(inLibrary, positivelyEvidenced) + AffinitySmoothing);

    /// <summary>
    /// One candidate's affinity: what its Actors and its Site are worth together, held under the
    /// ceiling so that a Video with a large cast cannot lead on arithmetic alone.
    /// </summary>
    public static double CandidateAffinity(IEnumerable<double> contributions) =>
        Math.Min(AffinityCeiling, contributions.Sum());

    /// <summary>
    /// Whether a Video with this last-watched moment is long unseen. A Video whose watching
    /// happened at a moment nothing recorded — an installation restored from before that was
    /// kept — is long unseen too: it was watched, and it plainly has not been since.
    /// </summary>
    public static bool IsLongUnseen(DateTimeOffset? lastWatchedAt, DateTimeOffset now) =>
        lastWatchedAt is not { } watched || now - watched >= LongUnseenAfter;

    /// <summary>
    /// How many of a page of discoveries are led by affinity, given how many the affinity pool can
    /// actually supply. A shortage on either side is filled from the other rather than left as a
    /// gap, and with no affinity evidence at all the whole page is independent.
    /// </summary>
    public static (int Affinity, int Independent) Mix(int take, int affinityAvailable, int independentAvailable)
    {
        var wanted = (int)Math.Round(take * AffinityShare, MidpointRounding.ToZero);
        var affinity = Math.Min(wanted, affinityAvailable);
        var independent = Math.Min(take - affinity, independentAvailable);

        return (Math.Min(affinityAvailable, take - independent), independent);
    }

    /// <summary>
    /// A deterministic order over candidates, so that one page generation is reproducible and the
    /// next seed is a different page rather than the same one again.
    /// </summary>
    /// <remarks>
    /// It is a hash rather than a shuffle with a random source because paging has to be able to
    /// ask for the same order twice, minutes apart, in a different process. The same seed and the
    /// same candidates give the same order, always.
    /// </remarks>
    public static IEnumerable<T> InSeededOrder<T>(IEnumerable<T> candidates, Func<T, Guid> identity, int seed) =>
        candidates.OrderBy(candidate => SelectionKey(identity(candidate), seed)).ThenBy(identity);

    /// <summary>
    /// Where a rotating window over a pool starts, so that successive seeds walk through the whole
    /// of it rather than returning to the newest part of it every time.
    /// </summary>
    public static int WindowStart(int poolSize, int windowSize, int seed)
    {
        if (poolSize <= windowSize)
        {
            return 0;
        }

        var steps = (poolSize + windowSize - 1) / windowSize;

        return (Math.Abs(seed) % steps) * windowSize % poolSize;
    }

    private static ulong SelectionKey(Guid identity, int seed)
    {
        // A cheap, stable mixing of the identity with the seed. It has to be the same everywhere
        // and forever, so it is written out rather than taken from a hash whose implementation is
        // free to change between runtimes.
        Span<byte> bytes = stackalloc byte[16];
        identity.TryWriteBytes(bytes);
        var mixed = 14695981039346656037UL ^ (ulong)(uint)seed;

        foreach (var value in bytes)
        {
            mixed = (mixed ^ value) * 1099511628211UL;
        }

        return mixed;
    }
}
