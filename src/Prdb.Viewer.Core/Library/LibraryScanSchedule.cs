namespace Prdb.Viewer.Core.Library;

/// <summary>
/// How long an Active Library Directory goes without a Library Scan before one is due.
///
/// Discovery cannot depend on someone remembering to ask for it: a file added to the mounted
/// library has to become a Video on its own. The period is fixed by the application rather than
/// configured, because a Library Scan reads every directory entry beneath its root and the cost of
/// that is paid by storage this application does not own — a sleeping disk is woken once a period
/// whether or not anything changed. Six hours keeps that cost negligible while leaving discovery
/// well inside the day it was added, and an Administrator who cannot wait has Scan now.
/// </summary>
public static class LibraryScanSchedule
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    /// <summary>
    /// When the next Library Scan falls due, counted from the observation that just happened
    /// rather than from the one before it. A Scan that took an hour therefore does not leave the
    /// next one an hour closer.
    /// </summary>
    public static DateTime NextDueAfter(DateTime observedAt) => observedAt + Interval;
}
