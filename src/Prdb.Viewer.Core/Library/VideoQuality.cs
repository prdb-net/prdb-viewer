namespace Prdb.Viewer.Core.Library;

/// <summary>
/// The resolution band a picture would be named by. The values are ordinal — discovery orders by
/// them — and Unknown is the lowest, so a Video whose dimensions inspection never established
/// sorts below one it did rather than above everything.
/// </summary>
public enum VideoQualityBand
{
    Unknown = 0,

    StandardDefinition = 1,

    Hd720 = 2,

    FullHd1080 = 3,

    Qhd1440 = 4,

    Uhd2160 = 5,

    Uhd4320 = 6,
}

/// <summary>
/// Derives Video File Quality from inspected dimensions, and a Video's own Quality from its
/// Available occurrences.
///
/// This is the one place the arithmetic lives. The Library filters and orders by the band it
/// projects, the catalogue answers with the band of each occurrence, and the screens name the band
/// they were given — so what a filter admits and what a card claims cannot disagree.
/// </summary>
public static class VideoQualityRule
{
    /// <summary>
    /// The band one Video File belongs to, or Unknown where inspection established no dimensions.
    /// </summary>
    public static VideoQualityBand For(int? width, int? height)
    {
        if (width is not > 0 || height is not > 0)
        {
            return VideoQualityBand.Unknown;
        }

        return NominalLines(width.Value, height.Value) switch
        {
            >= 3240 => VideoQualityBand.Uhd4320,
            >= 1800 => VideoQualityBand.Uhd2160,
            >= 1260 => VideoQualityBand.Qhd1440,
            >= 900 => VideoQualityBand.FullHd1080,
            >= 600 => VideoQualityBand.Hd720,
            _ => VideoQualityBand.StandardDefinition,
        };
    }

    /// <summary>
    /// A Video's own Quality: the highest band among the occurrences it is given, and Unknown when
    /// none of them establishes one. The caller passes the Available occurrences — a band that
    /// cannot be reached is not a quality the library holds.
    /// </summary>
    public static VideoQualityBand Best(IEnumerable<VideoQualityBand> bands)
    {
        var best = VideoQualityBand.Unknown;

        foreach (var band in bands)
        {
            if (band > best)
            {
                best = band;
            }
        }

        return best;
    }

    /// <summary>
    /// The nominal 16:9 line count of a picture, which is what a resolution name has always meant.
    ///
    /// Neither edge alone names one. A 1920×800 film is a 1080p encode with the bars cut off, and a
    /// 1080×1920 recording is a 1080p recording held upright — reading either by its height alone
    /// understates the first and overstates the second. The larger of the short edge and the long
    /// edge at 16:9 reads both the way the person who made them would.
    ///
    /// The bands then sit between the standards rather than on them, because an encoder cropping to
    /// a multiple of eight or sixteen produces 1072 lines of what everyone involved calls 1080p.
    /// A genuinely ultrawide picture is the case this misreads, and it is the rarer one.
    /// </summary>
    private static int NominalLines(int width, int height)
    {
        var shortEdge = Math.Min(width, height);
        var longEdge = Math.Max(width, height);

        return Math.Max(shortEdge, (int)Math.Round(longEdge * 9.0 / 16.0));
    }
}
