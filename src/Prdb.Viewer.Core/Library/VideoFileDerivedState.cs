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
