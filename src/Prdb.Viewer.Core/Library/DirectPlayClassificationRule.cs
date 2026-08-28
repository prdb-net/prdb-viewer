namespace Prdb.Viewer.Core.Library;

/// <summary>
/// The installation-wide conclusion about one Video File, drawn from its inspected facts alone.
/// It is the first of the direct-play contract's three levels and deliberately the weakest: it
/// says what is true of the file, never what a particular browser will do with it.
/// </summary>
public static class DirectPlayClassificationRule
{
    /// <summary>
    /// Dimensions and frame rates a conservative cross-client baseline may assume. Beyond them a
    /// file makes ordinary stream demands no longer, and its playability becomes a client question
    /// even where its codecs are the baseline ones.
    /// </summary>
    private const int BaselineMaximumHeight = 1080;

    private const int BaselineMaximumWidth = 1920;

    private const double BaselineMaximumFrameRate = 60.5;

    private static readonly string[] LegacyVideoCodecs =
    [
        "mpeg1video",
        "mpeg2video",
        "msmpeg4v1",
        "msmpeg4v2",
        "msmpeg4v3",
        "wmv1",
        "wmv2",
        "wmv3",
        "vc1",
        "rv10",
        "rv20",
        "rv30",
        "rv40",
        "svq1",
        "svq3",
        "flv1",
    ];

    /// <summary>
    /// Classifies one inspected Video File.
    ///
    /// Baseline Candidate is the narrowest defensible static expectation across the supported
    /// browsers: a conforming WebM carrying VP8 with ordinary Vorbis audio or none, at ordinary
    /// dimensions and frame rate. Everything else with a plausible direct-file path — ordinary
    /// H.264/AAC in MP4 included — is Client-Dependent, because its support depends on the exact
    /// configuration, operating-system decoders, and device. A configuration with no viable
    /// original-file path among the supported client families is Unsupported, and facts that do not
    /// settle the question leave it Undetermined.
    /// </summary>
    public static DirectPlayClassification Classify(MediaConfiguration media)
    {
        if (LegacyVideoCodecs.Contains(media.VideoCodec))
        {
            return DirectPlayClassification.Unsupported;
        }

        if (media.IsMp4)
        {
            return media.VideoCodec is "h264" or "hevc" or "av1" or "vp9" &&
                   media.AudioCodec is null or "aac" or "mp3" or "flac" or "opus"
                ? DirectPlayClassification.ClientDependent
                : DirectPlayClassification.Undetermined;
        }

        if (media.IsConformingWebm)
        {
            return IsBaseline(media)
                ? DirectPlayClassification.BaselineCandidate
                : DirectPlayClassification.ClientDependent;
        }

        // A container with no browser path carries its codecs no further, whatever they are.
        return media.IsMatroskaFamily ||
               media.Formats.Any(format =>
                   format is "avi" or "asf" or "mpegts" or "mpeg" or "flv" or "rm" or "ogg")
            ? DirectPlayClassification.Unsupported
            : DirectPlayClassification.Undetermined;
    }

    /// <summary>
    /// Whether a conforming WebM also stays inside the conservative baseline: VP8 with Vorbis or
    /// no audio, eight bits per sample, and ordinary dimensions and frame rate. A fact inspection
    /// did not establish is not assumed in the baseline's favour.
    /// </summary>
    private static bool IsBaseline(MediaConfiguration media) =>
        media.VideoCodec == "vp8" &&
        media.AudioCodec is null or "vorbis" &&
        media.BitDepth is null or 8 &&
        media.Width is > 0 and <= BaselineMaximumWidth &&
        media.Height is > 0 and <= BaselineMaximumHeight &&
        media.FrameRate is null or (> 0 and <= BaselineMaximumFrameRate);
}
