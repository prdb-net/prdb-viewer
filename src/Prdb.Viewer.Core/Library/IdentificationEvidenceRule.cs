namespace Prdb.Viewer.Core.Library;

/// <summary>
/// The rungs of the remote identification ladder, ordered as the prdb Public API reports them.
/// </summary>
public enum RemoteMatchKind
{
    OsHash,
    PerceptualHash,
    Filename,
    ReleaseName,
    Site,
}

/// <summary>
/// How far the remote catalogue trusts one result.
/// </summary>
public enum RemoteMatchConfidence
{
    None,
    Partial,
    Probable,
    Strong,
    Exact,
    Ambiguous,
}

public static class IdentificationEvidenceRule
{
    /// <summary>
    /// Classifies what a remote identification result may establish about a Video's work identity.
    /// Only a definitive match on the inspected file content is Conclusive; every name-derived rung
    /// and every ambiguous answer stays Suggestive so that it can only produce a reviewable
    /// Identification Candidate.
    /// </summary>
    public static IdentificationEvidenceClass ClassifyWorkIdentification(
        RemoteMatchKind? matchedBy,
        RemoteMatchConfidence confidence,
        bool hasSingleTarget,
        int candidateCount)
    {
        if (matchedBy is null ||
            confidence == RemoteMatchConfidence.None ||
            (!hasSingleTarget && candidateCount == 0))
        {
            return IdentificationEvidenceClass.Insufficient;
        }

        var contentMatch = matchedBy is RemoteMatchKind.OsHash or RemoteMatchKind.PerceptualHash;

        return contentMatch &&
               hasSingleTarget &&
               confidence is RemoteMatchConfidence.Exact or RemoteMatchConfidence.Strong
            ? IdentificationEvidenceClass.Conclusive
            : IdentificationEvidenceClass.Suggestive;
    }

    /// <summary>
    /// Classifies what a remote result may establish about a Video's originating site. A site
    /// carried by an identified work, or a deterministic unique attribution, is Conclusive; nothing
    /// else is offered by the remote ladder.
    /// </summary>
    public static IdentificationEvidenceClass ClassifySiteRecognition(bool hasSite) =>
        hasSite ? IdentificationEvidenceClass.Conclusive : IdentificationEvidenceClass.Insufficient;

    /// <summary>
    /// Whether an Administrator decision changes Shared Library Knowledge in a way whose
    /// consequences are less local or harder to reverse, and therefore requires a decision note.
    /// </summary>
    public static bool RequiresDecisionNote(IdentificationDecisionAction action) =>
        action is IdentificationDecisionAction.ReplaceClaim or
            IdentificationDecisionAction.RevokeClaim or
            IdentificationDecisionAction.SplitVideo;

    /// <summary>
    /// Whether one applicable Conclusive result may establish an Unknown claim without review.
    /// </summary>
    public static bool EstablishesAutomatically(
        IdentificationEvidenceClass evidence,
        IdentificationResolution currentResolution) =>
        evidence == IdentificationEvidenceClass.Conclusive &&
        currentResolution == IdentificationResolution.Unknown;
}
