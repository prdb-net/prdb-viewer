namespace Prdb.Viewer.Core.Personal;

/// <summary>
/// What a User said about a Video, explicitly and in their own name. Four values, because each of
/// them says something a recommendation can act on: a scale of five asked for a judgement of
/// quality and left the middle of it meaning nothing in particular.
/// </summary>
/// <remarks>
/// The order is the order of increasing interest, so a screen that offers the four in a row offers
/// them in a sequence a reader can predict. It is not a score: <see cref="Shrug"/> is a statement
/// of indifference rather than a middling one, and having said nothing at all is a different fact
/// again, which is why a Personal Reaction is optional rather than defaulted.
/// </remarks>
public enum PersonalReaction
{
    Dislike,

    /// <summary>Seen, and no opinion. Distinct from having said nothing.</summary>
    Shrug,

    Like,

    Love,
}

/// <summary>
/// What a Personal Reaction means to everything that reads one. It is written here rather than in
/// the recommender because a reaction is a fact about the Video, and the Library, the API and the
/// screens read the same three questions of it that the recommender does.
/// </summary>
public static class PersonalReactionRule
{
    /// <summary>
    /// Whether this reaction keeps the Video out of every recommendation section. Dislike is a
    /// hard exclusion whatever else the evidence says, and it is lifted the moment the reaction is
    /// changed or cleared. It hides nothing from the Library, from search, or from the Account's
    /// own lists.
    /// </summary>
    public static bool ExcludesFromRecommendations(PersonalReaction? reaction) =>
        reaction == PersonalReaction.Dislike;

    /// <summary>
    /// Whether this reaction is positive evidence in its own right, without any watching history
    /// behind it. Shrug is not: it contributes nothing and suppresses nothing, so behaviour can
    /// still speak for a Video its User had no opinion about.
    /// </summary>
    public static bool IsPositive(PersonalReaction? reaction) =>
        reaction is PersonalReaction.Like or PersonalReaction.Love;

    /// <summary>
    /// The order the four take when the Library is sorted by what this Account said: Love first,
    /// then Like, then Shrug, then the Videos with no reaction, and Dislike last. Absence ranks
    /// above Dislike because it is not a statement, and putting an explicit rejection above the
    /// unspoken would read as a mistake.
    /// </summary>
    /// <remarks>
    /// The Library answers this order in SQL and cannot call this, so the same ranking is written
    /// once more as an expression there. The discovery tests hold the two to the same answers.
    /// </remarks>
    public static int PresentationRank(PersonalReaction? reaction) => reaction switch
    {
        PersonalReaction.Love => 4,
        PersonalReaction.Like => 3,
        PersonalReaction.Shrug => 2,
        null => 1,
        _ => 0,
    };
}
