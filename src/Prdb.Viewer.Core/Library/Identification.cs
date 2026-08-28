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
