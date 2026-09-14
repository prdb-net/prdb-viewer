namespace Prdb.Viewer.Core.Library;

/// <summary>
/// Which part of a Video File's inspected configuration has no path to a browser.
///
/// The Direct-Play Classification says whether a file can be played; this says what about it
/// cannot, which is the only half a person can act on. "Needs conversion" is true of a Matroska
/// carrying H.264 and AAC and of an AVI carrying MPEG-4 Part 2, and the two want entirely
/// different things done about them: the first has streams every browser plays and is held back by
/// its container alone, the second has no codec any of them decodes.
/// </summary>
public enum DirectPlayObstacle
{
    /// <summary>Nothing about the file rules it out, whatever a particular client makes of it.</summary>
    None,

    /// <summary>
    /// The container has no browser path. The codecs it carries are ones the supported browsers
    /// play, so nothing about the picture or the sound is in the way.
    /// </summary>
    Container,

    /// <summary>The codecs have no browser path, whatever container carries them.</summary>
    Codecs,

    /// <summary>Neither the container nor the codecs has one.</summary>
    ContainerAndCodecs,

    /// <summary>Inspection did not establish enough to say which, if either, is in the way.</summary>
    Undetermined,
}

/// <summary>
/// Names the obstacle behind an Unsupported Direct-Play Classification. It reads the same inspected
/// facts <see cref="DirectPlayClassificationRule"/> decided from, so the two cannot drift: this
/// explains that decision rather than making a second one.
/// </summary>
public static class DirectPlayObstacleRule
{
    public static DirectPlayObstacle For(
        MediaConfiguration media,
        DirectPlayClassification classification)
    {
        if (classification == DirectPlayClassification.Undetermined)
        {
            return DirectPlayObstacle.Undetermined;
        }

        if (classification != DirectPlayClassification.Unsupported)
        {
            return DirectPlayObstacle.None;
        }

        return (CarriedByABrowserContainer(media), PlayedBySomeBrowser(media)) switch
        {
            (true, true) => DirectPlayObstacle.None,
            (false, true) => DirectPlayObstacle.Container,
            (true, false) => DirectPlayObstacle.Codecs,
            _ => DirectPlayObstacle.ContainerAndCodecs,
        };
    }

    /// <summary>
    /// Whether the container is one a browser reads at all. Matroska counts only where its codecs
    /// make it a WebM, because that is the only form of it a browser is offered.
    /// </summary>
    private static bool CarriedByABrowserContainer(MediaConfiguration media) =>
        media.IsMp4 || media.IsConformingWebm;

    /// <summary>
    /// Whether both codecs are ones some supported browser decodes where a container carries them.
    /// It is deliberately the permissive question: this is about what would have to change, and a
    /// stream one browser family plays is not the thing to re-encode.
    /// </summary>
    private static bool PlayedBySomeBrowser(MediaConfiguration media) =>
        media.VideoCodec is "h264" or "hevc" or "av1" or "vp8" or "vp9" &&
        media.AudioCodec is null or "aac" or "mp3" or "flac" or "opus" or "vorbis";
}
