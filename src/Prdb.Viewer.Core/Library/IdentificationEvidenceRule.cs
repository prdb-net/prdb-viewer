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
    /// Classifies what one Video File's resemblance to another of this installation's own files may
    /// establish about a Video's work identity.
    ///
    /// It is never Conclusive, whatever the distance. A similarity is this installation's own
    /// inference about two pictures; it is not an inspection of the file's content by the catalogue
    /// that holds the work, so it cannot carry what <see cref="RemoteMatchKind.OsHash"/> carries and
    /// cannot establish a claim. Within the near-duplicate band it is Suggestive and may propose a
    /// candidate; beyond it, or with no distance to read at all, it is nothing.
    ///
    /// The duration agreement deliberately does not enter here. It decides whether two Videos may
    /// be <em>associated</em> without review — which names no work — and it decides whether a later
    /// reading is materially stronger than a rejected one. What it must not do is quietly become a
    /// second threshold that suppresses proposals: ADR 0021 sends every similarity that does not
    /// clear the narrow path to an Administrator rather than deciding it, and what the running
    /// times did is said to that Administrator instead.
    /// </summary>
    public static IdentificationEvidenceClass ClassifyNeighbourWorkIdentification(int? distance) =>
        distance is { } bits && bits <= PerceptualNeighbourhoodRule.NeighbourhoodDistance
            ? IdentificationEvidenceClass.Suggestive
            : IdentificationEvidenceClass.Insufficient;

    /// <summary>
    /// Whether a neighbour-derived proposal an Administrator has already rejected may come back.
    ///
    /// Every rung suppresses a rejected proposal until materially stronger evidence appears, and
    /// for this one that phrase has to be decided rather than left to a comparison of evidence
    /// classes: every reading of a similarity is Suggestive, so a generic comparison would either
    /// suppress the rung for ever or let the same rejected proposal return the moment anything
    /// about it changed. Stronger here means what the measurement says it means — the two files
    /// moved closer together, or their running times stopped disagreeing. A pair that drifted
    /// further apart is weaker evidence and stays rejected.
    /// </summary>
    public static bool NeighbourEvidenceSupersedesRejection(
        int rejectedDistance,
        bool rejectedDurationsAgreed,
        int distance,
        bool durationsAgree) =>
        distance < rejectedDistance || (durationsAgree && !rejectedDurationsAgreed);

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
