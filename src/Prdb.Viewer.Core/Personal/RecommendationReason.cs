namespace Prdb.Viewer.Core.Personal;

/// <summary>
/// The three groups a page of Recommendations is made of. A Video appears in at most one of them
/// on one page.
/// </summary>
public enum RecommendationSection
{
    ForYouToWatchAgain,
    LongUnseen,
    NotYetDiscovered,
}

/// <summary>
/// Why a Video is being offered, in facts the reader's own activity produced.
///
/// It is one vocabulary for all three sections rather than one per policy, because a card says the
/// same kind of thing wherever it appears and a screen should not have to learn two ways of
/// reading the same sentence.
/// </summary>
/// <remarks>
/// Every value is about the Video it is attached to. None of them generalises into a claim about a
/// person: <see cref="SharesAnActorYouWatch"/> says that several Videos this Account showed
/// positive evidence for have an Actor in common with this one, which is a fact about a library,
/// and deliberately not that the Account likes that Actor.
/// </remarks>
public enum RecommendationReason
{
    Loved,
    Liked,
    InAPlaylist,
    Favourite,
    WatchedRepeatedly,
    WatchedAtLength,
    WatchedWithoutInterruption,

    /// <summary>Watched before, at a moment this installation no longer holds.</summary>
    WatchedBefore,

    JustWatchedInThisVisit,

    /// <summary>Watched before, and not for a while. Long unseen's own reason.</summary>
    NotWatchedForAWhile,

    /// <summary>Watched long ago, at a moment nothing recorded.</summary>
    WatchedLongAgo,

    /// <summary>Never watched here by this Account.</summary>
    NeverWatched,

    /// <summary>An Actor this Account keeps explicitly is in it.</summary>
    WithAFavouriteActor,

    /// <summary>An Actor several Videos this Account showed positive evidence for also has.</summary>
    SharesAnActorYouWatch,

    /// <summary>A Site several Videos this Account showed positive evidence for also comes from.</summary>
    FromASiteYouWatch,

    /// <summary>Chosen without reference to any inferred taste, on purpose.</summary>
    SomethingDifferent,
}
