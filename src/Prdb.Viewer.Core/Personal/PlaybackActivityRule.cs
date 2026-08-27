namespace Prdb.Viewer.Core.Personal;

public static class PlaybackActivityRule
{
    public const long MaximumReportDurationMilliseconds = 15_000;

    public static readonly TimeSpan SessionInactivityTimeout = TimeSpan.FromMinutes(30);

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
