using Prdb.Viewer.Infrastructure.Library;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

public sealed class FfprobeOutputParserTests
{
    [Fact]
    public void Parses_secret_free_recorded_media_facts()
    {
        const string output = """
            {
              "streams": [
                { "codec_name": "h264", "codec_type": "video", "width": 1920, "height": 1080 },
                { "codec_name": "aac", "codec_type": "audio" }
              ],
              "format": { "format_name": "mov,mp4,m4a,3gp,3g2,mj2", "duration": "12.345" }
            }
            """;

        var facts = FfprobeOutputParser.Parse(output);

        Assert.NotNull(facts);
        Assert.Equal("h264", facts.Media.VideoCodec);
        Assert.Equal("aac", facts.Media.AudioCodec);
        Assert.Equal(12_345, facts.DurationMilliseconds);
        Assert.Equal(1920, facts.Media.Width);
        Assert.Equal(1080, facts.Media.Height);
    }

    [Theory]
    [InlineData("{\"streams\":[]}")]
    [InlineData("not-json")]
    public void Rejects_output_without_an_audiovisual_stream(string output) =>
        Assert.Null(FfprobeOutputParser.Parse(output));
}
