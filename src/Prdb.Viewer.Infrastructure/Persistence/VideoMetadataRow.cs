namespace Prdb.Viewer.Infrastructure.Persistence;

/// <summary>
/// The last known prdb metadata for a Video's Established Work Identification. It is retained
/// through outages and rejected credentials so that browsing keeps working, and its freshness is
/// reported separately from the claim itself.
/// </summary>
public sealed class VideoMetadataRow
{
    public Guid VideoId { get; set; }

    public VideoRow Video { get; set; } = null!;

    public required string PrdbVideoId { get; set; }

    public required string Title { get; set; }

    public string? SiteId { get; set; }

    public string? SiteTitle { get; set; }

    public string? SiteUrl { get; set; }

    public string? ActorsJson { get; set; }

    public string? ArtworkUrl { get; set; }

    public DateTime? ReleaseDate { get; set; }

    public long? DurationMilliseconds { get; set; }

    public DateTime FetchedAt { get; set; }
}
