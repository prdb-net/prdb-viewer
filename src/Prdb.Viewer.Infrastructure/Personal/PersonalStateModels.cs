using Prdb.Viewer.Core.Personal;

namespace Prdb.Viewer.Infrastructure.Personal;

public sealed record PersonalVideoStateSummary(
    long? PlaybackProgressMilliseconds,
    long AccumulatedWatchDurationMilliseconds,
    int PlayCount,
    bool HasViewingCompletion,
    PersonalPlayState PlayState,
    bool ContinueWatching,
    bool Favourite,
    bool WatchLater,
    int? PersonalRating);

public sealed record PlaybackAttemptResult(
    PlaybackAttemptVerdict Verdict,
    Guid? PlaybackAttemptId,
    long? ResumePositionMilliseconds);

public sealed record PlaybackReportResult(
    PlaybackReportVerdict Verdict,
    PersonalVideoStateSummary? PersonalState);

public sealed record PersonalStateMutationResult(
    PersonalStateMutationVerdict Verdict,
    PersonalVideoStateSummary? PersonalState);
