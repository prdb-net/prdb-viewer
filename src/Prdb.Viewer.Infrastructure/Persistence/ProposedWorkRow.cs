using Prdb.Viewer.Core.Library;

namespace Prdb.Viewer.Infrastructure.Persistence;

/// <summary>
/// What prdb says about a work an Identification Candidate proposes, retained so that a review
/// case can be read long after the lane that made it ran, and without a live prdb request.
/// </summary>
/// <remarks>
/// It is keyed by the remote work rather than by the candidate, because the facts belong to the
/// work: several Videos may propose the same one, and a candidate that is rejected or superseded
/// does not take the retained picture with it. Like the Site Directory, it is regenerable rather
/// than authoritative, so a Backup Archive leaves it out.
/// </remarks>
public sealed class ProposedWorkRow
{
    public Guid Id { get; set; }

    public required string PrdbVideoId { get; set; }

    public required string Title { get; set; }

    public string? SiteTitle { get; set; }

    public string? SiteUrl { get; set; }

    public string? ActorsJson { get; set; }

    /// <summary>Where prdb offers the picture. The browser is never sent here.</summary>
    public string? ArtworkUrl { get; set; }

    public DateTime? ReleaseDate { get; set; }

    public long? DurationMilliseconds { get; set; }

    public DateTime FetchedAt { get; set; }

    public ProposedWorkArtworkState ArtworkState { get; set; }

    /// <summary>
    /// The random, non-enumerable identifier the retained picture is served by, so that a stored
    /// path or database key never appears in an address.
    /// </summary>
    public Guid? PublicArtworkId { get; set; }

    public string? ArtworkRelativePath { get; set; }

    /// <summary>What the retained bytes actually are, so they are served as themselves.</summary>
    public string? ArtworkContentType { get; set; }

    public DateTime? ArtworkRetainedAt { get; set; }
}
