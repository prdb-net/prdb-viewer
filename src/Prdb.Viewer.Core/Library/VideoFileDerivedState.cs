namespace Prdb.Viewer.Core.Library;

/// <summary>
/// Whether the hashes that identify a Video File's content to prdb are available for its
/// currently observed content.
/// </summary>
public enum VideoFileHashState
{
    Pending,
    Computed,
    Incomplete,
    Failed,
}

/// <summary>
/// Whether a durable local preview artefact exists for a Video File's currently observed content.
/// </summary>
public enum VideoFilePreviewState
{
    Pending,
    Generated,
    Failed,
}

/// <summary>
/// What a Video's picture is, derived from its Video Files the way the picture itself is. A
/// preview still to be generated and one that could not be produced look identical on a screen —
/// the same neutral placeholder — and are opposite facts: the first is a library still being
/// worked through, the second is as good as this Video's picture is going to get.
/// </summary>
public enum VideoPreviewState
{
    /// <summary>A picture exists, so there is nothing to explain.</summary>
    Present,

    /// <summary>The lane has not reached this Video yet.</summary>
    Pending,

    /// <summary>The attempt ran and no frame could be decoded from the content.</summary>
    NoFrame,

    /// <summary>No Available Video File remains to sample a frame from.</summary>
    Unreachable,
}

/// <summary>
/// Why a Video is shown with a placeholder. A Video is presented with a picture from any of its
/// Video Files, so it has something to explain only when none of them produced one.
/// </summary>
public static class VideoPreviewRule
{
    /// <summary>One Video File as this question sees it.</summary>
    public readonly record struct Occurrence(bool Available, VideoFilePreviewState Preview);

    public static VideoPreviewState For(
        bool hasPicture,
        IReadOnlyCollection<Occurrence> occurrences)
    {
        if (hasPicture)
        {
            return VideoPreviewState.Present;
        }

        var available = occurrences.Where(occurrence => occurrence.Available).ToArray();

        if (available.Length == 0)
        {
            return VideoPreviewState.Unreachable;
        }

        // One occurrence still to be attempted is enough to be waiting on: the Video gets its
        // picture from whichever of them produces one first.
        return available.Any(occurrence => occurrence.Preview != VideoFilePreviewState.Failed)
            ? VideoPreviewState.Pending
            : VideoPreviewState.NoFrame;
    }
}
