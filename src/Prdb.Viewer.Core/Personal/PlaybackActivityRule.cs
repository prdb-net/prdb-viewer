namespace Prdb.Viewer.Core.Personal;

public static class PlaybackActivityRule
{
    public const long MaximumReportDurationMilliseconds = 15_000;

    public static readonly TimeSpan SessionInactivityTimeout = TimeSpan.FromMinutes(30);

    /// <summary>
    /// How long a Browsing Visit outlives its last confirmed Active Watching. It is the same half
    /// hour a Viewing Session gets, and deliberately so: the two are different things — one is per
    /// Video and made of confirmed time, the other is one Account and client browsing — but a
    /// second number would be a second thing to explain for no reason anybody could name.
    /// </summary>
    public static readonly TimeSpan BrowsingVisitTimeout = TimeSpan.FromMinutes(30);

    /// <summary>
    /// How far a report may fall short of joining the one before it and still be the same
    /// Uninterrupted Run. Reports arrive on a timer and carry rounded milliseconds, so demanding
    /// that they meet exactly would break every run at the first rounding error; two seconds is
    /// short enough that a real pause, a buffer or a seek lands outside it.
    /// </summary>
    public const long RunContinuityToleranceMilliseconds = 2_000;

    /// <summary>
    /// Whether this report continues the Uninterrupted Run the last one was part of, rather than
    /// starting a new one.
    /// </summary>
    /// <remarks>
    /// Three things break a run, and each of them is asked here. A different Video File is a file
    /// switch. A gap in wall-clock time between the last evidence and this report's activity is a
    /// pause, a buffer, or evidence too old to be contiguous. And a position that does not follow
    /// on from where the run had reached is a seek: this report says it watched
    /// <paramref name="activeWatchingMilliseconds"/> and arrived at
    /// <paramref name="positionMilliseconds"/>, so it must have started where the run left off.
    ///
    /// None of the three is negative. The Viewing Session keeps its total across all of them, and
    /// only the run measurement starts again.
    /// </remarks>
    public static bool ContinuesUninterruptedRun(
        bool sameVideoFile,
        long? runEndPositionMilliseconds,
        long positionMilliseconds,
        long activeWatchingMilliseconds,
        TimeSpan? sinceLastEvidence)
    {
        if (!sameVideoFile || runEndPositionMilliseconds is not { } runEnd)
        {
            return false;
        }

        if (sinceLastEvidence is not { } gap ||
            gap > TimeSpan.FromMilliseconds(RunContinuityToleranceMilliseconds))
        {
            return false;
        }

        return Math.Abs(positionMilliseconds - activeWatchingMilliseconds - runEnd) <=
            RunContinuityToleranceMilliseconds;
    }

    public static long QualificationThresholdMilliseconds(long durationMilliseconds)
    {
        if (durationMilliseconds < 10_000)
        {
            return durationMilliseconds;
        }

        return Math.Max(10_000, Math.Min(60_000, durationMilliseconds / 10));
    }

    public static long CompletionEndZoneStartMilliseconds(long durationMilliseconds)
    {
        var endZoneLength = Math.Min(durationMilliseconds / 10, 300_000);
        return Math.Max(0, durationMilliseconds - endZoneLength);
    }

    public static bool Qualifies(
        long durationMilliseconds,
        long activeWatchingMilliseconds,
        bool naturalEndConfirmed) =>
        durationMilliseconds < 10_000
            ? naturalEndConfirmed
            : activeWatchingMilliseconds >= QualificationThresholdMilliseconds(durationMilliseconds);

    public static bool EstablishesCompletion(
        long durationMilliseconds,
        long positionMilliseconds,
        long confirmedActiveWatchingMilliseconds,
        bool naturalEndConfirmed) =>
        confirmedActiveWatchingMilliseconds > 0 &&
        (naturalEndConfirmed ||
         (durationMilliseconds > 0 &&
          positionMilliseconds >= CompletionEndZoneStartMilliseconds(durationMilliseconds)));

    public static bool IsMeaningfulResumePosition(
        long durationMilliseconds,
        long? positionMilliseconds) =>
        positionMilliseconds is > 0 &&
        (durationMilliseconds <= 0 ||
         positionMilliseconds < CompletionEndZoneStartMilliseconds(durationMilliseconds));
}
