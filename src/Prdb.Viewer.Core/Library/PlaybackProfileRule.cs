using System.Globalization;

namespace Prdb.Viewer.Core.Library;

/// <summary>
/// Turns an inspected media configuration into the exact question a browser can answer about it.
///
/// A client qualifies a Video File by asking about a codec string, so the string has to describe
/// the file that was actually inspected. Where inspection established profile, level and bit depth,
/// that produces a full RFC 6381 type a client can measure with Media Capabilities. Where it did
/// not, the rule offers the coarser type `canPlayType` accepts instead of inventing the digits —
/// an answer to a made-up question would be worse than an uncertain one.
/// </summary>
public static class PlaybackProfileRule
{
    /// <summary>The container MIME type, or null where no browser container applies.</summary>
    public static string? ContainerMimeType(MediaConfiguration media)
    {
        if (media.IsMp4)
        {
            return "video/mp4";
        }

        return media.IsConformingWebm ? "video/webm" : null;
    }

    /// <summary>
    /// The full RFC 6381 video type, such as `video/mp4; codecs="avc1.640028"`, or null when the
    /// inspected facts do not determine every part of the codec string.
    /// </summary>
    public static string? PreciseVideoContentType(MediaConfiguration media)
    {
        var container = ContainerMimeType(media);
        var codec = PreciseVideoCodec(media);

        return container is null || codec is null ? null : $"{container}; codecs=\"{codec}\"";
    }

    /// <summary>The full RFC 6381 audio type, or null when the file has no audio to qualify.</summary>
    public static string? PreciseAudioContentType(MediaConfiguration media)
    {
        var container = ContainerMimeType(media);
        var codec = PreciseAudioCodec(media);

        return container is null || codec is null
            ? null
            : $"{container.Replace("video/", "audio/")}; codecs=\"{codec}\"";
    }

    /// <summary>
    /// The coarser type `canPlayType` understands. WebM's codec names are complete on their own,
    /// so that type stays specific; the MP4 family falls back to the container alone, because a
    /// half-written `avc1` is not a question any browser answers.
    /// </summary>
    public static string? BasicContentType(MediaConfiguration media)
    {
        if (media.IsConformingWebm)
        {
            var codecs = new[] { media.VideoCodec, media.AudioCodec }
                .Where(codec => codec is "vp8" or "vp9" or "vorbis" or "opus")
                .ToArray();

            return codecs.Length == 0
                ? "video/webm"
                : $"video/webm; codecs=\"{string.Join(", ", codecs)}\"";
        }

        return ContainerMimeType(media);
    }

    public static string? PreciseVideoCodec(MediaConfiguration media) =>
        media.VideoCodec switch
        {
            "vp8" => "vp8",
            "h264" => Avc1(media),
            "hevc" => Hvc1(media),
            "vp9" => Vp09(media),
            "av1" => Av01(media),
            _ => null,
        };

    public static string? PreciseAudioCodec(MediaConfiguration media) =>
        media.AudioCodec switch
        {
            null => null,
            "aac" => "mp4a.40.2",
            "opus" => "opus",
            "vorbis" => "vorbis",
            "flac" => "flac",
            _ => null,
        };

    /// <summary>
    /// The resolution band a client's answer is really about. Two files that differ by a few lines
    /// are one question; standard definition and 4K are not.
    /// </summary>
    public static string ResolutionClass(int? width, int? height)
    {
        var lines = height ?? 0;

        return lines switch
        {
            <= 0 => "size-unknown",
            <= 480 => "sd",
            <= 720 => "hd",
            <= 1080 => "fullhd",
            <= 1440 => "qhd",
            _ => "uhd",
        };
    }

    public static string FrameRateClass(double? frameRate) =>
        frameRate switch
        {
            null or <= 0 => "rate-unknown",
            <= 30.5 => "standard",
            <= 60.5 => "high",
            _ => "very-high",
        };

    /// <summary>
    /// `avc1.PPCCLL`: profile, constraint flags, and level as the codec string encodes them. The
    /// constraint byte is the one a conforming Constrained Baseline stream sets and zero
    /// otherwise, which is what the inspected profile name distinguishes.
    /// </summary>
    private static string? Avc1(MediaConfiguration media)
    {
        var profile = media.VideoProfile?.ToLowerInvariant();
        var (profileIdc, constraints) = profile switch
        {
            null => (0, 0),
            _ when profile.Contains("constrained baseline") => (0x42, 0xE0),
            _ when profile.Contains("baseline") => (0x42, 0x00),
            _ when profile.Contains("high 10") => (0x6E, 0x00),
            _ when profile.Contains("high") => (0x64, 0x00),
            _ when profile.Contains("main") => (0x4D, 0x00),
            _ => (0, 0),
        };

        return profileIdc == 0 || media.VideoLevel is not > 0
            ? null
            : $"avc1.{profileIdc:X2}{constraints:X2}{media.VideoLevel.Value:X2}".ToLowerInvariant();
    }

    /// <summary>`hvc1.P.C.LL.B0`, with the general level the inspector reports.</summary>
    private static string? Hvc1(MediaConfiguration media)
    {
        var profile = media.VideoProfile?.ToLowerInvariant();
        var profileIdc = profile switch
        {
            null => 0,
            _ when profile.Contains("main 10") => 2,
            _ when profile.Contains("main") => 1,
            _ => 0,
        };

        return profileIdc == 0 || media.VideoLevel is not > 0
            ? null
            : $"hvc1.{profileIdc}.6.L{media.VideoLevel.Value}.B0";
    }

    /// <summary>`vp09.PP.LL.DD`: profile, level, and bit depth, each two digits.</summary>
    private static string? Vp09(MediaConfiguration media)
    {
        var profile = ProfileNumber(media.VideoProfile);

        return profile is null || media.VideoLevel is not > 0 || media.BitDepth is null
            ? null
            : $"vp09.{profile.Value:D2}.{media.VideoLevel.Value:D2}.{media.BitDepth.Value:D2}";
    }

    /// <summary>`av01.P.LLT.DD`, with the tier a non-professional stream uses.</summary>
    private static string? Av01(MediaConfiguration media)
    {
        var profile = ProfileNumber(media.VideoProfile);

        return profile is null || media.VideoLevel is not > 0 || media.BitDepth is null
            ? null
            : $"av01.{profile.Value}.{media.VideoLevel.Value:D2}M.{media.BitDepth.Value:D2}";
    }

    /// <summary>
    /// The number in a profile name such as `Profile 0`, which is how the inspector reports the
    /// numbered profiles of VP9 and AV1.
    /// </summary>
    private static int? ProfileNumber(string? profile)
    {
        if (profile is null)
        {
            return null;
        }

        var digits = new string(profile.Where(char.IsAsciiDigit).ToArray());

        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}
