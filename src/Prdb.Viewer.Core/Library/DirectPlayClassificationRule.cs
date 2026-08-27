namespace Prdb.Viewer.Core.Library;

public static class DirectPlayClassificationRule
{
    public static DirectPlayClassification Classify(
        string containerFormat,
        string videoCodec,
        string? audioCodec)
    {
        var formats = containerFormat.Split(',', StringSplitOptions.TrimEntries);
        var mp4 = formats.Any(format => format is "mov" or "mp4" or "m4a" or "3gp" or "3g2" or "mj2");
        var baselineAudio = audioCodec is null or "aac" or "mp3";

        if (mp4 && videoCodec == "h264" && baselineAudio)
        {
            return DirectPlayClassification.BaselineCandidate;
        }

        if ((formats.Contains("webm") && videoCodec is "vp8" or "vp9" or "av1" &&
             audioCodec is null or "opus" or "vorbis") ||
            (mp4 && videoCodec is "hevc" or "av1"))
        {
            return DirectPlayClassification.ClientDependent;
        }

        if (videoCodec is "mpeg1video" or "mpeg2video" or "msmpeg4v3" or "wmv1" or "wmv2" or "wmv3")
        {
            return DirectPlayClassification.Unsupported;
        }

        return DirectPlayClassification.Undetermined;
    }
}
