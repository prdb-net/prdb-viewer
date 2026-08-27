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
