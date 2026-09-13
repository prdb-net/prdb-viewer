namespace Prdb.Viewer.Infrastructure.Persistence;

/// <summary>
/// One Video this Account and client has watched during the Browsing Visit that is currently open.
///
/// It exists so that a Video watched a few minutes ago can move down a page of recommendations
/// without being excluded, and it holds nothing else: no navigation, no search, no order of
/// arrival beyond the moment itself. The marks of a visit are deleted the first time anything
/// happens more than the visit timeout after the last of them, so what is retained is one visit
/// rather than a history of them.
/// </summary>
public sealed class BrowsingVisitWatchRow
{
    public Guid AccountId { get; set; }

    public AccountRow Account { get; set; } = null!;

    /// <summary>
    /// The client this visit belongs to. Two clients of one Account browse separately, so what was
    /// watched on the television does not rearrange what the phone is offered.
    /// </summary>
    public string ClientContextKey { get; set; } = string.Empty;

    public Guid VideoId { get; set; }

    public VideoRow Video { get; set; } = null!;

    public DateTime WatchedAt { get; set; }
}
