namespace Prdb.Viewer.Core.Library;

/// <summary>
/// What one client made of a Video File's inspected configuration. It qualifies the installation-
/// wide Direct-Play Classification for this browser and device and generalises to no other.
/// </summary>
public enum ClientPlaybackAssessmentVerdict
{
    /// <summary>The client could not answer, so nothing is concluded either way.</summary>
    Indeterminate,

    Positive,

    Negative,
}

/// <summary>What actually happened when an Account played a Video File on one client.</summary>
public enum ObservedPlaybackOutcome
{
    Succeeded,

    Failed,
}

/// <summary>
/// Why a playback attempt ended. The categories exist so a failure is never collapsed into
/// "unsupported browser": only Media says something about the file itself.
/// </summary>
public enum PlaybackFailureCategory
{
    /// <summary>The client could not decode or accept the media it received.</summary>
    Media,

    /// <summary>The occurrence could not be read where the library expected it.</summary>
    Availability,

    /// <summary>The installation's own delivery path answered incorrectly.</summary>
    Delivery,

    /// <summary>The client could not reach the installation at all.</summary>
    Network,
}

/// <summary>Whether a Video can be played directly by one Account on one client.</summary>
public enum ClientVideoPlayability
{
    /// <summary>Offered with the ordinary Play action, without an interrupting warning.</summary>
    ReadyForDirectPlay,

    /// <summary>A plausible attempt remains, offered as Try Direct Play with its explanation.</summary>
    CompatibilityUncertain,

    /// <summary>No normal Play action; only an explicitly chosen Try Anyway remains.</summary>
    NotDirectlyPlayable,
}

/// <summary>
/// Everything known about one Available Video File for one Account on one client: what the
/// installation concluded from its bytes, what this client made of that, and what happened when it
/// was last played here.
/// </summary>
public sealed record VariantEvidence(
    DirectPlayClassification Classification,
    ClientPlaybackAssessmentVerdict? Assessment = null,
    bool? Smooth = null,
    bool? PowerEfficient = null,
    ObservedPlaybackOutcome? Outcome = null,
    long Pixels = 0,
    long Bitrate = 0)
{
    /// <summary>
    /// Whether this client has ruled the file out. A failure observed here and a negative
    /// assessment are the same answer arrived at differently.
    /// </summary>
    public bool RejectedByClient =>
        Outcome == ObservedPlaybackOutcome.Failed ||
        Assessment == ClientPlaybackAssessmentVerdict.Negative;

    public bool ConfirmedByClient => Outcome == ObservedPlaybackOutcome.Succeeded;

    /// <summary>
    /// Whether the file may be offered without a warning: a Baseline Candidate this client has not
    /// rejected, or a Client-Dependent file it positively assessed. A confirmed success outranks
    /// both, because it is the only evidence that is not a prediction.
    /// </summary>
    public bool ReadyForDirectPlay =>
        ConfirmedByClient ||
        (!RejectedByClient &&
         (Classification == DirectPlayClassification.BaselineCandidate ||
          (Classification == DirectPlayClassification.ClientDependent &&
           Assessment == ClientPlaybackAssessmentVerdict.Positive)));

    /// <summary>
    /// Whether an attempt is still plausible: the client has neither accepted nor ruled out a file
    /// that has some direct-play path at all.
    /// </summary>
    public bool WorthAttempting =>
        !RejectedByClient &&
        Classification is DirectPlayClassification.BaselineCandidate or
            DirectPlayClassification.ClientDependent or
            DirectPlayClassification.Undetermined;
}

/// <summary>
/// Why one variant was chosen over the others, so the selection can be inspected rather than
/// merely obeyed.
/// </summary>
public enum VariantSelectionReason
{
    /// <summary>This file already played here for this Account.</summary>
    PreviouslyPlayedHere,

    /// <summary>This client assessed the exact configuration positively and expects it to be smooth.</summary>
    PositivelyAssessedAndSmooth,

    /// <summary>This client assessed the exact configuration positively.</summary>
    PositivelyAssessed,

    /// <summary>The conservative cross-client baseline, which this client has not ruled out.</summary>
    BaselineCandidate,

    /// <summary>No client evidence either way; an attempt is still plausible.</summary>
    NotYetAssessed,

    /// <summary>This client has ruled the file out, so it is offered only if asked for explicitly.</summary>
    RuledOutHere,
}

public static class ClientVideoPlayabilityRule
{
    /// <summary>
    /// Derives a Video's playability for one client from its Available Video Files. A Video with no
    /// Available occurrence is Not Directly Playable here, which says nothing about its
    /// availability — that remains its own, client-independent fact.
    /// </summary>
    public static ClientVideoPlayability For(IEnumerable<VariantEvidence> variants)
    {
        var evidence = variants as IReadOnlyCollection<VariantEvidence> ?? variants.ToArray();

        if (evidence.Any(variant => variant.ReadyForDirectPlay))
        {
            return ClientVideoPlayability.ReadyForDirectPlay;
        }

        return evidence.Any(variant => variant.WorthAttempting)
            ? ClientVideoPlayability.CompatibilityUncertain
            : ClientVideoPlayability.NotDirectlyPlayable;
    }

    /// <summary>
    /// Whether every Available Video File is statically Unsupported, which is the installation-wide
    /// Unsupported Video rather than one client's refusal.
    /// </summary>
    public static bool IsUnsupportedVideo(IEnumerable<DirectPlayClassification> available)
    {
        var classifications = available as IReadOnlyCollection<DirectPlayClassification> ??
                              available.ToArray();

        return classifications.Count > 0 &&
               classifications.All(classification =>
                   classification == DirectPlayClassification.Unsupported);
    }
}

/// <summary>
/// The order in which one deliberate play action tries the Available Video Files: evidence that
/// this client already played a file, then what it positively assessed, preferring smooth and then
/// energy-efficient decoding, then the conservative baseline, then what is merely untried. Within
/// one rank the highest quality that remains inside the client's assessed capability leads.
/// Files this client has ruled out come last and are never tried automatically.
/// </summary>
public static class VariantSelectionRule
{
    public static IEnumerable<T> Order<T>(
        IEnumerable<T> variants,
        Func<T, VariantEvidence> evidenceOf) =>
        variants
            .OrderBy(variant => Rank(evidenceOf(variant)))
            .ThenByDescending(variant => evidenceOf(variant).Pixels)
            .ThenByDescending(variant => evidenceOf(variant).Bitrate);

    public static VariantSelectionReason ReasonFor(VariantEvidence evidence)
    {
        if (evidence.RejectedByClient)
        {
            return VariantSelectionReason.RuledOutHere;
        }

        if (evidence.ConfirmedByClient)
        {
            return VariantSelectionReason.PreviouslyPlayedHere;
        }

        if (evidence.Assessment == ClientPlaybackAssessmentVerdict.Positive)
        {
            return evidence.Smooth == true
                ? VariantSelectionReason.PositivelyAssessedAndSmooth
                : VariantSelectionReason.PositivelyAssessed;
        }

        return evidence.Classification == DirectPlayClassification.BaselineCandidate
            ? VariantSelectionReason.BaselineCandidate
            : VariantSelectionReason.NotYetAssessed;
    }

    private static int Rank(VariantEvidence evidence) =>
        ReasonFor(evidence) switch
        {
            VariantSelectionReason.PreviouslyPlayedHere => 0,
            VariantSelectionReason.PositivelyAssessedAndSmooth =>
                evidence.PowerEfficient == true ? 1 : 2,
            VariantSelectionReason.PositivelyAssessed => 3,
            VariantSelectionReason.BaselineCandidate => 4,
            VariantSelectionReason.NotYetAssessed => 5,
            _ => 6,
        };
}
