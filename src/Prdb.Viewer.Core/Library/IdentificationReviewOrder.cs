namespace Prdb.Viewer.Core.Library;

/// <summary>
/// What one answer costs the person giving it, by what they have to look at to give it.
///
/// It is a property of the question rather than of the answer: the same decision is cheap when
/// nothing disagrees and expensive when two things do, whatever the reviewer eventually says.
/// </summary>
public enum IdentificationReviewEffort
{
    /// <summary>
    /// Nothing is established in this dimension yet, so the answer adds knowledge rather than
    /// replacing any. It is a comparison a person makes with their eyes — a proposal against the
    /// Video, or two files against each other — and it costs a look and nothing more.
    /// </summary>
    Judgement,

    /// <summary>
    /// Something is established and the proposal differs from it. The answer would take knowledge
    /// away as well as add it, so it costs reading what is already there before deciding.
    /// </summary>
    Displacement,

    /// <summary>
    /// Something already established is in the way — two conclusive results disagree, an
    /// Administrative Override stands, or the remote identity has moved. Answering costs reading
    /// the history that got the Video here.
    /// </summary>
    Conflict,
}

/// <summary>
/// The facts about one Identification Review Group that decide where it sits in the queue.
/// </summary>
public readonly record struct IdentificationReviewGroupFacts(
    int CaseCount,
    IdentificationEvidenceClass Evidence,
    IdentificationReviewReason Reason,
    bool DisplacesAnEstablishedClaim,
    DateTime OldestCaseAt);

/// <summary>
/// The order an identification backlog is worked from the top, and why it is that order.
///
/// A queue of thousands is not read; it is worked through until it is empty or until the reviewer
/// stops. What that makes valuable is not the most interesting case but the next one that removes
/// the most library for the least attention — so the order is the cheapest certainty first, and it
/// is decided here rather than in an <c>OrderBy</c> chain, because it is a claim about how a person
/// should spend their afternoon rather than a detail of a query.
///
/// Three things decide it, in this order, and none of them is blended into a score:
///
/// <list type="number">
/// <item><description><b>How much library it settles.</b> A group is a question asked once and
/// answered once, so a group of four hundred removes four hundred cases for one answer and a group
/// of one removes one. Nothing else in the queue has that leverage.</description></item>
/// <item><description><b>How confident the evidence is.</b> Among groups that settle the same
/// number, the one whose evidence is Conclusive is the one whose answer is not really in
/// doubt.</description></item>
/// <item><description><b>How much work the answer costs.</b> A judgement before a displacement
/// before a conflict: the cases that need a person to weigh something against what the library
/// already holds are the ones a person is actually needed for, and they are still there
/// afterwards.</description></item>
/// </list>
///
/// A backlog worked from the top therefore empties faster than one worked in any other order,
/// because every step takes the answer that settles the most for the least, and what is left at
/// the end is what could not have been settled any more cheaply. Ties are broken by how long the
/// oldest case in the group has waited, so nothing starves.
/// </summary>
public static class IdentificationReviewOrder
{
    /// <summary>
    /// What answering this kind of case costs. A conflict is named by its reason; everything else
    /// is a displacement where the Video already holds a claim in this dimension, and a plain
    /// judgement where it does not.
    /// </summary>
    public static IdentificationReviewEffort EffortOf(
        IdentificationReviewReason reason,
        bool displacesAnEstablishedClaim) =>
        reason switch
        {
            IdentificationReviewReason.ConflictingConclusiveEvidence or
                IdentificationReviewReason.ConflictsWithAdministrativeOverride or
                IdentificationReviewReason.RemoteIdentityChanged =>
                IdentificationReviewEffort.Conflict,
            _ when displacesAnEstablishedClaim => IdentificationReviewEffort.Displacement,
            _ => IdentificationReviewEffort.Judgement,
        };

    public static IdentificationReviewEffort EffortOf(IdentificationReviewGroupFacts facts) =>
        EffortOf(facts.Reason, facts.DisplacesAnEstablishedClaim);

    /// <summary>
    /// Puts groups in the order a backlog should be worked in. The caller keeps whatever it is
    /// carrying; this decides only where each of them goes.
    /// </summary>
    public static IReadOnlyList<TGroup> Sort<TGroup>(
        IEnumerable<TGroup> groups,
        Func<TGroup, IdentificationReviewGroupFacts> factsOf) =>
        groups
            .OrderByDescending(group => factsOf(group).CaseCount)
            .ThenByDescending(group => factsOf(group).Evidence)
            .ThenBy(group => EffortOf(factsOf(group)))
            .ThenBy(group => factsOf(group).OldestCaseAt)
            .ToArray();
}
