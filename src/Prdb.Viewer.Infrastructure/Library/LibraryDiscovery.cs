using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Core.Personal;
using Prdb.Viewer.Infrastructure.Persistence;

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
    /// catalogue of every Site and Actor it has ever seen.
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
        var ready = ReadyHere(accountId, clientContextKey);
        var attemptable = WorthAttemptingHere(accountId, clientContextKey);

        // The counts describe what the current rules keep out of the answer, so they are taken
        // from the same match before playability and availability narrow it.
        var admitted = Admit(matched, request, preference, ready, attemptable);
        var total = await admitted.CountAsync(cancellationToken);
        var take = LibraryPaging.Clamp(request.Take);
        var page = await Order(admitted, request.Sort)
            .Skip(Math.Max(0, request.Skip))
            .Take(take + 1)
            .Select(video => video.Id)
            .ToListAsync(cancellationToken);
        var hasMore = page.Count > take;
        var ids = page.Take(take).ToArray();
        var hiddenNotReady = request.Playability.Count > 0 || preference
            ? 0
            : await matched
                .Where(video => video.Availability == VideoAvailability.Available)
                .Where(Not(ready))
                .CountAsync(cancellationToken);
        var hiddenUnavailable = request.Availability.Count > 0
            ? 0
            : await matched.CountAsync(
                video => video.Availability == VideoAvailability.Unavailable,
                cancellationToken);

        return new LibraryPage(
            await LoadAsync(accountId, clientContextKey, ids, request.Sort, cancellationToken),
            total,
            hiddenNotReady,
            hiddenUnavailable,
            hasMore,
            preference);
    }

    /// <summary>
    /// The Established Sites and Actors of the Videos an Account can currently discover, with the
    /// counts a facet list shows. Removed Videos are never counted.
    /// </summary>
    public async Task<LibraryFacets> GetFacetsAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var active = database.Videos
            .AsNoTracking()
            .Where(video => video.SurvivingVideoId == null &&
                            video.Availability != VideoAvailability.Removed);
        var sites = await active
            .Where(video => video.EstablishedSite != null)
            .GroupBy(video => video.EstablishedSite!)
            .Select(group => new { Value = group.Key, Count = group.Count() })
            .OrderByDescending(value => value.Count)
            .ThenBy(value => value.Value)
            .Take(FacetLimit)
            .ToListAsync(cancellationToken);
        var actors = await database.VideoActors
            .AsNoTracking()
            .Where(actor => active.Any(video => video.Id == actor.VideoId))
            .GroupBy(actor => actor.Name)
            .Select(group => new { Value = group.Key, Count = group.Count() })
            .OrderByDescending(value => value.Count)
            .ThenBy(value => value.Value)
            .Take(FacetLimit)
            .ToListAsync(cancellationToken);

        return new LibraryFacets(
            sites.Select(site => new LibraryFacetValue(site.Value, site.Count)).ToArray(),
            actors.Select(actor => new LibraryFacetValue(actor.Value, actor.Count)).ToArray());
    }

    /// <summary>
    /// Whether at least one Available occurrence is ready for direct play here: one this client has
    /// already played, or one it has not ruled out that is either the conservative baseline or a
    /// Client-Dependent file it assessed positively.
    /// </summary>
    private Expression<Func<VideoRow, bool>> ReadyHere(Guid accountId, string clientContextKey) =>
        video => database.VideoFiles.Any(file =>
            file.VideoId == video.Id &&
            file.Availability == VideoFileAvailability.Available &&
            (database.ObservedPlaybackOutcomes.Any(outcome =>
                 outcome.AccountId == accountId &&
                 outcome.ClientContextKey == clientContextKey &&
                 outcome.VideoFileId == file.Id &&
                 outcome.ContentSha256 == file.Sha256 &&
                 outcome.Outcome == ObservedPlaybackOutcome.Succeeded) ||
             (!database.ObservedPlaybackOutcomes.Any(outcome =>
                  outcome.AccountId == accountId &&
                  outcome.ClientContextKey == clientContextKey &&
                  outcome.VideoFileId == file.Id &&
                  outcome.ContentSha256 == file.Sha256 &&
                  outcome.Outcome == ObservedPlaybackOutcome.Failed) &&
              !database.ClientPlaybackAssessments.Any(assessment =>
                  assessment.AccountId == accountId &&
                  assessment.ClientContextKey == clientContextKey &&
                  assessment.ProfileKey == file.ProfileKey &&
                  assessment.Verdict == ClientPlaybackAssessmentVerdict.Negative) &&
              (file.DirectPlayClassification == DirectPlayClassification.BaselineCandidate ||
               (file.DirectPlayClassification == DirectPlayClassification.ClientDependent &&
                database.ClientPlaybackAssessments.Any(assessment =>
                    assessment.AccountId == accountId &&
                    assessment.ClientContextKey == clientContextKey &&
                    assessment.ProfileKey == file.ProfileKey &&
                    assessment.Verdict == ClientPlaybackAssessmentVerdict.Positive))))));

    /// <summary>
    /// Whether an attempt is still plausible: an Available occurrence with some direct-play path
    /// that this client has neither ruled out nor confirmed.
    /// </summary>
    private Expression<Func<VideoRow, bool>> WorthAttemptingHere(
        Guid accountId,
        string clientContextKey) =>
        video => database.VideoFiles.Any(file =>
            file.VideoId == video.Id &&
            file.Availability == VideoFileAvailability.Available &&
            file.DirectPlayClassification != DirectPlayClassification.Unsupported &&
            !database.ObservedPlaybackOutcomes.Any(outcome =>
                outcome.AccountId == accountId &&
                outcome.ClientContextKey == clientContextKey &&
                outcome.VideoFileId == file.Id &&
                outcome.ContentSha256 == file.Sha256 &&
                outcome.Outcome == ObservedPlaybackOutcome.Failed) &&
            !database.ClientPlaybackAssessments.Any(assessment =>
                assessment.AccountId == accountId &&
                assessment.ClientContextKey == clientContextKey &&
                assessment.ProfileKey == file.ProfileKey &&
                assessment.Verdict == ClientPlaybackAssessmentVerdict.Negative));

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

        return videos;
    }

    /// <summary>
    /// Narrows a match to what Ordinary Discovery actually shows for this client. An explicit
    /// filter decides for this view; otherwise the Account's preference does, and unavailable
    /// Videos stay out until asked for.
    /// </summary>
    private static IQueryable<VideoRow> Admit(
        IQueryable<VideoRow> videos,
        LibraryDiscoveryRequest request,
        bool preference,
        Expression<Func<VideoRow, bool>> ready,
        Expression<Func<VideoRow, bool>> attemptable)
    {
        if (request.Playability.Count > 0)
        {
            videos = videos.Where(PlayabilityFilter(request.Playability, ready, attemptable));
        }
        else if (!preference)
        {
            videos = videos.Where(ready);
        }

        if (request.Availability.Count > 0)
        {
            var availability = request.Availability;
            return videos.Where(video => availability.Contains(video.Availability));
        }

        return videos.Where(video => video.Availability == VideoAvailability.Available);
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
    /// newly added. Title A-Z is the one explicit alternative.
    /// </summary>
    private static IQueryable<VideoRow> Order(IQueryable<VideoRow> videos, LibrarySortOrder sort) =>
        sort == LibrarySortOrder.TitleAscending
            ? videos.OrderBy(video => video.DisplayLabel).ThenByDescending(video => video.DiscoveryDate)
            : videos.OrderByDescending(video => video.DiscoveryDate).ThenBy(video => video.Id);

    /// <summary>
    /// Loads the page's Videos with everything a card needs, including what this client may do with
    /// each of them. Only the page is loaded, which is the whole point of projecting the facts the
    /// filter needed.
    /// </summary>
    private async Task<IReadOnlyList<VideoSummary>> LoadAsync(
        Guid accountId,
        string clientContextKey,
        IReadOnlyList<Guid> ids,
        LibrarySortOrder sort,
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
