namespace Prdb.Viewer.Infrastructure.Persistence;

public sealed class VideoRow
{
    public Guid Id { get; set; }

    public DateTime DiscoveryDate { get; set; }

    /// <summary>
    /// The Video this identity was merged into. A merged Video remains as a historical alias with
    /// its own Discovery Date and decision history; it no longer carries Video Files.
    /// </summary>
    public Guid? SurvivingVideoId { get; set; }

    public DateTime? MergedAt { get; set; }

    /// <summary>
    /// Increments whenever this Video's claims, candidates, or associations change. An
    /// Administrator's confirmation is bound to the version it was shown, so a decision taken
    /// against superseded knowledge is refused rather than silently applied.
    /// </summary>
    public int CaseVersion { get; set; }

    public VideoMetadataRow? Metadata { get; set; }

    public ICollection<VideoFileRow> VideoFiles { get; set; } = [];

    public ICollection<IdentificationClaimRow> IdentificationClaims { get; set; } = [];

    public ICollection<IdentificationCandidateRow> IdentificationCandidates { get; set; } = [];

    public ICollection<PersonalVideoStateRow> PersonalStates { get; set; } = [];

    public ICollection<PlaybackAttemptRow> PlaybackAttempts { get; set; } = [];
}
