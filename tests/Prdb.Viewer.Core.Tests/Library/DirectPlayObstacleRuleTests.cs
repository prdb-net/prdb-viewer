using Prdb.Viewer.Core.Library;

using Xunit;

namespace Prdb.Viewer.Core.Tests.Library;

/// <summary>
/// What a file with no browser path is actually held back by. "Needs conversion" was true of every
/// one of these and useful about none of them: a Matroska carrying H.264 and AAC wants a remux, an
/// AVI carrying MPEG-4 Part 2 wants a re-encode, and only one of the two says anything about the
/// picture and the sound.
/// </summary>
public sealed class DirectPlayObstacleRuleTests
{
    private static MediaConfiguration Media(string format, string video, string? audio) =>
        new(format, video, audio) { Width = 1920, Height = 1080, FrameRate = 25, BitDepth = 8 };

    private static DirectPlayObstacle Obstacle(string format, string video, string? audio)
    {
        var media = Media(format, video, audio);

        return DirectPlayObstacleRule.For(media, DirectPlayClassificationRule.Classify(media));
    }

    [Theory]
    // Matroska is the case this exists for: every supported browser decodes these streams, and the
    // only thing between the file and playback is a container none of them reads.
    [InlineData("matroska,webm", "h264", "aac")]
    [InlineData("matroska", "hevc", "opus")]
    [InlineData("avi", "h264", "mp3")]
    public void A_container_with_no_browser_path_carrying_streams_browsers_play_is_the_container(
        string format,
        string video,
        string? audio) =>
        Assert.Equal(DirectPlayObstacle.Container, Obstacle(format, video, audio));

    [Theory]
    // Nothing about the container would save these: no supported browser has a decoder for them.
    [InlineData("avi", "mpeg4", null)]
    [InlineData("matroska", "wmv3", "wmav2")]
    [InlineData("asf", "vc1", "wmav2")]
    public void Streams_no_browser_decodes_in_a_container_none_reads_are_both(
        string format,
        string video,
        string? audio) =>
        Assert.Equal(DirectPlayObstacle.ContainerAndCodecs, Obstacle(format, video, audio));

    [Fact]
    public void A_browser_container_carrying_a_legacy_codec_is_the_codecs() =>
        Assert.Equal(
            DirectPlayObstacle.Codecs,
            DirectPlayObstacleRule.For(
                Media("mov,mp4,m4a,3gp,3g2,mj2", "msmpeg4v3", "aac"),
                DirectPlayClassification.Unsupported));

    [Theory]
    [InlineData("matroska,webm", "vp8", "vorbis")]
    [InlineData("mov,mp4,m4a,3gp,3g2,mj2", "h264", "aac")]
    public void A_file_with_a_direct_play_path_has_no_obstacle_to_name(
        string format,
        string video,
        string? audio) =>
        Assert.Equal(DirectPlayObstacle.None, Obstacle(format, video, audio));

    [Fact]
    public void Facts_that_settle_nothing_name_no_obstacle_either() =>
        Assert.Equal(DirectPlayObstacle.Undetermined, Obstacle("mp4", "prores", "pcm_s16le"));

    [Theory]
    [InlineData("matroska,webm", "h264", "aac", "Matroska")]
    [InlineData("matroska,webm", "vp8", "vorbis", "WebM")]
    [InlineData("mov,mp4,m4a,3gp,3g2,mj2", "h264", "aac", "MP4")]
    [InlineData("avi", "mpeg4", null, "AVI")]
    [InlineData("asf", "wmv3", "wmav2", "Windows Media")]
    public void A_container_is_named_the_way_a_person_names_it(
        string format,
        string video,
        string? audio,
        string expected) =>
        Assert.Equal(expected, Media(format, video, audio).ContainerName);

    /// <summary>
    /// The inspector's own word is better than a guess, and this is the one place it may reach a
    /// reader — a container nothing here has a name for still has to be called something.
    /// </summary>
    [Fact]
    public void An_unnamed_container_falls_back_to_what_the_inspector_called_it() =>
        Assert.Equal("nut", Media("nut", "h264", "aac").ContainerName);
}
