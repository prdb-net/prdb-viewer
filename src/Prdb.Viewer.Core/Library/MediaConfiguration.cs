using System.Globalization;

namespace Prdb.Viewer.Core.Library;

/// <summary>
/// The inspected media configuration of one Video File, in the detail the direct-play contract
/// needs: the exact container, codecs, profile, bit depth, dimensions, frame rate, and bitrate. A
/// client can only qualify a file it is told this much about, so nothing here is optional for
/// convenience — a fact that inspection could not establish is null and stays null.
/// </summary>
public sealed record MediaConfiguration(
    string ContainerFormat,
    string VideoCodec,
    string? AudioCodec)
{
    /// <summary>The codec profile as the inspector names it, such as `High` or `Main 10`.</summary>
    public string? VideoProfile { get; init; }

    /// <summary>The codec level times ten, as `avc1` and `hvc1` encode it: 4.0 is 40.</summary>
    public int? VideoLevel { get; init; }

    public int? BitDepth { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public double? FrameRate { get; init; }

    public long? VideoBitrate { get; init; }

    public int? AudioChannels { get; init; }

    public int? AudioSampleRate { get; init; }

    public long? AudioBitrate { get; init; }

    /// <summary>The Video File Quality of this configuration.</summary>
    public VideoQualityBand QualityBand => VideoQualityRule.For(Width, Height);

    public IReadOnlyList<string> Formats =>
        ContainerFormat.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Whether the container is the MP4 family. `ffprobe` reports one name for the whole family,
    /// so this is what the family looks like rather than a guess from the file name.
    /// </summary>
    public bool IsMp4 =>
        Formats.Any(format => format is "mov" or "mp4" or "m4a" or "3gp" or "3g2" or "mj2");

    /// <summary>
    /// Whether the container is Matroska or WebM. The two share one inspected name, so which of
    /// them a file is has to be decided from the codecs it carries — WebM admits only VP8, VP9 and
    /// AV1 video with Vorbis or Opus audio.
    /// </summary>
    public bool IsMatroskaFamily => Formats.Contains("matroska") || Formats.Contains("webm");

    public bool IsConformingWebm =>
        IsMatroskaFamily &&
        VideoCodec is "vp8" or "vp9" or "av1" &&
        AudioCodec is null or "vorbis" or "opus";

    /// <summary>
    /// A stable, human-readable name for everything a client's decision depends on. Two Video
    /// Files that share it are the same question for a client, so one answer covers both and a
    /// library of thousands of files asks a handful of questions.
    /// </summary>
    public string ProfileKey
    {
        get
        {
            var parts = new List<string>
            {
                PlaybackProfileRule.ContainerMimeType(this) ?? "unknown",
                VideoCodec,
                VideoProfile?.ToLowerInvariant().Replace(' ', '-') ?? "profile-unknown",
                VideoLevel?.ToString(CultureInfo.InvariantCulture) ?? "level-unknown",
                $"{BitDepth?.ToString(CultureInfo.InvariantCulture) ?? "depth-unknown"}bit",
                PlaybackProfileRule.ResolutionClass(Width, Height),
                PlaybackProfileRule.FrameRateClass(FrameRate),
                AudioCodec ?? "no-audio",
                AudioChannels is null ? "channels-unknown" : $"{AudioChannels}ch",
            };

            return string.Join('|', parts);
        }
    }
}
