using Prdb.Viewer.Core.Library;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>How an index of Actors is ordered.</summary>
public enum ActorSortOrder
{
    /// <summary>By name, which is how somebody looking for a particular person reads a list.</summary>
    Name,

    /// <summary>By how many Videos the Actor has here, which is how somebody browsing reads one.</summary>
    MostHere,
}

public sealed record ActorIndexRequest
{
    public string? Query { get; init; }

    public ActorSortOrder Sort { get; init; } = ActorSortOrder.Name;

    public int Skip { get; init; }

    public int Take { get; init; } = 60;
}

/// <summary>One Actor as an index lists them.</summary>
public sealed record ActorSummary(
    string ActorId,
    string Name,
    // An address in this installation, never at prdb. Null unless a picture is actually held.
    string? PortraitUrl,
    string? GenderLabel,
    int VideoCount,
    ActorProfileState ProfileState);

public sealed record ActorIndexPage(
    IReadOnlyList<ActorSummary> Actors,
    int TotalMatches,
    bool HasMore,
    /// <summary>
    /// How many Actors are still waiting for a profile. An index of names without pictures is a
    /// plausible grid of grey rectangles, and this is what lets it say why.
    /// </summary>
    int AwaitingProfiles);

public sealed record ActorImageView(string Url, string? KindLabel);

public sealed record ActorLinkView(string Url, string? SiteLabel);

/// <summary>
/// One Actor, everything prdb says about them, and the Videos this library holds them in.
/// </summary>
/// <remarks>
/// Every field of the profile may be absent, and the whole profile may be: an Actor exists here
/// because a credit resolved to them, which happens before prdb is asked anything (ADR 0020). The
/// Videos are the part that is always there.
/// </remarks>
public sealed record ActorDetail(
    string ActorId,
    string Name,
    ActorProfileState ProfileState,
    DateTimeOffset? ProfileFetchedAt,
    string? GenderLabel,
    DateTimeOffset? Birthday,
    string? BirthdayPrecisionLabel,
    DateTimeOffset? Deathday,
    string? Birthplace,
    string? HaircolourLabel,
    string? EyecolourLabel,
    string? BreastTypeLabel,
    int? HeightCentimetres,
    string? BraSizeLabel,
    int? WaistCentimetres,
    int? HipCentimetres,
    string? NationalityLabel,
    string? EthnicityLabel,
    int? CareerStart,
    int? CareerEnd,
    string? Tattoos,
    string? Piercings,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<ActorLinkView> Links,
    IReadOnlyList<string> Bios,
    IReadOnlyList<ActorImageView> Images,
    /// <summary>How many pictures prdb offers, which may be more than are held.</summary>
    int OfferedImageCount,
    IReadOnlyList<VideoSummary> Videos,
    int TotalVideos,
    /// <summary>
    /// The names this library credits the Actor under. They are what the Library's Actor facet is
    /// keyed by, so this is what a link from here into the Library has to carry — prdb may lead
    /// with a name no Video here uses.
    /// </summary>
    IReadOnlyList<string> CreditedNames);
