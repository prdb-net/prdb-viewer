using Prdb.Viewer.Core.Library;

using Xunit;

namespace Prdb.Viewer.Core.Tests.Library;

public sealed class VideoQualityRuleTests
{
    [Theory]
    // The standard itself, an encode cropped to a multiple of sixteen, a film with its bars cut
    // off, and the same picture held upright: all four are what everyone calls 1080p.
    [InlineData(1920, 1080, VideoQualityBand.FullHd1080)]
    [InlineData(1920, 1072, VideoQualityBand.FullHd1080)]
    [InlineData(1920, 800, VideoQualityBand.FullHd1080)]
    [InlineData(1080, 1920, VideoQualityBand.FullHd1080)]
    [InlineData(7680, 4320, VideoQualityBand.Uhd4320)]
    [InlineData(3840, 2160, VideoQualityBand.Uhd2160)]
    [InlineData(2560, 1440, VideoQualityBand.Qhd1440)]
    [InlineData(1280, 720, VideoQualityBand.Hd720)]
    [InlineData(720, 404, VideoQualityBand.StandardDefinition)]
    [InlineData(720, 576, VideoQualityBand.StandardDefinition)]
    public void A_band_names_a_picture_the_way_a_release_is_named(
        int width,
        int height,
        VideoQualityBand expected) =>
        Assert.Equal(expected, VideoQualityRule.For(width, height));

    [Theory]
    [InlineData(null, null)]
    [InlineData(1920, null)]
    [InlineData(0, 1080)]
    public void Dimensions_inspection_did_not_establish_claim_nothing(int? width, int? height) =>
        Assert.Equal(VideoQualityBand.Unknown, VideoQualityRule.For(width, height));

    [Fact]
    public void A_videos_quality_is_the_best_of_the_occurrences_it_is_given() =>
        Assert.Equal(
            VideoQualityBand.Uhd2160,
            VideoQualityRule.Best(
                [VideoQualityBand.Hd720, VideoQualityBand.Uhd2160, VideoQualityBand.FullHd1080]));

    [Fact]
    public void A_video_with_no_occurrence_to_judge_has_no_quality()
    {
        Assert.Equal(VideoQualityBand.Unknown, VideoQualityRule.Best([]));
        Assert.Equal(
            VideoQualityBand.Unknown,
            VideoQualityRule.Best([VideoQualityBand.Unknown, VideoQualityBand.Unknown]));
    }

    /// <summary>
    /// The order the enumeration declares is what discovery sorts by, so it is asserted rather than
    /// assumed: a value inserted in the wrong place would silently reorder the library.
    /// </summary>
    [Fact]
    public void The_bands_are_ordered_from_worst_to_best() =>
        Assert.Equal(
            [
                VideoQualityBand.Unknown,
                VideoQualityBand.StandardDefinition,
                VideoQualityBand.Hd720,
                VideoQualityBand.FullHd1080,
                VideoQualityBand.Qhd1440,
                VideoQualityBand.Uhd2160,
                VideoQualityBand.Uhd4320,
            ],
            Enum.GetValues<VideoQualityBand>().OrderBy(band => band));
}
