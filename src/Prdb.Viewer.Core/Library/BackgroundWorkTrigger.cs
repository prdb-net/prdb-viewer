namespace Prdb.Viewer.Core.Library;

/// <summary>
/// Why a bounded Background Work run exists. Administrative status shows it so an Administrator can
/// tell an initial activation, an explicit request, and automatic follow-up work apart.
/// </summary>
public enum BackgroundWorkTrigger
{
    /// <summary>Queued when a Library Directory became active.</summary>
    Activation,

    /// <summary>Requested explicitly by an Administrator.</summary>
    Administrator,

    /// <summary>Queued by an earlier lane whose results this work depends on.</summary>
    FollowUpWork,

    /// <summary>Queued because an Administrator asked an unresolved Work Issue to be reattempted.</summary>
    IssueRetry,

    /// <summary>Queued because the Library Directory reached the time its next Scan was due.</summary>
    Periodic,
}
