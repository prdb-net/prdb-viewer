namespace Prdb.Viewer.Core.Library;

public enum BackgroundWorkState
{
    Queued,
    Running,
    Waiting,
    Paused,
    Completed,
    CompletedWithIssues,
    Cancelled,
}

/// <summary>
/// The phases a bounded run reports instead of a percentage when no stable denominator exists.
/// </summary>
public static class BackgroundWorkPhases
{
    public const string Queued = "Waiting to start";
    public const string Traversing = "Traversing directories";
    public const string Reconciling = "Reconciling absences";
    public const string Inspecting = "Inspecting candidates";
    public const string Hashing = "Hashing content";
    public const string GeneratingPreviews = "Generating previews";
    public const string Identifying = "Asking prdb";
    public const string RecognisingSites = "Recognising sites";
    public const string Enriching = "Enriching from prdb";
    public const string Settled = "Settled";
}
