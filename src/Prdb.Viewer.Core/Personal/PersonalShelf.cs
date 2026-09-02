namespace Prdb.Viewer.Core.Personal;

/// <summary>
/// One of the three lists a User keeps of their own: Continue Watching, which is derived from
/// viewing, and Favourites and Watch Later, which are maintained by hand. A Personal Shelf is a way
/// of narrowing the Library rather than a library of its own, so everything the Library offers —
/// search, facets, order and paging — holds inside one.
/// </summary>
public enum PersonalShelf
{
    ContinueWatching,

    Favourites,

    WatchLater,
}
