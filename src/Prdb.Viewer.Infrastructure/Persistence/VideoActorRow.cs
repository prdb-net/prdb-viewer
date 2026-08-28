namespace Prdb.Viewer.Infrastructure.Persistence;

/// <summary>
/// One Established Actor of a Video, projected out of retained metadata so it can be faceted and
/// navigated. Derived state per ADR 0013: the metadata document remains the authority, and an
/// Actor named only by a Pending Identification Candidate never appears here.
/// </summary>
public sealed class VideoActorRow
{
    public Guid Id { get; set; }

    public Guid VideoId { get; set; }

    public VideoRow Video { get; set; } = null!;

    /// <summary>The Actor as the metadata spells them, which is what a facet shows.</summary>
    public required string Name { get; set; }

    /// <summary>The same name normalised for comparison and search.</summary>
    public required string NormalizedName { get; set; }
}
