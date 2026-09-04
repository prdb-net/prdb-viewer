namespace Prdb.Viewer.Infrastructure.Persistence;

using Prdb.Viewer.Core.Library;

/// <summary>
/// One Actor this library's Videos credit, and the last known prdb facts about them.
/// </summary>
/// <remarks>
/// The identity is prdb's and is never minted here; the facts are an Actor Profile, which is a
/// regenerable projection rather than Shared Library Knowledge (ADR 0020). It is therefore absent
/// from the Backup Archive, and it never establishes, corrects or disputes an Identification
/// Claim.
///
/// prdb sends each of its enumerations with a label of its own, so the labels are what is kept:
/// a translation table here would be a second vocabulary to maintain and a way to print
/// "Unknown (7)" the day prdb learns an eighth value.
/// </remarks>
public sealed class ActorRow
{
    public Guid Id { get; set; }

    /// <summary>The Actor as prdb identifies them. The identity of this row.</summary>
    public required string PrdbActorId { get; set; }

    public required string Name { get; set; }

    /// <summary>The same name normalised, which is what the index is searched by.</summary>
    public required string NormalizedName { get; set; }

    public ActorProfileState ProfileState { get; set; } = ActorProfileState.Pending;

    /// <summary>When prdb last answered about this Actor, whatever it said.</summary>
    public DateTime? FetchedAt { get; set; }

    public string? GenderLabel { get; set; }

    public DateTime? Birthday { get; set; }

    /// <summary>How exactly the birthday is known, in prdb's own words.</summary>
    public string? BirthdayPrecisionLabel { get; set; }

    public DateTime? Deathday { get; set; }

    public string? Birthplace { get; set; }

    public string? HaircolourLabel { get; set; }

    public string? EyecolourLabel { get; set; }

    public string? BreastTypeLabel { get; set; }

    /// <summary>Height in centimetres, as prdb records it.</summary>
    public int? HeightCentimetres { get; set; }

    public string? BraSizeLabel { get; set; }

    public int? WaistCentimetres { get; set; }

    public int? HipCentimetres { get; set; }

    public string? NationalityLabel { get; set; }

    public string? EthnicityLabel { get; set; }

    public int? CareerStart { get; set; }

    public int? CareerEnd { get; set; }

    public string? Tattoos { get; set; }

    public string? Piercings { get; set; }

    /// <summary>
    /// The Actor's links away from here, retained as prdb sends them. They are never followed by
    /// this installation and are the one thing on an Actor's page that leaves it.
    /// </summary>
    public string? LinksJson { get; set; }

    /// <summary>The bios prdb holds, in the order it holds them.</summary>
    public string? BiosJson { get; set; }

    /// <summary>
    /// How many pictures prdb offers for this Actor, which may be more than are held. The page
    /// says so rather than presenting a capped gallery as the whole of one.
    /// </summary>
    public int OfferedImageCount { get; set; }

    public ICollection<ActorAliasRow> Aliases { get; set; } = [];

    public ICollection<ActorImageRow> Images { get; set; } = [];
}

/// <summary>
/// One name an Actor is also credited under. It is a row of its own rather than a field in the
/// profile document because it is searched: somebody looking for an Actor types the name they
/// know, which is not always the one prdb leads with.
/// </summary>
public sealed class ActorAliasRow
{
    public Guid Id { get; set; }

    public Guid ActorId { get; set; }

    public ActorRow Actor { get; set; } = null!;

    public required string Name { get; set; }

    public required string NormalizedName { get; set; }

    /// <summary>The Site this alias belongs to, where prdb names one.</summary>
    public string? PrdbSiteId { get; set; }
}

/// <summary>
/// One picture of an Actor: where prdb offers it, and where this installation holds it.
/// </summary>
public sealed class ActorImageRow
{
    public Guid Id { get; set; }

    public Guid ActorId { get; set; }

    public ActorRow Actor { get; set; } = null!;

    public required string PrdbImageId { get; set; }

    /// <summary>Where prdb offers the picture. The browser is never sent here.</summary>
    public required string SourceUrl { get; set; }

    /// <summary>Thumbnail, Poster or Face, as <see cref="ActorImageKind"/> numbers them.</summary>
    public int Kind { get; set; }

    public string? KindLabel { get; set; }

    /// <summary>Where this picture stands among the Actor's, in prdb's own order.</summary>
    public int Position { get; set; }

    public ActorImageState State { get; set; } = ActorImageState.Pending;

    /// <summary>
    /// The random, non-enumerable identifier the retained picture is served by, so that a stored
    /// path or database key never appears in an address.
    /// </summary>
    public Guid? PublicImageId { get; set; }

    public string? RelativePath { get; set; }

    /// <summary>What the retained bytes actually are, so they are served as themselves.</summary>
    public string? ContentType { get; set; }

    public DateTime? RetainedAt { get; set; }
}
