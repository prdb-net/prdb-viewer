using Prdb.Viewer.Core.Library;

using Xunit;

namespace Prdb.Viewer.Core.Tests.Library;

public sealed class ClientPlaybackRuleTests
{
    private static VariantEvidence Baseline(
        ClientPlaybackAssessmentVerdict? assessment = null,
        ObservedPlaybackOutcome? outcome = null) =>
        new(DirectPlayClassification.BaselineCandidate, assessment, Outcome: outcome);

    private static VariantEvidence ClientDependent(
        ClientPlaybackAssessmentVerdict? assessment = null,
        bool? smooth = null,
        bool? powerEfficient = null,
        ObservedPlaybackOutcome? outcome = null,
        long pixels = 0) =>
        new(DirectPlayClassification.ClientDependent,
            assessment,
            smooth,
            powerEfficient,
            outcome,
            pixels);

    [Fact]
    public void A_baseline_candidate_this_client_has_not_rejected_is_ready() =>
        Assert.Equal(
            ClientVideoPlayability.ReadyForDirectPlay,
            ClientVideoPlayabilityRule.For([Baseline()]));

    [Fact]
    public void A_client_dependent_file_is_ready_only_once_this_client_accepts_it()
    {
        Assert.Equal(
            ClientVideoPlayability.CompatibilityUncertain,
            ClientVideoPlayabilityRule.For([ClientDependent()]));
        Assert.Equal(
            ClientVideoPlayability.ReadyForDirectPlay,
            ClientVideoPlayabilityRule.For(
                [ClientDependent(ClientPlaybackAssessmentVerdict.Positive)]));
    }

    [Fact]
    public void A_file_this_client_rejected_is_not_ready_even_where_it_is_the_baseline()
    {
        Assert.Equal(
            ClientVideoPlayability.NotDirectlyPlayable,
            ClientVideoPlayabilityRule.For(
                [Baseline(ClientPlaybackAssessmentVerdict.Negative)]));
        Assert.Equal(
            ClientVideoPlayability.NotDirectlyPlayable,
            ClientVideoPlayabilityRule.For(
                [Baseline(outcome: ObservedPlaybackOutcome.Failed)]));
    }

    [Fact]
    public void A_confirmed_success_outranks_every_prediction() =>
        Assert.Equal(
            ClientVideoPlayability.ReadyForDirectPlay,
            ClientVideoPlayabilityRule.For(
            [
                ClientDependent(
                    ClientPlaybackAssessmentVerdict.Indeterminate,
                    outcome: ObservedPlaybackOutcome.Succeeded),
            ]));

    [Fact]
    public void A_video_is_as_playable_as_its_most_playable_occurrence() =>
        Assert.Equal(
            ClientVideoPlayability.ReadyForDirectPlay,
            ClientVideoPlayabilityRule.For(
            [
                new VariantEvidence(DirectPlayClassification.Unsupported),
                Baseline(),
            ]));

    [Fact]
    public void An_undetermined_file_the_client_has_not_ruled_out_remains_worth_attempting() =>
        Assert.Equal(
            ClientVideoPlayability.CompatibilityUncertain,
            ClientVideoPlayabilityRule.For(
                [new VariantEvidence(DirectPlayClassification.Undetermined)]));

    [Fact]
    public void A_video_with_no_available_occurrence_offers_no_direct_play() =>
        Assert.Equal(
            ClientVideoPlayability.NotDirectlyPlayable,
            ClientVideoPlayabilityRule.For([]));

    [Fact]
    public void One_clients_refusal_does_not_make_a_video_unsupported()
    {
        Assert.False(ClientVideoPlayabilityRule.IsUnsupportedVideo(
        [
            DirectPlayClassification.ClientDependent,
            DirectPlayClassification.Unsupported,
        ]));
        Assert.True(ClientVideoPlayabilityRule.IsUnsupportedVideo(
            [DirectPlayClassification.Unsupported]));
        Assert.False(ClientVideoPlayabilityRule.IsUnsupportedVideo([]));
    }

    [Fact]
    public void Selection_prefers_what_played_here_then_what_this_client_measured()
    {
        var playedHere = ClientDependent(outcome: ObservedPlaybackOutcome.Succeeded);
        var smoothAndEfficient = ClientDependent(
            ClientPlaybackAssessmentVerdict.Positive,
            smooth: true,
            powerEfficient: true);
        var smooth = ClientDependent(ClientPlaybackAssessmentVerdict.Positive, smooth: true);
        var accepted = ClientDependent(ClientPlaybackAssessmentVerdict.Positive);
        var baseline = Baseline();
        var untried = ClientDependent();
        var ruledOut = ClientDependent(ClientPlaybackAssessmentVerdict.Negative);

        Assert.Equal(
            [playedHere, smoothAndEfficient, smooth, accepted, baseline, untried, ruledOut],
            VariantSelectionRule.Order(
                new[] { ruledOut, untried, baseline, accepted, smooth, smoothAndEfficient, playedHere },
                evidence => evidence));
    }

    [Fact]
    public void Within_one_rank_the_highest_quality_leads()
    {
        var large = ClientDependent(ClientPlaybackAssessmentVerdict.Positive, pixels: 8_294_400);
        var small = ClientDependent(ClientPlaybackAssessmentVerdict.Positive, pixels: 921_600);

        Assert.Equal([large, small], VariantSelectionRule.Order([small, large], evidence => evidence));
    }

    [Theory]
    [InlineData(DirectPlayClassification.BaselineCandidate, VariantSelectionReason.BaselineCandidate)]
    [InlineData(DirectPlayClassification.Undetermined, VariantSelectionReason.NotYetAssessed)]
    public void An_untried_variant_says_which_evidence_it_rests_on(
        DirectPlayClassification classification,
        VariantSelectionReason expected) =>
        Assert.Equal(
            expected,
            VariantSelectionRule.ReasonFor(new VariantEvidence(classification)));
}
