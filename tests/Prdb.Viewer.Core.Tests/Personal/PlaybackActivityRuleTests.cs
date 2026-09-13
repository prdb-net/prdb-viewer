using Prdb.Viewer.Core.Personal;

using Xunit;

namespace Prdb.Viewer.Core.Tests.Personal;

public sealed class PlaybackActivityRuleTests
{
    [Theory]
    [InlineData(600_000, 60_000)]
    [InlineData(100_000, 10_000)]
    [InlineData(30_000, 10_000)]
    public void Qualification_threshold_is_bounded_by_ten_and_sixty_seconds(
        long duration,
        long expected) =>
        Assert.Equal(expected, PlaybackActivityRule.QualificationThresholdMilliseconds(duration));

    [Fact]
    public void Short_video_qualifies_only_at_its_confirmed_natural_end()
    {
        Assert.False(PlaybackActivityRule.Qualifies(9_000, 8_999, naturalEndConfirmed: false));
        Assert.True(PlaybackActivityRule.Qualifies(9_000, 1, naturalEndConfirmed: true));
    }

    [Fact]
    public void Completion_requires_active_watching_in_the_capped_end_zone()
    {
        Assert.Equal(3_300_000, PlaybackActivityRule.CompletionEndZoneStartMilliseconds(3_600_000));
        Assert.False(PlaybackActivityRule.EstablishesCompletion(3_600_000, 3_500_000, 0, false));
        Assert.True(PlaybackActivityRule.EstablishesCompletion(3_600_000, 3_300_000, 1, false));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(10_000, true)]
    [InlineData(89_999, true)]
    [InlineData(90_000, false)]
    public void Resume_position_stays_outside_the_completion_end_zone(
        long progress,
        bool expected) =>
        Assert.Equal(expected, PlaybackActivityRule.IsMeaningfulResumePosition(100_000, progress));

    /// <summary>
    /// A run is broken by a file switch, by a gap in wall-clock time, and by a position that does
    /// not follow on from where the run had reached. Nothing else breaks one, and none of the
    /// three is negative evidence: the Viewing Session keeps its total across all of them.
    /// </summary>
    [Fact]
    public void An_uninterrupted_run_continues_only_where_this_report_joins_the_last_one()
    {
        var contiguous = TimeSpan.FromSeconds(1);

        // Ten more seconds, arriving at 20 s, having started where the run reached 10 s.
        Assert.True(PlaybackActivityRule.ContinuesUninterruptedRun(
            sameVideoFile: true,
            runEndPositionMilliseconds: 10_000,
            positionMilliseconds: 20_000,
            activeWatchingMilliseconds: 10_000,
            sinceLastEvidence: contiguous));

        // A seek: the same ten seconds were watched, but somewhere else entirely.
        Assert.False(PlaybackActivityRule.ContinuesUninterruptedRun(
            sameVideoFile: true,
            runEndPositionMilliseconds: 10_000,
            positionMilliseconds: 620_000,
            activeWatchingMilliseconds: 10_000,
            sinceLastEvidence: contiguous));

        // A pause or a buffer: the evidence resumes where it left off, a minute later.
        Assert.False(PlaybackActivityRule.ContinuesUninterruptedRun(
            sameVideoFile: true,
            runEndPositionMilliseconds: 10_000,
            positionMilliseconds: 20_000,
            activeWatchingMilliseconds: 10_000,
            sinceLastEvidence: TimeSpan.FromMinutes(1)));

        // A file switch, and the very first report of a session, each start a run rather than
        // continue one.
        Assert.False(PlaybackActivityRule.ContinuesUninterruptedRun(
            sameVideoFile: false,
            runEndPositionMilliseconds: 10_000,
            positionMilliseconds: 20_000,
            activeWatchingMilliseconds: 10_000,
            sinceLastEvidence: contiguous));
        Assert.False(PlaybackActivityRule.ContinuesUninterruptedRun(
            sameVideoFile: true,
            runEndPositionMilliseconds: null,
            positionMilliseconds: 10_000,
            activeWatchingMilliseconds: 10_000,
            sinceLastEvidence: null));
    }

    /// <summary>
    /// Reports arrive on a timer and carry rounded milliseconds, so a run has to survive the
    /// rounding it is measured with.
    /// </summary>
    [Fact]
    public void Rounding_between_two_reports_does_not_break_a_run()
    {
        Assert.True(PlaybackActivityRule.ContinuesUninterruptedRun(
            sameVideoFile: true,
            runEndPositionMilliseconds: 10_000,
            positionMilliseconds: 20_137,
            activeWatchingMilliseconds: 10_000,
            sinceLastEvidence: TimeSpan.FromMilliseconds(137)));
    }
}
