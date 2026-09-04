using Prdb.Viewer.Core.Library;

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

    /// <summary>
    /// The label ordinary browsing shows. Established knowledge supplies it when there is any;
    /// otherwise it is the file name of the oldest active occurrence. Projected per ADR 0013.
    /// </summary>
    public string DisplayLabel { get; set; } = string.Empty;

    /// <summary>
    /// Every searchable fact of this Video, normalised for comparison: the display label, the
    /// Established title, the Established Site, the Established Actors, and the current file names.
    /// A Pending Identification Candidate never reaches it.
    /// </summary>
    public string SearchText { get; set; } = string.Empty;

    /// <summary>
    /// The most playable Direct-Play Classification among this Video's Available Video Files. It
    /// is installation-wide and promises nothing about a client; discovery uses it only to skip
    /// Videos no client could play before asking the per-client question.
    /// </summary>
    public DirectPlayClassification BestClassification { get; set; } =
        DirectPlayClassification.Unsupported;

    /// <summary>
    /// The highest Video File Quality among this Video's Available occurrences. It is
    /// installation-wide, which is what lets the Library filter and order by it in one indexed
    /// question rather than deciding it per Account and client. See ADR 0018.
    /// </summary>
    public VideoQualityBand Quality { get; set; } = VideoQualityBand.Unknown;

    public VideoAvailability Availability { get; set; } = VideoAvailability.Unavailable;

    public bool HasEstablishedWork { get; set; }

    /// <summary>The Established Site as a facet value, or null while Site Recognition is Unknown.</summary>
    public string? EstablishedSite { get; set; }

    /// <summary>
    /// The same Site normalised for comparison, so looking for a Site among the facet values
    /// answers the way the Library's own search does — ignoring case, diacritics and ordinary
    /// punctuation. An Actor already had one; the Site had only the aggregate search text, which
    /// mixes every searchable fact and so cannot say that a term matched the Site.
    /// </summary>
    public string? NormalizedSite { get; set; }

    public bool ReviewNeeded { get; set; }

    /// <summary>
    /// When the projection was last computed. A row that has never been projected, or was
    /// projected before a rule changed, is found and rebuilt through this rather than guessed at.
    /// </summary>
    public DateTime? ProjectedAt { get; set; }

    public ICollection<VideoActorRow> ProjectedActors { get; set; } = [];

    public VideoMetadataRow? Metadata { get; set; }

    public ICollection<VideoFileRow> VideoFiles { get; set; } = [];

    public ICollection<IdentificationClaimRow> IdentificationClaims { get; set; } = [];

    public ICollection<IdentificationCandidateRow> IdentificationCandidates { get; set; } = [];

    public ICollection<PersonalVideoStateRow> PersonalStates { get; set; } = [];

    public ICollection<PlaybackAttemptRow> PlaybackAttempts { get; set; } = [];
}
