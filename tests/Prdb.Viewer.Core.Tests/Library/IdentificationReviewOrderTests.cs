using Prdb.Viewer.Core.Library;

using Xunit;

namespace Prdb.Viewer.Core.Tests.Library;

/// <summary>
/// The order a backlog is worked from the top. A queue of thousands is not read; it is worked
/// through until it is empty or until the reviewer stops, so what matters is that the next case is
/// the one that removes the most library for the least attention.
/// </summary>
public sealed class IdentificationReviewOrderTests
{
    private static readonly DateTime Noon =
        DateTime.SpecifyKind(new DateTime(2026, 9, 12, 12, 0, 0), DateTimeKind.Utc);

    [Theory]
    [InlineData(IdentificationReviewReason.ConflictingConclusiveEvidence)]
    [InlineData(IdentificationReviewReason.ConflictsWithAdministrativeOverride)]
    [InlineData(IdentificationReviewReason.RemoteIdentityChanged)]
    public void Answering_against_something_established_costs_reading_the_history(
        IdentificationReviewReason reason) =>
        Assert.Equal(
            IdentificationReviewEffort.Conflict,
            IdentificationReviewOrder.EffortOf(reason, displacesAnEstablishedClaim: false));

    [Fact]
    public void A_proposal_over_a_claim_costs_reading_what_is_already_there() =>
        Assert.Equal(
            IdentificationReviewEffort.Displacement,
            IdentificationReviewOrder.EffortOf(
                IdentificationReviewReason.SuggestiveEvidence,
                displacesAnEstablishedClaim: true));

    [Theory]
    [InlineData(IdentificationReviewReason.SuggestiveEvidence)]
    [InlineData(IdentificationReviewReason.PerceptualNeighbour)]
    public void A_proposal_on_an_unknown_video_costs_a_look(IdentificationReviewReason reason) =>
        Assert.Equal(
            IdentificationReviewEffort.Judgement,
            IdentificationReviewOrder.EffortOf(reason, displacesAnEstablishedClaim: false));

    /// <summary>
    /// A group is a question asked once and answered once, so the one that settles four hundred
    /// cases comes before the one that settles nine — whatever else is true about either.
    /// </summary>
    [Fact]
    public void The_group_that_settles_the_most_comes_first()
    {
        var many = Facts(count: 400, IdentificationEvidenceClass.Suggestive);
        var few = Facts(count: 9, IdentificationEvidenceClass.Conclusive);

        Assert.Equal([many, few], IdentificationReviewOrder.Sort([few, many], facts => facts));
    }

    [Fact]
    public void Among_equals_the_more_confident_evidence_comes_first()
    {
        var conclusive = Facts(count: 10, IdentificationEvidenceClass.Conclusive);
        var suggestive = Facts(count: 10, IdentificationEvidenceClass.Suggestive);

        Assert.Equal(
            [conclusive, suggestive],
            IdentificationReviewOrder.Sort([suggestive, conclusive], facts => facts));
    }

    [Fact]
    public void Among_those_the_cheaper_answer_comes_first()
    {
        var judgement = Facts(count: 10, IdentificationEvidenceClass.Suggestive);
        var displacement = judgement with { DisplacesAnEstablishedClaim = true };
        var conflict = judgement with
        {
            Reason = IdentificationReviewReason.ConflictsWithAdministrativeOverride,
        };

        Assert.Equal(
            [judgement, displacement, conflict],
            IdentificationReviewOrder.Sort(
                [conflict, displacement, judgement],
                facts => facts));
    }

    /// <summary>Nothing starves: two groups that are equal in every other way go oldest first.</summary>
    [Fact]
    public void And_then_whichever_has_waited_longest()
    {
        var older = Facts(count: 10, IdentificationEvidenceClass.Suggestive) with
        {
            OldestCaseAt = Noon.AddDays(-3),
        };
        var newer = Facts(count: 10, IdentificationEvidenceClass.Suggestive);

        Assert.Equal([older, newer], IdentificationReviewOrder.Sort([newer, older], facts => facts));
    }

    private static IdentificationReviewGroupFacts Facts(
        int count,
        IdentificationEvidenceClass evidence) =>
        new(count, evidence, IdentificationReviewReason.SuggestiveEvidence, false, Noon);
}
