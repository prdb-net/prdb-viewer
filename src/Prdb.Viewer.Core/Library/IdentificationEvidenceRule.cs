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
    /// carried by an identified work, or a deterministic unique attribution, is Conclusive; the
    /// remote ladder offers nothing weaker about a site.
    /// </summary>
    public static IdentificationEvidenceClass ClassifySiteRecognition(bool hasSite) =>
        hasSite ? IdentificationEvidenceClass.Conclusive : IdentificationEvidenceClass.Insufficient;

    /// <summary>
    /// Classifies what a Video File's own path may establish about its originating site. Local
    /// evidence that maps uniquely to one known site is Conclusive, because the mapping is
    /// deterministic rather than a similarity. A path that names several sites, or that names one
    /// only through a word short enough to be an ordinary word, stays Suggestive and can therefore
    /// only propose an Identification Candidate.
    /// </summary>
    public static IdentificationEvidenceClass ClassifyLocalSiteRecognition(
        int distinctSites,
        int longestAliasLength) =>
        distinctSites switch
        {
            <= 0 => IdentificationEvidenceClass.Insufficient,
            1 when longestAliasLength >= SiteVocabulary.ConclusiveAliasLength =>
                IdentificationEvidenceClass.Conclusive,
            _ => IdentificationEvidenceClass.Suggestive,
        };

    /// <summary>
    /// Whether an Administrator decision changes Shared Library Knowledge in a way whose
    /// consequences are less local or harder to reverse, and therefore requires a decision note.
    /// </summary>
    public static bool RequiresDecisionNote(IdentificationDecisionAction action) =>
        action is IdentificationDecisionAction.ReplaceClaim or
            IdentificationDecisionAction.RevokeClaim or
            IdentificationDecisionAction.SplitVideo;

    /// <summary>
    /// Whether one Conclusive result may take the place of a current claim without review. Only a
    /// locally derived claim yields this way, and only to the catalogue whose knowledge it was
    /// standing in for: reading a site out of a file's path is a substitute for what prdb knows,
    /// never a rival to it. An Administrative Override never yields, and two remote results that
    /// disagree still require review.
    /// </summary>
    public static bool SupersedesAutomatically(
        IdentificationSource currentSource,
        bool currentIsAdministrativeOverride,
        IdentificationSource source,
        IdentificationEvidenceClass evidence) =>
        currentSource == IdentificationSource.LocalInference &&
        !currentIsAdministrativeOverride &&
        source == IdentificationSource.PrdbIdentification &&
        evidence == IdentificationEvidenceClass.Conclusive;

    /// <summary>
    /// Whether one applicable Conclusive result may establish an Unknown claim without review.
    /// </summary>
    public static bool EstablishesAutomatically(
        IdentificationEvidenceClass evidence,
        IdentificationResolution currentResolution) =>
        evidence == IdentificationEvidenceClass.Conclusive &&
        currentResolution == IdentificationResolution.Unknown;
}
