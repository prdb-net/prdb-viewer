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
}
