using Prdb.Viewer.Core.Personal;

using Xunit;

namespace Prdb.Viewer.Core.Tests.Personal;

/// <summary>
/// The ordering examples ADR 0022 states, as a table. The numbers in the policy are initial tuning
/// and may change; these orderings are what they are tuned to produce, so a change that breaks one
/// of these is a change to the agreed rules rather than to the tuning.
/// </summary>
public sealed class ReturnInterestPolicyTests
{
    [Fact]
    public void A_minute_counts_the_same_however_it_was_assembled_and_a_straight_one_a_little_more()
    {
        var fragmented = Score(Session(60_000, run: 10_000));
        var continuous = Score(Session(60_000, run: 60_000));

        Assert.True(fragmented > 0);
        Assert.True(continuous > fragmented);
        // Only a little more: seeking is neutral rather than a fault, so six ten-second stretches
        // remain a minute of interest.
        Assert.Equal(ReturnInterestPolicy.UninterruptedRunContribution, continuous - fragmented, 3);
    }

    [Fact]
    public void Under_a_minute_says_nothing_positive_however_long_the_run_was()
    {
        Assert.Equal(0, Score(Session(59_999, run: 59_999)));
        Assert.Equal(0, Score(Session(1_000, run: 1_000)));
    }

    [Fact]
    public void Longer_sessions_are_worth_more_and_by_smaller_steps()
    {
        var steps = new[] { 60_000L, 120_000L, 300_000L, 600_000L, 7_200_000L }
            .Select(active => Score(Session(active)))
            .ToArray();

        Assert.Equal(steps.OrderBy(step => step), steps);
        // Two minutes over one is worth a whole step; two hours over ten minutes is worth none.
        Assert.True(steps[1] - steps[0] > 0);
        Assert.Equal(steps[3], steps[4]);
    }

    [Fact]
    public void Coming_back_says_more_than_one_long_sitting_and_stops_saying_more_eventually()
    {
        var once = Score(Session(600_000));
        var thrice = Score(Session(120_000), Session(120_000), Session(120_000));
        var relentless = Score(Enumerable.Repeat(Session(120_000), 20).ToArray());

        Assert.True(thrice > once);
        Assert.True(relentless > thrice);
        // Bounded: the twentieth visit does not keep pushing a Video above everything else.
        Assert.True(relentless < thrice * 3);
    }

    [Fact]
    public void Duration_alone_never_outranks_an_explicit_like_or_love()
    {
        var watchedForHours = Evaluate(new ReturnInterestEvidence(
            Enumerable.Repeat(Session(3_600_000), 10).ToArray()));
        var liked = Evaluate(new ReturnInterestEvidence([], PersonalReaction.Like));
        var loved = Evaluate(new ReturnInterestEvidence([], PersonalReaction.Love));

        Assert.True(liked.Tier > watchedForHours.Tier);
        Assert.True(loved.Tier > liked.Tier);
        // An explicit positive is offerable with no watching history at all; behaviour-only items
        // need evidence.
        Assert.True(liked.IsPositive);
        Assert.False(Evaluate(new ReturnInterestEvidence([])).IsPositive);
    }

    [Fact]
    public void A_dislike_overrides_every_positive_signal_there_could_be()
    {
        var everything = Evaluate(new ReturnInterestEvidence(
            Enumerable.Repeat(Session(3_600_000, run: 3_600_000), 10).ToArray(),
            PersonalReaction.Dislike,
            Favourite: true,
            PlaylistMemberships: 4));

        Assert.True(everything.Excluded);
        Assert.False(everything.IsPositive);
        Assert.Empty(everything.Reasons);
    }

    [Fact]
    public void A_shrug_says_nothing_and_suppresses_nothing()
    {
        var shrugged = Evaluate(new ReturnInterestEvidence([Session(600_000)], PersonalReaction.Shrug));
        var unsaid = Evaluate(new ReturnInterestEvidence([Session(600_000)]));

        Assert.False(shrugged.Excluded);
        Assert.Equal(unsaid.Tier, shrugged.Tier);
        Assert.Equal(unsaid.Score, shrugged.Score);
    }

    [Fact]
    public void A_playlist_is_worth_less_than_a_like_and_the_same_however_many_hold_it()
    {
        var inOne = Evaluate(new ReturnInterestEvidence([], PlaylistMemberships: 1));
        var inNine = Evaluate(new ReturnInterestEvidence([], PlaylistMemberships: 9));
        var liked = Evaluate(new ReturnInterestEvidence([], PersonalReaction.Like));

        Assert.Equal(inOne.Score, inNine.Score);
        Assert.True(inOne.IsPositive);
        Assert.True(liked.Tier > inOne.Tier);
        Assert.Contains(ReturnInterestReason.InAPlaylist, inOne.Reasons);
    }

    [Fact]
    public void Only_a_deliberate_departure_after_a_very_short_visit_costs_anything()
    {
        var baseline = Score(Session(600_000));
        var skipped = Score(
            Session(600_000),
            Session(8_000, departure: PlaybackDeparture.AnotherVideo));
        var failed = Score(
            Session(600_000),
            Session(8_000, departure: PlaybackDeparture.TechnicalFailure));
        var closed = Score(
            Session(600_000),
            Session(8_000, departure: PlaybackDeparture.Closed));
        var unaccounted = Score(Session(600_000), Session(8_000));

        Assert.True(skipped < baseline);
        Assert.Equal(baseline, failed);
        Assert.Equal(baseline, closed);
        Assert.Equal(baseline, unaccounted);

        // An under-minute visit that was not a departure to another Video is simply neutral.
        Assert.Equal(baseline, Score(Session(600_000), Session(40_000)));
    }

    [Fact]
    public void A_short_skip_cannot_erase_what_was_established_or_what_was_said()
    {
        var established = Session(600_000, run: 600_000);
        var skips = Enumerable
            .Repeat(Session(2_000, departure: PlaybackDeparture.AnotherVideo), 20)
            .ToArray();

        // Twenty glances pull it down and leave it standing: half of what the real session
        // established survives however many there were.
        var behaviour = Evaluate(new ReturnInterestEvidence([established, .. skips]));
        Assert.True(behaviour.Score > 0);
        Assert.True(behaviour.Score >= ReturnInterestPolicy.SessionContribution(established) / 2);
        Assert.True(behaviour.Score < Score(established));

        // After a Love, the skip costs nothing at all, and the tier is untouched.
        var loved = Evaluate(new ReturnInterestEvidence(skips, PersonalReaction.Love));
        var lovedWithoutSkips = Evaluate(new ReturnInterestEvidence([], PersonalReaction.Love));
        Assert.Equal(ReturnInterestPolicy.LovedTier, loved.Tier);
        Assert.Equal(lovedWithoutSkips.Score, loved.Score);
    }

    [Fact]
    public void A_short_video_whose_play_count_qualified_is_still_judged_on_absolute_watching()
    {
        // Twelve seconds of a ninety-second clip is a qualifying Viewing Session for Play Count.
        // It is not a minute, so it says nothing here — Play Count keeps its own threshold and is
        // deliberately not read as this one.
        Assert.Equal(0, Score(Session(12_000, run: 12_000)));
        Assert.True(Score(Session(60_000, run: 60_000)) > 0);
    }

    [Fact]
    public void What_was_watched_in_this_visit_moves_down_without_leaving()
    {
        var evidence = new ReturnInterestEvidence([Session(600_000)]);
        var justWatched = evidence with { WatchedInThisVisit = true };

        var settled = Evaluate(evidence);
        var recent = Evaluate(justWatched);

        Assert.True(recent.Score < settled.Score);
        Assert.False(recent.Excluded);
        Assert.Contains(ReturnInterestReason.JustWatchedInThisVisit, recent.Reasons);
        // The adjustment belongs to the visit, not to the Video: the next visit reads the same
        // evidence without it.
        Assert.Equal(settled.Score, Evaluate(justWatched with { WatchedInThisVisit = false }).Score);
    }

    [Fact]
    public void Legacy_watching_establishes_that_it_happened_and_invents_no_sessions()
    {
        var legacy = Evaluate(new ReturnInterestEvidence([], HasPriorWatching: true));

        Assert.False(legacy.Excluded);
        Assert.Equal(0, legacy.Score);
        Assert.Contains(ReturnInterestReason.WatchedBefore, legacy.Reasons);
        Assert.DoesNotContain(ReturnInterestReason.WatchedAtLength, legacy.Reasons);
        Assert.DoesNotContain(ReturnInterestReason.WatchedRepeatedly, legacy.Reasons);
    }

    [Fact]
    public void Reasons_state_the_evidence_that_actually_exists()
    {
        var reasons = Evaluate(new ReturnInterestEvidence(
            [Session(600_000, run: 600_000), Session(300_000, run: 10_000)],
            PersonalReaction.Love,
            Favourite: true,
            PlaylistMemberships: 2)).Reasons;

        Assert.Equal(
            [
                ReturnInterestReason.Loved,
                ReturnInterestReason.InAPlaylist,
                ReturnInterestReason.Favourite,
                ReturnInterestReason.WatchedRepeatedly,
                ReturnInterestReason.WatchedWithoutInterruption,
            ],
            reasons);

        // Nothing is claimed that did not happen: one uninterrupted-free session says so.
        Assert.DoesNotContain(
            ReturnInterestReason.WatchedWithoutInterruption,
            Evaluate(new ReturnInterestEvidence([Session(600_000, run: 10_000)])).Reasons);
    }

    /// <summary>
    /// The whole ordering, in one place: what a page of For you to watch again would put first.
    /// </summary>
    [Fact]
    public void The_ordering_puts_explicit_tiers_first_and_evidence_within_them()
    {
        var candidates = new (string Name, ReturnInterestEvidence Evidence)[]
        {
            ("loved but barely watched", new([], PersonalReaction.Love)),
            ("liked and watched a lot", new(
                Enumerable.Repeat(Session(600_000), 4).ToArray(),
                PersonalReaction.Like)),
            ("returned to three times", new(
                [Session(300_000), Session(300_000), Session(300_000)])),
            ("watched once for a minute", new([Session(60_000)])),
            ("skipped after eight seconds", new(
                [Session(8_000, departure: PlaybackDeparture.AnotherVideo)])),
            ("disliked", new(Enumerable.Repeat(Session(600_000), 9).ToArray(), PersonalReaction.Dislike)),
        };

        var ordered = candidates
            .Select(candidate => (candidate.Name, Interest: Evaluate(candidate.Evidence)))
            .Where(candidate => !candidate.Interest.Excluded)
            .OrderByDescending(candidate => candidate.Interest.Tier)
            .ThenByDescending(candidate => candidate.Interest.Score)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
            .Select(candidate => candidate.Name)
            .ToArray();

        Assert.Equal(
            [
                "loved but barely watched",
                "liked and watched a lot",
                "returned to three times",
                "watched once for a minute",
                "skipped after eight seconds",
            ],
            ordered);
    }

    private static ViewingSessionEvidence Session(
        long active,
        long run = 0,
        PlaybackDeparture departure = PlaybackDeparture.Unknown) =>
        new(active, run, departure);

    private static ReturnInterest Evaluate(ReturnInterestEvidence evidence) =>
        ReturnInterestPolicy.Evaluate(evidence);

    private static double Score(params ViewingSessionEvidence[] sessions) =>
        ReturnInterestPolicy.Evaluate(new ReturnInterestEvidence(sessions)).Score;
}
