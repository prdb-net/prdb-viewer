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
