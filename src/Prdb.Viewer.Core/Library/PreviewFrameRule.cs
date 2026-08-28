namespace Prdb.Viewer.Core.Library;

/// <summary>
/// Where a locally generated preview image is sampled from. A single deterministic frame keeps
/// regeneration cheap and reproducible for identified and unidentified Videos alike.
/// </summary>
public static class PreviewFrameRule
{
    public const int PreviewWidth = 480;

    private const double Position = 0.25;
    private const double LeadInSeconds = 1;

    /// <summary>
    /// The sample point in seconds, a quarter into the established duration, kept away from the
    /// very first frame so that a title card or a black lead-in does not become the preview.
    /// Returns null when no usable duration is established.
    /// </summary>
    public static double? SampleSeconds(long durationMilliseconds)
    {
        if (durationMilliseconds <= 0)
        {
            return null;
        }

        var seconds = durationMilliseconds / 1000d;

        return seconds <= LeadInSeconds ? 0 : Math.Min(seconds * Position, seconds - LeadInSeconds);
    }
}
