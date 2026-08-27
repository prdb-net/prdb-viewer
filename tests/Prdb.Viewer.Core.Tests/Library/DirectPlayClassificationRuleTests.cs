using Prdb.Viewer.Core.Library;

using Xunit;

namespace Prdb.Viewer.Core.Tests.Library;

public sealed class DirectPlayClassificationRuleTests
{
    [Theory]
    [InlineData("mov,mp4,m4a,3gp,3g2,mj2", "h264", "aac")]
    [InlineData("mp4", "h264", null)]
    public void H264_in_mp4_with_baseline_audio_is_a_baseline_candidate(
        string format,
        string video,
        string? audio) =>
        Assert.Equal(
            DirectPlayClassification.BaselineCandidate,
            DirectPlayClassificationRule.Classify(format, video, audio));

    [Theory]
    [InlineData("webm", "vp9", "opus", DirectPlayClassification.ClientDependent)]
    [InlineData("mp4", "hevc", "aac", DirectPlayClassification.ClientDependent)]
    [InlineData("asf", "wmv3", "wmav2", DirectPlayClassification.Unsupported)]
    [InlineData("matroska", "h264", "aac", DirectPlayClassification.Undetermined)]
    public void Other_properties_remain_explicit(
        string format,
        string video,
        string? audio,
        DirectPlayClassification expected) =>
        Assert.Equal(expected, DirectPlayClassificationRule.Classify(format, video, audio));
}
