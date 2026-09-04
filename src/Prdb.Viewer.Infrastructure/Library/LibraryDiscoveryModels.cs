using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Core.Personal;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// What one Account asked the Library for. Every facet is a list: values inside one facet combine
/// with OR, and the facets combine with AND.
/// </summary>
public sealed record LibraryDiscoveryRequest
{
    public string? Query { get; init; }

    public LibrarySortOrder Sort { get; init; } = LibrarySortOrder.Newest;

    public IReadOnlyList<string> Sites { get; init; } = [];

    public IReadOnlyList<string> Actors { get; init; } = [];

    /// <summary>Whether Work Identification is Established or still Unknown.</summary>
    public IReadOnlyList<IdentificationResolution> WorkIdentification { get; init; } = [];

    public IReadOnlyList<IdentificationReviewStatus> ReviewStatus { get; init; } = [];

    /// <summary>
    /// An explicit Client Video Playability filter. It overrides the Account's preference for this
    /// view, which is why it is a filter rather than another way of setting the preference.
    /// </summary>
    public IReadOnlyList<ClientVideoPlayability> Playability { get; init; } = [];

    public IReadOnlyList<VideoAvailability> Availability { get; init; } = [];

    /// <summary>
    /// The Video Quality bands wanted. It matches the Video's own Quality — the best its Available
    /// occurrences hold — rather than the one this client would be shown, per ADR 0018.
    /// </summary>
    public IReadOnlyList<VideoQualityBand> Quality { get; init; } = [];

    public IReadOnlyList<PersonalPlayState> PlayState { get; init; } = [];

    /// <summary>
    /// The Personal Shelves to narrow to. A shelf is a personal reference rather than a discovery,
    /// so a request that names one is answered without the admission rule: what the User put there
    /// is shown whether or not this client can play it, and unavailable Videos stay in until the
    /// Video is Removed.
    /// </summary>
    public IReadOnlyList<PersonalShelf> Shelf { get; init; } = [];

    /// <summary>True selects Videos with no Established Site, which is its own facet value.</summary>
    public bool UnknownSite { get; init; }

    public int Skip { get; init; }

    public int Take { get; init; } = LibraryPaging.DefaultPageSize;
}

public static class LibraryPaging
{
    public const int DefaultPageSize = 60;

    public const int MaximumPageSize = 120;

    public static int Clamp(int take) =>
        take <= 0 ? DefaultPageSize : Math.Min(take, MaximumPageSize);
}

/// <summary>
/// One page of the Library, plus what the current rules kept out of it. The hidden counts exist so
/// the view can offer the control that reveals those matches instead of silently losing them.
/// </summary>
public sealed record LibraryPage(
    IReadOnlyList<VideoSummary> Videos,
    int TotalMatches,
    int HiddenNotReadyForDirectPlay,
    int HiddenUnavailable,
    bool HasMore,
    bool IncludesNotReadyForDirectPlay);

/// <summary>
/// One Video answered on its own. <paramref name="SupersededVideoId"/> is the identity that was
/// asked for when it differs from the one answered, so the view can say that the link led to the
/// Video this one was merged into rather than silently showing something else.
/// </summary>
public sealed record VideoDetail(VideoSummary Video, Guid? SupersededVideoId);

/// <summary>
/// One media configuration this Account's client has not answered for yet, with everything Media
/// Capabilities needs to answer it. The Video Files carrying it are not named: the question is
/// about the configuration, not about anyone's library.
/// </summary>
public sealed record UnassessedPlaybackProfile(
    string ProfileKey,
    string? VideoContentType,
    string? AudioContentType,
    string? BasicContentType,
    int? Width,
    int? Height,
    double? FrameRate,
    long? Bitrate,
    int? AudioChannels,
    int? AudioSampleRate,
    long? AudioBitrate);

/// <summary>What one client concluded about one media configuration.</summary>
public sealed record ClientPlaybackAssessmentReport(
    string ProfileKey,
    ClientPlaybackAssessmentVerdict Verdict,
    bool? Smooth,
    bool? PowerEfficient,
    string Method);

/// <summary>
/// Looking for one value among a facet's own, which narrows the list on offer rather than the
/// Library. It is answered here rather than in the browser because only the most populated values
/// are sent: a browser filtering what it was given could not find the Site that never arrived, and
/// would say so by silently offering nothing.
/// </summary>
public sealed record LibraryFacetSearch
{
    public string? Sites { get; init; }

    public string? Actors { get; init; }
}

/// <summary>
/// The Established values an Account can currently filter or navigate by.
/// <paramref name="MoreSites"/> and <paramref name="MoreActors"/> say that the facet holds values
/// this answer left out, so the view can offer the search that reaches them instead of claiming to
/// list the lot.
/// </summary>
public sealed record LibraryFacets(
    IReadOnlyList<LibraryFacetValue> Sites,
    IReadOnlyList<LibraryFacetValue> Actors,
    IReadOnlyList<LibraryQualityFacetValue> Quality,
    bool MoreSites,
    bool MoreActors);

public sealed record LibraryFacetValue(string Value, int Count);

/// <summary>
/// One Video Quality band the library actually holds, with how much of it there is. Bands nobody
/// has are not offered: a facet is a way to narrow the Library, and a value that narrows it to
/// nothing is a dead end rather than a choice.
/// </summary>
public sealed record LibraryQualityFacetValue(VideoQualityBand Value, int Count);
