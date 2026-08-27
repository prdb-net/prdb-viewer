namespace Prdb.Viewer.Core.Personal;

public enum PlaybackAttemptVerdict
{
    Started,
    VideoNotFound,
    VideoFileUnavailable,
}

public enum PlaybackReportVerdict
{
    Accepted,
    Duplicate,
    NotFound,
    AttemptEnded,
    InvalidReport,
}

public enum PersonalStateMutationVerdict
{
    Updated,
    VideoNotFound,
    InvalidRating,
}
