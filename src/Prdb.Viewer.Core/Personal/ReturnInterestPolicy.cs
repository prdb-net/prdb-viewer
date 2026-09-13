namespace Prdb.Viewer.Core.Personal;

/// <summary>
/// One Viewing Session, as the preference rules read it.
/// </summary>
public sealed record ViewingSessionEvidence(
    long ActiveWatchingMilliseconds,
    long LongestUninterruptedRunMilliseconds,
    PlaybackDeparture Departure);

/// <summary>
/// Everything one Account's Personal State says about one Video, gathered for the ranking. It is
/// deliberately the whole input: the policy is a pure function of this, so an ordering can be
/// reproduced from what it was given rather than from the state of a database at the time.
/// </summary>
public sealed record ReturnInterestEvidence(
    IReadOnlyList<ViewingSessionEvidence> Sessions,
    PersonalReaction? Reaction = null,
    bool Favourite = false,
    int PlaylistMemberships = 0,
    /// <summary>
    /// Whether this Video was watched during the Browsing Visit that is open now. It moves the
    /// Video down within this visit and is gone by the next one, which is what replaces a fixed
    /// rewatch cooldown.
    /// </summary>
    bool WatchedInThisVisit = false,
    /// <summary>
    /// Watching this Account did before the per-session evidence was retained. It establishes that
    /// the Video was watched and nothing more: it is never expanded into sessions that were not
    /// recorded.
    /// </summary>
    bool HasPriorWatching = false);

/// <summary>
/// Why a Video is being offered, in facts the reader's own activity produced. Every one of these
/// is about this Video: none of them generalises into a claim about an Actor or a Site, because
/// watching one Video is not evidence of liking everybody in it.
/// </summary>
public enum ReturnInterestReason
{
    Loved,
    Liked,
    InAPlaylist,
    Favourite,
    WatchedRepeatedly,
    WatchedAtLength,
    WatchedWithoutInterruption,
    WatchedBefore,
    JustWatchedInThisVisit,
}

/// <summary>
/// What the policy concluded about one Video: whether it may be offered at all, which tier it
/// belongs to, what it scored, and the facts behind that.
/// </summary>
public sealed record ReturnInterest(
    bool Excluded,
    int Tier,
    double Score,
    IReadOnlyList<ReturnInterestReason> Reasons)
{
    /// <summary>Whether there is any positive evidence at all, which behaviour-only items need.</summary>
    public bool IsPositive => !Excluded && (Tier > 0 || Score > 0);
}

/// <summary>
/// How much a User wants to watch a Video again, from their own Personal State on this
/// installation.
///
/// The semantics are ADR 0022's and are the User's own approved rules. The numbers here are
/// initial tuning: they are a first attempt at expressing those rules and may be changed, but only
/// in a way that preserves the ordering examples the tests state.
/// </summary>
/// <remarks>
/// Two things are deliberately absent. Viewing Completion and the fraction of a Video watched are
/// not consulted at all, because a Video that was finished is not spent and a minute of a short
/// clip is the same minute of interest as a minute of a long one. And Play Count is not consulted
/// either: it qualifies on its own threshold, which is not this one, and reading it here would
/// quietly substitute a different rule for the one that was agreed.
/// </remarks>
public static class ReturnInterestPolicy
{
    /// <summary>The Active Watching in one session below which nothing positive is concluded.</summary>
    public const long MildlyPositiveMilliseconds = 60_000;

    /// <summary>
    /// An Uninterrupted Run at least this long adds a little to a session that already qualifies.
    /// Watching a minute straight through says slightly more than a minute assembled from six
    /// stretches, and only slightly: seeking is neutral, not a fault.
    /// </summary>
    public const long UninterruptedRunMilliseconds = 60_000;

    public const double UninterruptedRunContribution = 0.5;

    /// <summary>
    /// How many sessions are counted. Beyond this a Video is already established as one somebody
    /// returns to, and further sessions say the same thing again — which is the diminishing return
    /// the rules ask for, expressed as a limit rather than as a curve nobody could predict.
    /// </summary>
    public const int CountedSessions = 5;

    /// <summary>What each further meaningful visit adds, and how many of them are counted.</summary>
    public const double RepeatVisitContribution = 0.6;

    public const int CountedRepeatVisits = 5;

    /// <summary>
    /// Being in a Playlist at all. It is worth less than a Like, because filing something is not
    /// the same as saying you liked it, and it is worth the same whether the Video is in one
    /// Playlist or nine.
    /// </summary>
    public const double PlaylistContribution = 1.0;

    public const double FavouriteContribution = 1.0;

    /// <summary>
    /// Watching shorter than this, followed by a positively observed move to another Video, is the
    /// one negative signal behaviour can produce. The cutoff is initial tuning; the rule that only
    /// a deliberate departure counts is not.
    /// </summary>
    public const long ShortVisitMilliseconds = 15_000;

    public const double ShortVisitPenalty = 0.5;

    /// <summary>How far short visits can pull a Video down in total, however many there were.</summary>
    public const double MaximumShortVisitPenalty = 1.5;

    /// <summary>
    /// How much of what the strongest Viewing Session established survives any number of short
    /// visits. It is what stops a run of glances from erasing a real session: a Video somebody
    /// once watched properly stays a Video somebody once watched properly.
    /// </summary>
    public const double EstablishedEvidenceFloor = 0.5;

    /// <summary>
    /// What a Video watched during this Browsing Visit gives up while the visit lasts. It moves
    /// down rather than out, and a later visit removes the adjustment entirely — there is no fixed
    /// rewatch cooldown, and a favourite may lead again tomorrow.
    /// </summary>
    public const double RecentlyWatchedAdjustment = 2.0;

    /// <summary>An explicit Love, an explicit Like, and everything else, in that order.</summary>
    public const int LovedTier = 2;

    public const int LikedTier = 1;

    public static ReturnInterest Evaluate(ReturnInterestEvidence evidence)
    {
        // A Dislike is a hard exclusion and is not a score of any kind. Nothing below it runs,
        // which is what makes it independent of every other piece of evidence there could be.
        if (PersonalReactionRule.ExcludesFromRecommendations(evidence.Reaction))
        {
            return new ReturnInterest(true, 0, 0, []);
        }

        var reasons = new List<ReturnInterestReason>();
        var tier = evidence.Reaction switch
        {
            PersonalReaction.Love => LovedTier,
            PersonalReaction.Like => LikedTier,
            _ => 0,
        };

        if (tier == LovedTier) reasons.Add(ReturnInterestReason.Loved);
        if (tier == LikedTier) reasons.Add(ReturnInterestReason.Liked);

        var contributions = evidence.Sessions
            .Select(SessionContribution)
            .Where(contribution => contribution > 0)
            .OrderByDescending(contribution => contribution)
            .ToArray();
        var watched = contributions.Take(CountedSessions).Sum();
        var strongest = contributions.Length == 0 ? 0 : contributions[0];
        var repeats = Math.Min(Math.Max(contributions.Length - 1, 0), CountedRepeatVisits);
        var positive = watched + (repeats * RepeatVisitContribution);

        if (evidence.PlaylistMemberships > 0)
        {
            positive += PlaylistContribution;
            reasons.Add(ReturnInterestReason.InAPlaylist);
        }

        if (evidence.Favourite)
        {
            positive += FavouriteContribution;
            reasons.Add(ReturnInterestReason.Favourite);
        }

        if (repeats > 0) reasons.Add(ReturnInterestReason.WatchedRepeatedly);
        else if (contributions.Length > 0) reasons.Add(ReturnInterestReason.WatchedAtLength);

        if (evidence.Sessions.Any(session =>
                Qualifies(session) &&
                session.LongestUninterruptedRunMilliseconds >= UninterruptedRunMilliseconds))
        {
            reasons.Add(ReturnInterestReason.WatchedWithoutInterruption);
        }

        if (contributions.Length == 0 && evidence.HasPriorWatching)
        {
            reasons.Add(ReturnInterestReason.WatchedBefore);
        }

        // A short visit followed by a deliberate departure is the only negative behaviour can
        // produce. It moves a Video down and can never erase it: whatever the strongest real
        // session established, half of it survives any number of glances. An explicit Like or Love
        // keeps the penalty off altogether — somebody who loved a Video and then looked at it for
        // eight seconds has not changed their mind by looking.
        var penalty = tier > 0 ? 0 : Math.Min(
            MaximumShortVisitPenalty,
            evidence.Sessions.Count(IsRejectedQuickly) * ShortVisitPenalty);
        var score = Math.Max(strongest * EstablishedEvidenceFloor, positive - penalty);

        if (evidence.WatchedInThisVisit)
        {
            score = Math.Max(0, score - RecentlyWatchedAdjustment);
            reasons.Add(ReturnInterestReason.JustWatchedInThisVisit);
        }

        return new ReturnInterest(false, tier, score, reasons);
    }

    /// <summary>
    /// What one Viewing Session is worth. Absolute Active Watching decides it — never a fraction
    /// of the Video's duration — and the steps grow more slowly as they go up, because the
    /// difference between one minute and five says far more than the difference between an hour
    /// and two.
    /// </summary>
    public static double SessionContribution(ViewingSessionEvidence session)
    {
        if (!Qualifies(session))
        {
            return 0;
        }

        var duration = session.ActiveWatchingMilliseconds switch
        {
            >= 600_000 => 4,
            >= 300_000 => 3,
            >= 120_000 => 2,
            _ => 1,
        };

        return session.LongestUninterruptedRunMilliseconds >= UninterruptedRunMilliseconds
            ? duration + UninterruptedRunContribution
            : duration;
    }

    private static bool Qualifies(ViewingSessionEvidence session) =>
        session.ActiveWatchingMilliseconds >= MildlyPositiveMilliseconds;

    /// <summary>
    /// A very short visit that ended because the User positively went to another Video. Every other
    /// way of ending a short session — a failure, a closed tab, an inactivity timeout, a session
    /// nothing accounts for — says nothing and is treated as saying nothing.
    /// </summary>
    private static bool IsRejectedQuickly(ViewingSessionEvidence session) =>
        session.Departure == PlaybackDeparture.AnotherVideo &&
        session.ActiveWatchingMilliseconds < ShortVisitMilliseconds;
}
