using Prdb.Viewer.Core.Library;

using Xunit;

namespace Prdb.Viewer.Core.Tests.Library;

public sealed class IdentificationEvidenceRuleTests
{
    [Theory]
    [InlineData(RemoteMatchKind.OsHash, RemoteMatchConfidence.Exact)]
    [InlineData(RemoteMatchKind.PerceptualHash, RemoteMatchConfidence.Strong)]
    public void A_definitive_match_on_the_inspected_content_is_conclusive(
        RemoteMatchKind matchedBy,
        RemoteMatchConfidence confidence) =>
        Assert.Equal(
            IdentificationEvidenceClass.Conclusive,
            IdentificationEvidenceRule.ClassifyWorkIdentification(
                matchedBy,
                confidence,
                hasSingleTarget: true,
                candidateCount: 0));

    [Theory]
    [InlineData(RemoteMatchKind.Filename, RemoteMatchConfidence.Probable)]
    [InlineData(RemoteMatchKind.ReleaseName, RemoteMatchConfidence.Partial)]
    [InlineData(RemoteMatchKind.Site, RemoteMatchConfidence.Strong)]
    [InlineData(RemoteMatchKind.OsHash, RemoteMatchConfidence.Partial)]
    public void A_name_derived_or_weak_match_is_only_suggestive(
        RemoteMatchKind matchedBy,
        RemoteMatchConfidence confidence) =>
        Assert.Equal(
            IdentificationEvidenceClass.Suggestive,
            IdentificationEvidenceRule.ClassifyWorkIdentification(
                matchedBy,
                confidence,
                hasSingleTarget: true,
                candidateCount: 0));

    [Fact]
    public void An_ambiguous_result_is_suggestive_even_when_it_matched_on_content() =>
        Assert.Equal(
            IdentificationEvidenceClass.Suggestive,
            IdentificationEvidenceRule.ClassifyWorkIdentification(
                RemoteMatchKind.PerceptualHash,
                RemoteMatchConfidence.Ambiguous,
                hasSingleTarget: false,
                candidateCount: 3));

    [Theory]
    [InlineData(null, RemoteMatchConfidence.Exact, true, 0)]
    [InlineData(RemoteMatchKind.OsHash, RemoteMatchConfidence.None, false, 0)]
    [InlineData(RemoteMatchKind.Filename, RemoteMatchConfidence.Probable, false, 0)]
    public void An_absent_failed_or_targetless_result_is_insufficient(
        RemoteMatchKind? matchedBy,
        RemoteMatchConfidence confidence,
        bool hasSingleTarget,
        int candidateCount) =>
        Assert.Equal(
            IdentificationEvidenceClass.Insufficient,
            IdentificationEvidenceRule.ClassifyWorkIdentification(
                matchedBy,
                confidence,
                hasSingleTarget,
                candidateCount));

    [Fact]
    public void A_unique_site_attribution_is_conclusive_and_its_absence_is_insufficient()
    {
        Assert.Equal(
            IdentificationEvidenceClass.Conclusive,
            IdentificationEvidenceRule.ClassifySiteRecognition(hasSite: true));
        Assert.Equal(
            IdentificationEvidenceClass.Insufficient,
            IdentificationEvidenceRule.ClassifySiteRecognition(hasSite: false));
    }

    [Fact]
    public void Only_a_conclusive_result_may_establish_an_unknown_claim_by_itself()
    {
        Assert.True(IdentificationEvidenceRule.EstablishesAutomatically(
            IdentificationEvidenceClass.Conclusive,
            IdentificationResolution.Unknown));
        Assert.False(IdentificationEvidenceRule.EstablishesAutomatically(
            IdentificationEvidenceClass.Suggestive,
            IdentificationResolution.Unknown));
        Assert.False(IdentificationEvidenceRule.EstablishesAutomatically(
            IdentificationEvidenceClass.Conclusive,
            IdentificationResolution.Established));
    }

    [Theory]
    [InlineData(IdentificationDecisionAction.ReplaceClaim, true)]
    [InlineData(IdentificationDecisionAction.RevokeClaim, true)]
    [InlineData(IdentificationDecisionAction.SplitVideo, true)]
    [InlineData(IdentificationDecisionAction.AcceptCandidate, false)]
    [InlineData(IdentificationDecisionAction.AssignDirectly, false)]
    [InlineData(IdentificationDecisionAction.RejectCandidate, false)]
    public void The_less_local_decisions_require_a_note(
        IdentificationDecisionAction action,
        bool required) =>
        Assert.Equal(required, IdentificationEvidenceRule.RequiresDecisionNote(action));

    /// <summary>
    /// A similarity is this installation's own inference about two pictures, not an inspection of
    /// the content by the catalogue that holds the work. However close two files are, it proposes.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(6)]
    public void A_perceptual_neighbour_is_never_more_than_suggestive(int distance) =>
        Assert.Equal(
            IdentificationEvidenceClass.Suggestive,
            IdentificationEvidenceRule.ClassifyNeighbourWorkIdentification(distance));

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(64)]
    [InlineData(null)]
    public void A_file_beyond_the_band_or_with_no_distance_proposes_nothing(int? distance) =>
        Assert.Equal(
            IdentificationEvidenceClass.Insufficient,
            IdentificationEvidenceRule.ClassifyNeighbourWorkIdentification(distance));

    /// <summary>
    /// Stronger means what the measurement says it means. A generic comparison of evidence classes
    /// could not decide this: every reading of a similarity is Suggestive.
    /// </summary>
    [Theory]
    [InlineData(6, false, 4, false)]
    [InlineData(6, false, 6, true)]
    [InlineData(4, false, 2, true)]
    public void A_closer_reading_or_agreeing_running_times_overturn_a_rejection(
        int rejectedDistance,
        bool rejectedDurationsAgreed,
        int distance,
        bool durationsAgree) =>
        Assert.True(IdentificationEvidenceRule.NeighbourEvidenceSupersedesRejection(
            rejectedDistance,
            rejectedDurationsAgreed,
            distance,
            durationsAgree));

    [Theory]
    [InlineData(4, false, 4, false)]
    [InlineData(4, false, 6, false)]
    [InlineData(2, true, 4, true)]
    [InlineData(2, true, 2, false)]
    public void A_reading_that_is_no_closer_leaves_a_rejection_standing(
        int rejectedDistance,
        bool rejectedDurationsAgreed,
        int distance,
        bool durationsAgree) =>
        Assert.False(IdentificationEvidenceRule.NeighbourEvidenceSupersedesRejection(
            rejectedDistance,
            rejectedDurationsAgreed,
            distance,
            durationsAgree));
}
