using Prdb.Viewer.Core.Library;

using Xunit;

namespace Prdb.Viewer.Core.Tests.Library;

public sealed class PreviewFrameRuleTests
{
    [Fact]
    public void A_video_without_an_established_duration_has_no_sample_point() =>
        Assert.Null(PreviewFrameRule.SampleSeconds(0));

    [Fact]
    public void The_sample_point_is_a_quarter_into_the_runtime() =>
        Assert.Equal(30, PreviewFrameRule.SampleSeconds(120_000));

    [Fact]
    public void A_very_short_video_still_yields_a_frame_inside_its_runtime()
    {
        Assert.Equal(0, PreviewFrameRule.SampleSeconds(800));
        Assert.Equal(0.375, PreviewFrameRule.SampleSeconds(1_500));
    }
}
