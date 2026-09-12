namespace Prdb.Viewer.Core.Library;

public enum IdentificationDimension
{
    WorkIdentification,
    SiteRecognition,
}

public enum IdentificationResolution
{
    Unknown,
    Established,
}

public enum IdentificationReviewStatus
{
    Clear,
    ReviewNeeded,
}

public enum IdentificationEvidenceClass
{
    Insufficient,
    Suggestive,
    Conclusive,
}

public enum IdentificationSource
{
    PrdbIdentification,
    LocalInference,
    AdministratorDecision,
}

public enum IdentificationClaimStatus
{
    Current,
    Superseded,
    Revoked,
}

public enum IdentificationCandidateStatus
{
    Pending,
    Rejected,
    Superseded,
}

public enum IdentificationReviewReason
{
    SuggestiveEvidence,
    ConflictingConclusiveEvidence,
    ConflictsWithAdministrativeOverride,
    RemoteIdentityChanged,

    /// <summary>
    /// Another Video File of this installation looks like this one and its Video carries an
    /// Established Work Identification. What a reviewer is looking at is therefore not a work in
    /// prdb's catalogue beside a file name, but two files of this library beside each other.
    /// </summary>
    PerceptualNeighbour,
}

public enum IdentificationDecisionAction
{
    AcceptCandidate,
    AssignDirectly,
    ReplaceClaim,
    RejectCandidate,
    RevokeClaim,
    SplitVideo,
}

/// <summary>
/// Whether the picture prdb offers for a proposed work is held by this installation. A review
/// screen shows a retained picture and never sends an Administrator's browser to prdb, so a
/// proposal whose picture has not arrived says so rather than leaving a broken frame.
/// </summary>
public enum ProposedWorkArtworkState
{
    None,
    Pending,
    Retained,
    Unavailable,
}
