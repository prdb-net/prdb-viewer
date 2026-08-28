using Prdb.Viewer.Core.Library;

using Xunit;

namespace Prdb.Viewer.Core.Tests.Library;

public sealed class DirectPlayClassificationRuleTests
{
    private static MediaConfiguration Media(
        string format,
        string video,
        string? audio,
        int width = 1920,
        int height = 1080,
        double? frameRate = 25,
        int? bitDepth = 8) =>
        new(format, video, audio)
        {
            Width = width,
            Height = height,
            FrameRate = frameRate,
            BitDepth = bitDepth,
        };

    [Theory]
    [InlineData("matroska,webm", "vp8", "vorbis")]
    [InlineData("webm", "vp8", null)]
    public void A_conforming_webm_with_vp8_at_ordinary_demands_is_the_baseline(
        string format,
        string video,
        string? audio) =>
        Assert.Equal(
            DirectPlayClassification.BaselineCandidate,
            DirectPlayClassificationRule.Classify(Media(format, video, audio)));

    [Theory]
    // Ordinary H.264/AAC in MP4 is the broadest candidate there is, and still not a static
    // promise: Firefox depends on operating-system decoders for it.
    [InlineData("mov,mp4,m4a,3gp,3g2,mj2", "h264", "aac")]
    [InlineData("mp4", "hevc", "aac")]
    [InlineData("mp4", "av1", null)]
    [InlineData("webm", "vp9", "opus")]
    public void A_plausible_but_client_specific_path_is_client_dependent(
        string format,
        string video,
        string? audio) =>
        Assert.Equal(
            DirectPlayClassification.ClientDependent,
            DirectPlayClassificationRule.Classify(Media(format, video, audio)));

    [Theory]
    [InlineData(3840, 2160, 25.0, 8)]
    [InlineData(1920, 1080, 120.0, 8)]
    [InlineData(1920, 1080, 25.0, 10)]
    public void A_baseline_codec_beyond_ordinary_demands_becomes_a_client_question(
        int width,
        int height,
        double frameRate,
        int bitDepth) =>
        Assert.Equal(
            DirectPlayClassification.ClientDependent,
            DirectPlayClassificationRule.Classify(
                Media("webm", "vp8", "vorbis", width, height, frameRate, bitDepth)));

    [Theory]
    [InlineData("asf", "wmv3", "wmav2")]
    [InlineData("mpegts", "h264", "aac")]
    [InlineData("matroska,webm", "h264", "aac")]
    public void A_configuration_with_no_browser_path_is_unsupported(
        string format,
        string video,
        string? audio) =>
        Assert.Equal(
            DirectPlayClassification.Unsupported,
            DirectPlayClassificationRule.Classify(Media(format, video, audio)));

    [Theory]
    [InlineData("mp4", "prores", "pcm_s16le")]
    [InlineData("unknown", "unknown", null)]
    public void A_configuration_the_rules_do_not_settle_stays_undetermined(
        string format,
        string video,
        string? audio) =>
        Assert.Equal(
            DirectPlayClassification.Undetermined,
            DirectPlayClassificationRule.Classify(Media(format, video, audio)));

    [Fact]
    public void An_unestablished_dimension_is_not_assumed_in_the_baselines_favour() =>
        Assert.Equal(
            DirectPlayClassification.ClientDependent,
            DirectPlayClassificationRule.Classify(
                new MediaConfiguration("webm", "vp8", "vorbis")));
}
