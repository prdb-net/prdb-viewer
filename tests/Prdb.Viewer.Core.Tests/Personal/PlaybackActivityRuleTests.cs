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
}
