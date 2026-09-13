using Prdb.Viewer.Core.Personal;

namespace Prdb.Viewer.Infrastructure.Personal;

public sealed record PersonalVideoStateSummary(
    long? PlaybackProgressMilliseconds,
    /// <summary>
    /// The Video File the resume position was observed on. A position belongs to the file it was
    /// confirmed on and moves to another only where the two timelines are known to be equivalent,
    /// so a screen that offers a variant can say which of the two it is rather than pretending.
    /// </summary>
    Guid? ProgressVideoFileId,
    long AccumulatedWatchDurationMilliseconds,
    int PlayCount,
    bool HasViewingCompletion,
    PersonalPlayState PlayState,
    bool ContinueWatching,
    bool Favourite,
    bool WatchLater,
    PersonalReaction? Reaction,
    /// <summary>
    /// When this Account last had confirmed Active Watching of this Video. Null where it never
    /// has, and — on an installation restored from before this was kept — where the Playback
    /// Attempts it would be summarised from are not there to summarise. It is never guessed at
    /// from a Play Count.
    /// </summary>
    DateTimeOffset? LastWatchedAt);

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
