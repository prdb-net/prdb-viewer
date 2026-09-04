using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Core.Personal;
using Prdb.Viewer.Infrastructure.Persistence;
using Prdb.Viewer.Infrastructure.Personal;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// Ordinary Discovery: the Library, its search, its facets and its order, answered a page at a
/// time. It filters and sorts in SQL over the projection ADR 0013 maintains, so the cost of a page
/// is the page rather than the library.
///
/// Admission is by Client Video Playability, which is per Account and per client. The rule itself
/// is <see cref="VariantEvidence"/> in the Core; the predicates below are its translation into
/// something the database can answer, and the discovery tests hold the two to the same answers.
/// </summary>
public sealed class LibraryDiscovery(ViewerDbContext database, PlaybackPlanner planner)
{
    /// <summary>
    /// How many values a facet list offers. A facet is a way to narrow the Library, not a
    /// catalogue of every Site and Actor it has ever seen. What the limit leaves out is reached by
    /// searching the facet rather than by raising it, and the answer says when it left anything
    /// out so that the view never claims to be listing them all.
    /// </summary>
    private const int FacetLimit = 50;

    public async Task<LibraryPage> GetAsync(
        Guid accountId,
        string clientContextKey,
        LibraryDiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        var preference = await database.Accounts
            .Where(account => account.Id == accountId)
            .Select(account => account.IncludesNotReadyForDirectPlay)
            .SingleOrDefaultAsync(cancellationToken);
        var matched = Matching(accountId, request);
        var assessed = await AssessedAsync(accountId, clientContextKey, cancellationToken);
        var ready = ReadyHere(accountId, clientContextKey, assessed);
        var attemptable = WorthAttemptingHere(accountId, clientContextKey, assessed);

        // The counts describe what the current rules keep out of the answer, so they are taken
        // from the same match before playability and availability narrow it.
        var admitted = Admit(matched, request, preference, ready, attemptable);
        var total = await admitted.CountAsync(cancellationToken);
        var take = LibraryPaging.Clamp(request.Take);
        var page = await Order(admitted, request, accountId)
            .Skip(Math.Max(0, request.Skip))
            .Take(take + 1)
            .Select(video => video.Id)
            .ToListAsync(cancellationToken);
        var hasMore = page.Count > take;
        var ids = page.Take(take).ToArray();
        var hiddenNotReady = await HiddenNotReadyAsync(
            matched,
            request,
            preference,
            ready,
            total,
            cancellationToken);
        // A Personal Shelf shows what the User put there, so nothing is kept out of one to count.
        var hiddenUnavailable = request.Availability.Count > 0 || request.Shelf.Count > 0
            ? 0
            : await matched.CountAsync(
                video => video.Availability == VideoAvailability.Unavailable,
                cancellationToken);

        return new LibraryPage(
            await LoadAsync(accountId, clientContextKey, ids, cancellationToken),
            total,
            hiddenNotReady,
            hiddenUnavailable,
            hasMore,
            preference);
    }

    /// <summary>
    /// The Established Sites and Actors, and the Video Quality bands, of the Videos the current
    /// narrowing admits, with the counts a facet list shows. Removed Videos are never counted.
    ///
    /// Each facet is counted against everything the request narrows by except itself: a Site's
    /// count says what choosing that Site would leave once the chosen Actors and bands have had
    /// their say, and a second Site widens the set rather than narrowing it, so the Sites already
    /// chosen must not narrow the Sites on offer. Admission by playability is not applied, because
    /// what the library holds is the same fact on every client while what plays here is not.
    /// </summary>
    public async Task<LibraryFacets> GetFacetsAsync(
        Guid accountId,
        LibraryDiscoveryRequest request,
        LibraryFacetSearch? search = null,
        CancellationToken cancellationToken = default)
    {
        var forSites = Matching(accountId, request with { Sites = [], UnknownSite = false });
        var forActors = Matching(accountId, request with { Actors = [] });
        var forQuality = Matching(accountId, request with { Quality = [] });
        // A term is normalised the way the Library's own search normalises one, so looking for a
        // Site ignores case, diacritics and ordinary punctuation there too. The projection stores
        // both names normalised, which is what lets the database answer this rather than the
        // browser.
        var siteTerm = LibrarySearchRule.Normalize(search?.Sites);
        var actorTerm = LibrarySearchRule.Normalize(search?.Actors);

        var sitesQuery = forSites.Where(video => video.EstablishedSite != null);

        if (siteTerm.Length > 0)
        {
            sitesQuery = sitesQuery.Where(video =>
                video.NormalizedSite != null && video.NormalizedSite.Contains(siteTerm));
        }

        // One more than the limit, so the answer can say that it left something out without
        // counting values nobody asked to see.
        var sites = await sitesQuery
            .GroupBy(video => video.EstablishedSite!)
            .Select(group => new { Value = group.Key, Count = group.Count() })
            .OrderByDescending(value => value.Count)
            .ThenBy(value => value.Value)
            .Take(FacetLimit + 1)
            .ToListAsync(cancellationToken);

        var actorsQuery = database.VideoActors
            .AsNoTracking()
            .Where(actor => forActors.Any(video => video.Id == actor.VideoId));

        if (actorTerm.Length > 0)
        {
            actorsQuery = actorsQuery.Where(actor => actor.NormalizedName.Contains(actorTerm));
        }

        var actors = await actorsQuery
            .GroupBy(actor => actor.Name)
            .Select(group => new { Value = group.Key, Count = group.Count() })
            .OrderByDescending(value => value.Count)
            .ThenBy(value => value.Value)
            .Take(FacetLimit + 1)
            .ToListAsync(cancellationToken);

        var quality = await forQuality
            .GroupBy(video => video.Quality)
            .Select(group => new { Value = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return new LibraryFacets(
            sites.Take(FacetLimit)
                .Select(site => new LibraryFacetValue(site.Value, site.Count))
                .ToArray(),
            actors.Take(FacetLimit)
                .Select(actor => new LibraryFacetValue(actor.Value, actor.Count))
                .ToArray(),
            quality
                .OrderByDescending(band => band.Value)
                .Select(band => new LibraryQualityFacetValue(band.Value, band.Count))
                .ToArray(),
            sites.Count > FacetLimit,
            actors.Count > FacetLimit);
    }

    /// <summary>
    /// One Video, addressed directly rather than discovered.
    ///
    /// Direct address is not Ordinary Discovery and does not apply its admission rule: a Video this
    /// client cannot play is still answered, because following a link is the User's own decision to
    /// look at a Video rather than the Library's decision to offer it. What is no longer part of
    /// the active Library is still refused, and a merged identity answers as the Video that
    /// survived it, so a link taken before a merge keeps leading somewhere true.
    /// </summary>
    public async Task<VideoDetail?> GetVideoAsync(
        Guid accountId,
        string clientContextKey,
        Guid videoId,
        CancellationToken cancellationToken = default)
    {
        var addressed = await SurvivorOfAsync(videoId, cancellationToken);

        if (addressed is null)
        {
            return null;
        }

        var loaded = await LoadAsync(accountId, clientContextKey, [addressed.Value], cancellationToken);

        return loaded.Count == 0
            ? null
            : new VideoDetail(loaded[0], addressed.Value == videoId ? null : videoId);
    }

    /// <summary>
    /// The Video that carries this identity today. A merge points the Video it absorbed at its
    /// survivor, and a later merge can move that survivor again, so the chain is followed rather
    /// than the single hop — bounded, because a cycle is a defect rather than a longer chain.
    /// </summary>
    private async Task<Guid?> SurvivorOfAsync(Guid videoId, CancellationToken cancellationToken)
    {
        const int mergeChainLimit = 8;
        var current = videoId;

        for (var hop = 0; hop < mergeChainLimit; hop++)
        {
            var found = await database.Videos
                .AsNoTracking()
                .Where(video => video.Id == current)
                .Select(video => new { video.SurvivingVideoId, video.Availability })
                .SingleOrDefaultAsync(cancellationToken);

            if (found is null || found.Availability == VideoAvailability.Removed)
            {
                return null;
            }

            if (found.SurvivingVideoId is null)
            {
                return current;
            }

            current = found.SurvivingVideoId.Value;
        }

        return null;
    }

    /// <summary>
    /// How many Available matches this client cannot play, which is what the view offers to reveal.
    ///
    /// In the ordinary case it is arithmetic rather than a second question: everything admitted was
    /// Available and ready, so the ones kept out are the Available matches minus the admitted ones.
    /// Asking the database to count them directly would mean deciding playability for every row of
    /// the library twice over. An explicit availability filter breaks that identity, and only then
    /// is the count taken the long way.
    /// </summary>
    private static async Task<int> HiddenNotReadyAsync(
        IQueryable<VideoRow> matched,
        LibraryDiscoveryRequest request,
        bool preference,
        Expression<Func<VideoRow, bool>> ready,
        int total,
        CancellationToken cancellationToken)
    {
        if (request.Playability.Count > 0 || preference || request.Shelf.Count > 0)
        {
            return 0;
        }

        var available = matched.Where(video => video.Availability == VideoAvailability.Available);

        return request.Availability.Count == 0
            ? await available.CountAsync(cancellationToken) - total
            : await available.Where(Not(ready)).CountAsync(cancellationToken);
    }

    /// <summary>
    /// What this client has answered about the library's media configurations.
    ///
    /// It is read once and carried into the query as two small sets rather than asked again for
    /// every Video File. A Profile Key describes a band of configurations rather than a file, so
    /// even a large library holds a few dozen of them, while the question "is this Video ready
    /// here" is asked of every row the page and its count consider.
    /// </summary>
    private async Task<AssessedProfiles> AssessedAsync(
        Guid accountId,
        string clientContextKey,
        CancellationToken cancellationToken)
    {
        var assessments = await database.ClientPlaybackAssessments
            .AsNoTracking()
            .Where(assessment => assessment.AccountId == accountId &&
                                 assessment.ClientContextKey == clientContextKey &&
                                 assessment.Verdict != ClientPlaybackAssessmentVerdict.Indeterminate)
            .Select(assessment => new { assessment.ProfileKey, assessment.Verdict })
            .ToListAsync(cancellationToken);

        return new AssessedProfiles(
            assessments
                .Where(assessment => assessment.Verdict == ClientPlaybackAssessmentVerdict.Positive)
                .Select(assessment => assessment.ProfileKey)
                .ToArray(),
            assessments
                .Where(assessment => assessment.Verdict == ClientPlaybackAssessmentVerdict.Negative)
                .Select(assessment => assessment.ProfileKey)
                .ToArray());
    }

    private sealed record AssessedProfiles(
        IReadOnlyList<string> Positive,
        IReadOnlyList<string> Negative);

    /// <summary>
    /// Whether at least one Available occurrence is ready for direct play here: one this client has
    /// already played, or one it has not ruled out that is either the conservative baseline or a
    /// Client-Dependent file it assessed positively.
    /// </summary>
    private Expression<Func<VideoRow, bool>> ReadyHere(
        Guid accountId,
        string clientContextKey,
        AssessedProfiles assessed)
    {
        var positive = assessed.Positive;
        var negative = assessed.Negative;

        return video => database.VideoFiles.Any(file =>
            file.VideoId == video.Id &&
            file.Availability == VideoFileAvailability.Available &&
            (database.ObservedPlaybackOutcomes.Any(outcome =>
                 outcome.AccountId == accountId &&
                 outcome.ClientContextKey == clientContextKey &&
                 outcome.VideoFileId == file.Id &&
                 outcome.ContentSha256 == file.Sha256 &&
                 outcome.Outcome == ObservedPlaybackOutcome.Succeeded) ||
             ((file.DirectPlayClassification == DirectPlayClassification.BaselineCandidate ||
               (file.DirectPlayClassification == DirectPlayClassification.ClientDependent &&
                positive.Contains(file.ProfileKey))) &&
              !negative.Contains(file.ProfileKey) &&
              !database.ObservedPlaybackOutcomes.Any(outcome =>
                  outcome.AccountId == accountId &&
                  outcome.ClientContextKey == clientContextKey &&
                  outcome.VideoFileId == file.Id &&
                  outcome.ContentSha256 == file.Sha256 &&
                  outcome.Outcome == ObservedPlaybackOutcome.Failed))));
    }

    /// <summary>
    /// Whether an attempt is still plausible: an Available occurrence with some direct-play path
    /// that this client has neither ruled out nor confirmed.
    /// </summary>
    private Expression<Func<VideoRow, bool>> WorthAttemptingHere(
        Guid accountId,
        string clientContextKey,
        AssessedProfiles assessed)
    {
        var negative = assessed.Negative;

        return video => database.VideoFiles.Any(file =>
            file.VideoId == video.Id &&
            file.Availability == VideoFileAvailability.Available &&
            file.DirectPlayClassification != DirectPlayClassification.Unsupported &&
            !negative.Contains(file.ProfileKey) &&
            !database.ObservedPlaybackOutcomes.Any(outcome =>
                outcome.AccountId == accountId &&
                outcome.ClientContextKey == clientContextKey &&
                outcome.VideoFileId == file.Id &&
                outcome.ContentSha256 == file.Sha256 &&
                outcome.Outcome == ObservedPlaybackOutcome.Failed));
    }

    /// <summary>
    /// Everything the query text and the non-playability facets match. Removed Videos and merged
    /// identities are never part of the active Library and never reach this.
    /// </summary>
    private IQueryable<VideoRow> Matching(Guid accountId, LibraryDiscoveryRequest request)
    {
        var videos = database.Videos
            .AsNoTracking()
            .Where(video => video.SurvivingVideoId == null &&
                            video.Availability != VideoAvailability.Removed);

        foreach (var term in LibrarySearchRule.Terms(request.Query))
        {
            // Every term has to match somewhere, though not all in the same fact. The projected
            // search text already carries every searchable fact, normalised.
            var value = term;
            videos = videos.Where(video => video.SearchText.Contains(value));
        }

        if (request.Sites.Count > 0 || request.UnknownSite)
        {
            var sites = request.Sites;
            var unknown = request.UnknownSite;
            videos = videos.Where(video =>
                (unknown && video.EstablishedSite == null) ||
                (video.EstablishedSite != null && sites.Contains(video.EstablishedSite)));
        }

        if (request.Actors.Count > 0)
        {
            var actors = request.Actors;
            videos = videos.Where(video =>
                database.VideoActors.Any(actor =>
                    actor.VideoId == video.Id && actors.Contains(actor.Name)));
        }

        if (request.WorkIdentification.Count == 1)
        {
            var established = request.WorkIdentification[0] == IdentificationResolution.Established;
            videos = videos.Where(video => video.HasEstablishedWork == established);
        }

        if (request.ReviewStatus.Count == 1)
        {
            var needed = request.ReviewStatus[0] == IdentificationReviewStatus.ReviewNeeded;
            videos = videos.Where(video => video.ReviewNeeded == needed);
        }

        if (request.Quality.Count > 0)
        {
            // The Video's own Quality is a projected column, so this is one indexed comparison
            // rather than a decision taken per occurrence. ADR 0018 says why it is not the band
            // this client would be shown.
            var bands = request.Quality;
            videos = videos.Where(video => bands.Contains(video.Quality));
        }

        if (request.PlayState.Count > 0)
        {
            var states = request.PlayState;
            var unplayed = states.Contains(PersonalPlayState.Unplayed);
            videos = videos.Where(video =>
                database.PersonalVideoStates.Any(state =>
                    state.AccountId == accountId &&
                    state.VideoId == video.Id &&
                    states.Contains(state.PlayState)) ||
                (unplayed && !database.PersonalVideoStates.Any(state =>
                    state.AccountId == accountId && state.VideoId == video.Id)));
        }

        if (request.Shelf.Count > 0)
        {
            videos = videos.Where(OnShelf(accountId, request.Shelf));
        }

        return videos;
    }

    /// <summary>
    /// Whether this Account keeps the Video on one of the named Personal Shelves. Favourites and
    /// Watch Later are the presence of their addition; Continue Watching is the rule
    /// <see cref="PersonalStateService.ToSummary"/> applies in memory, translated so the database
    /// can answer it: in progress, with qualifying activity later than any dismissal, at a resume
    /// position that is past the start and short of the Completion End Zone of the file it is in.
    /// The discovery tests hold the two readings to the same answers.
    /// </summary>
    private Expression<Func<VideoRow, bool>> OnShelf(Guid accountId, IReadOnlyList<PersonalShelf> shelves)
    {
        var favourites = shelves.Contains(PersonalShelf.Favourites);
        var watchLater = shelves.Contains(PersonalShelf.WatchLater);
        var continueWatching = shelves.Contains(PersonalShelf.ContinueWatching);
        const long longestEndZoneMilliseconds = 300_000;

        return video => database.PersonalVideoStates.Any(state =>
            state.AccountId == accountId &&
            state.VideoId == video.Id &&
            ((favourites && state.FavouriteAddedAt != null) ||
             (watchLater && state.WatchLaterAddedAt != null) ||
             (continueWatching &&
              state.PlayState == PersonalPlayState.InProgress &&
              state.LastQualifiedActivityAt != null &&
              (state.ContinueWatchingDismissedAt == null ||
               state.LastQualifiedActivityAt > state.ContinueWatchingDismissedAt) &&
              state.PlaybackProgressMilliseconds > 0 &&
              database.VideoFiles
                  .Where(file => file.Id == state.ProgressVideoFileId)
                  .Select(file => file.DurationMilliseconds <= 0 ||
                                  state.PlaybackProgressMilliseconds <
                                  file.DurationMilliseconds -
                                  (file.DurationMilliseconds / 10 < longestEndZoneMilliseconds
                                      ? file.DurationMilliseconds / 10
                                      : longestEndZoneMilliseconds))
                  .FirstOrDefault())));
    }

    /// <summary>
    /// Narrows a match to what Ordinary Discovery actually shows for this client. An explicit
    /// filter decides for this view; otherwise the Account's preference does, and unavailable
    /// Videos stay out until asked for.
    ///
    /// A Personal Shelf is the exception the glossary allows: a personal reference is shown
    /// whether or not this client can play it, and while its Video is merely unavailable, because
    /// what someone put there is not the Library's decision to withhold. An explicit filter still
    /// narrows a shelf, so "Favourites this browser can play" remains a question it can answer.
    /// </summary>
    private static IQueryable<VideoRow> Admit(
        IQueryable<VideoRow> videos,
        LibraryDiscoveryRequest request,
        bool preference,
        Expression<Func<VideoRow, bool>> ready,
        Expression<Func<VideoRow, bool>> attemptable)
    {
        var shelf = request.Shelf.Count > 0;

        if (request.Playability.Count > 0)
        {
            videos = videos.Where(PlayabilityFilter(request.Playability, ready, attemptable));
        }
        else if (!preference && !shelf)
        {
            videos = videos.Where(ready);
        }

        if (request.Availability.Count > 0)
        {
            var availability = request.Availability;
            return videos.Where(video => availability.Contains(video.Availability));
        }

        return shelf
            ? videos
            : videos.Where(video => video.Availability == VideoAvailability.Available);
    }

    /// <summary>
    /// Turns the requested Client Video Playability values into one predicate. The three values
    /// partition the library, so asking for all three is asking for everything.
    /// </summary>
    private static Expression<Func<VideoRow, bool>> PlayabilityFilter(
        IReadOnlyList<ClientVideoPlayability> wanted,
        Expression<Func<VideoRow, bool>> ready,
        Expression<Func<VideoRow, bool>> attemptable)
    {
        var video = Expression.Parameter(typeof(VideoRow), "video");
        var isReady = Rebind(ready, video);
        var isAttemptable = Rebind(attemptable, video);
        Expression? body = null;

        foreach (var playability in wanted.Distinct())
        {
            Expression clause = playability switch
            {
                ClientVideoPlayability.ReadyForDirectPlay => isReady,
                ClientVideoPlayability.CompatibilityUncertain =>
                    Expression.AndAlso(Expression.Not(isReady), isAttemptable),
                _ => Expression.AndAlso(Expression.Not(isReady), Expression.Not(isAttemptable)),
            };
            body = body is null ? clause : Expression.OrElse(body, clause);
        }

        return Expression.Lambda<Func<VideoRow, bool>>(
            body ?? Expression.Constant(true),
            video);
    }

    private static Expression<Func<VideoRow, bool>> Not(Expression<Func<VideoRow, bool>> predicate)
    {
        var video = Expression.Parameter(typeof(VideoRow), "video");

        return Expression.Lambda<Func<VideoRow, bool>>(
            Expression.Not(Rebind(predicate, video)),
            video);
    }

    /// <summary>
    /// Rewrites a predicate's body onto another parameter. Combining the predicates by invoking
    /// them would leave a lambda invocation in the tree, which the database provider cannot
    /// translate; substituting the parameter leaves one ordinary expression it can.
    /// </summary>
    private static Expression Rebind(
        Expression<Func<VideoRow, bool>> predicate,
        ParameterExpression parameter) =>
        new ParameterSubstitution(predicate.Parameters[0], parameter).Visit(predicate.Body);

    private sealed class ParameterSubstitution(ParameterExpression from, Expression to)
        : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == from ? to : base.VisitParameter(node);
    }

    /// <summary>
    /// Discovery Date descending by default, so later enrichment never makes an old Video look
    /// newly added. Title A-Z, best Video Quality first and longest runtime first are the explicit
    /// alternatives that hold for every Account; most recently played and best rated read this
    /// Account's own Personal State, and put the Videos it has no such state for last. Shelf order
    /// is the order the chosen Personal Shelf keeps, and Newest where none is chosen.
    /// </summary>
    private IQueryable<VideoRow> Order(
        IQueryable<VideoRow> videos,
        LibraryDiscoveryRequest request,
        Guid accountId) =>
        request.Sort switch
        {
            LibrarySortOrder.TitleAscending => videos
                .OrderBy(video => video.DisplayLabel)
                .ThenByDescending(video => video.DiscoveryDate),
            // A band holds thousands of Videos and says nothing about which of them to read first,
            // so the default order decides inside it rather than leaving it to the row order.
            LibrarySortOrder.QualityDescending => videos
                .OrderByDescending(video => video.Quality)
                .ThenByDescending(video => video.DiscoveryDate)
                .ThenBy(video => video.Id),
            // The runtime is the longest Available occurrence's, which is the same fact the Video's
            // Quality is taken from. A missing value sorts below every present one.
            LibrarySortOrder.LongestFirst => videos
                .OrderByDescending(video => database.VideoFiles
                    .Where(file => file.VideoId == video.Id &&
                                   file.Availability == VideoFileAvailability.Available)
                    .Max(file => (long?)file.DurationMilliseconds))
                .ThenByDescending(video => video.DiscoveryDate)
                .ThenBy(video => video.Id),
            LibrarySortOrder.RecentlyPlayed => videos
                .OrderByDescending(video => database.PersonalVideoStates
                    .Where(state => state.AccountId == accountId && state.VideoId == video.Id)
                    .Select(state => state.PlayStateChangedAt)
                    .FirstOrDefault())
                .ThenByDescending(video => video.DiscoveryDate)
                .ThenBy(video => video.Id),
            LibrarySortOrder.BestRated => videos
                .OrderByDescending(video => database.PersonalVideoStates
                    .Where(state => state.AccountId == accountId && state.VideoId == video.Id)
                    .Select(state => state.PersonalRating)
                    .FirstOrDefault())
                .ThenByDescending(video => video.DiscoveryDate)
                .ThenBy(video => video.Id),
            // Watch Later is a queue, oldest addition first; every other shelf leads with what
            // entered it last. A Video that is on none of the chosen shelves, which the Library can
            // hold when the order is chosen without the facet, comes last.
            LibrarySortOrder.ShelfOrder when request.Shelf is [PersonalShelf.WatchLater] => videos
                .OrderBy(video => database.PersonalVideoStates
                    .Where(state => state.AccountId == accountId && state.VideoId == video.Id)
                    .Select(state => state.WatchLaterAddedAt)
                    .FirstOrDefault() == null)
                .ThenBy(video => database.PersonalVideoStates
                    .Where(state => state.AccountId == accountId && state.VideoId == video.Id)
                    .Select(state => state.WatchLaterAddedAt)
                    .FirstOrDefault())
                .ThenByDescending(video => video.DiscoveryDate)
                .ThenBy(video => video.Id),
            LibrarySortOrder.ShelfOrder when request.Shelf.Count > 0 => videos
                .OrderByDescending(video => database.PersonalVideoStates
                    .Where(state => state.AccountId == accountId && state.VideoId == video.Id)
                    .Select(ShelfMoment(request.Shelf))
                    .FirstOrDefault())
                .ThenByDescending(video => video.DiscoveryDate)
                .ThenBy(video => video.Id),
            _ => videos.OrderByDescending(video => video.DiscoveryDate).ThenBy(video => video.Id),
        };

    /// <summary>
    /// When a Personal State last entered one of the named shelves: the latest of the additions
    /// and the qualifying activity that the chosen shelves are made of. A shelf that is not chosen,
    /// or an entry the Video never made, counts as the beginning of time rather than as nothing,
    /// because the database answers a comparison with nothing as nothing and would drop the Video
    /// to the end for having one shelf fewer. Written as one expression so the database orders by it.
    /// </summary>
    private static Expression<Func<PersonalVideoStateRow, DateTime>> ShelfMoment(
        IReadOnlyList<PersonalShelf> shelves)
    {
        var favourites = shelves.Contains(PersonalShelf.Favourites);
        var watchLater = shelves.Contains(PersonalShelf.WatchLater);
        var continueWatching = shelves.Contains(PersonalShelf.ContinueWatching);
        var never = DateTime.MinValue;

        return state =>
            ((favourites ? state.FavouriteAddedAt : null) ?? never) >=
            ((watchLater ? state.WatchLaterAddedAt : null) ?? never)
                ? (((favourites ? state.FavouriteAddedAt : null) ?? never) >=
                   ((continueWatching ? state.LastQualifiedActivityAt : null) ?? never)
                    ? ((favourites ? state.FavouriteAddedAt : null) ?? never)
                    : ((continueWatching ? state.LastQualifiedActivityAt : null) ?? never))
                : (((watchLater ? state.WatchLaterAddedAt : null) ?? never) >=
                   ((continueWatching ? state.LastQualifiedActivityAt : null) ?? never)
                    ? ((watchLater ? state.WatchLaterAddedAt : null) ?? never)
                    : ((continueWatching ? state.LastQualifiedActivityAt : null) ?? never));
    }

    /// <summary>
    /// Loads the page's Videos with everything a card needs, including what this client may do with
    /// each of them. Only the page is loaded, which is the whole point of projecting the facts the
    /// filter needed.
    /// </summary>
    private async Task<IReadOnlyList<VideoSummary>> LoadAsync(
        Guid accountId,
        string clientContextKey,
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var videos = await database.Videos
            .AsNoTracking()
            .Include(video => video.Metadata)
            .Include(video => video.VideoFiles)
            .Include(video => video.IdentificationClaims)
            .Include(video => video.PersonalStates.Where(state => state.AccountId == accountId))
            .Where(video => ids.Contains(video.Id))
            .ToListAsync(cancellationToken);
        var plans = await planner.PlanAsync(accountId, clientContextKey, videos, cancellationToken);
        var byId = videos.ToDictionary(video => video.Id);

        return ids
            .Where(byId.ContainsKey)
            .Select(id => VideoCatalog.Map(byId[id], accountId, plans[id]))
            .ToArray();
    }
}
