using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Personal;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Personal;

/// <summary>One Video chosen for a section, with the facts that chose it.</summary>
public sealed record ResurfacedVideo(Guid VideoId, IReadOnlyList<RecommendationReason> Reasons);

/// <summary>
/// The two sections that are not about what an Account is currently watching: Long unseen, which
/// is about forgetting, and Not yet discovered, which is about never having looked.
///
/// Both draw from Ordinary Discovery for the current Account and client, so what they offer is
/// something the reader can actually press play on. Neither reads another Account's state, and
/// the affinity behind discovery is inferred from this Account's own evidence and normalised so
/// that whoever appears most often in the library cannot win on volume.
/// </summary>
public sealed class ResurfacingService(
    ViewerDbContext database,
    LibraryDiscovery discovery,
    ReturnInterestService returnInterest)
{
    /// <summary>
    /// How many candidates a section reads before choosing from them. It bounds every query here:
    /// a page is a few dozen Videos, and reading a large library to choose eight of them is the
    /// thing this number exists to prevent.
    /// </summary>
    private const int WindowSize = 200;

    /// <summary>
    /// How many Actors and Sites the affinity is taken from. Beyond a couple of dozen the rates
    /// are too thin to mean anything, and a longer list would only widen the query.
    /// </summary>
    private const int AffinityNames = 24;

    /// <summary>
    /// Videos this Account showed positive interest in, watched at some point, and has not watched
    /// for a while. Age orders them, and only among Videos there is positive evidence for: a Video
    /// somebody sampled for eight seconds two years ago is not a forgotten favourite.
    /// </summary>
    public async Task<IReadOnlyList<ResurfacedVideo>> LongUnseenAsync(
        Guid accountId,
        string clientContextKey,
        DateTimeOffset now,
        int take,
        IReadOnlyCollection<Guid> exclude,
        CancellationToken cancellationToken = default)
    {
        var candidates = (await returnInterest.CandidatesAsync(accountId, cancellationToken))
            .Except(exclude)
            .ToArray();

        if (candidates.Length == 0 || take <= 0)
        {
            return [];
        }

        var admitted = await AdmittedAsync(
            accountId,
            clientContextKey,
            new LibraryDiscoveryRequest { Videos = candidates, Take = LibraryPaging.MaximumPageSize },
            cancellationToken);

        if (admitted.Count == 0)
        {
            return [];
        }

        var ranked = await returnInterest.RankAsync(
            accountId,
            clientContextKey,
            admitted,
            cancellationToken);
        var watched = await LastWatchedAsync(accountId, admitted, cancellationToken);

        return ranked
            // Prior positive interest and prior confirmed watching, both required. The first is
            // the ranking's own answer; the second is why a Video that was only ever liked without
            // being watched belongs in a different section than this one.
            .Where(video =>
                video.Tier > 0 || video.Score > 0)
            .Where(video => watched.ContainsKey(video.VideoId))
            .Where(video => ResurfacingPolicy.IsLongUnseen(watched[video.VideoId], now))
            // Longest unseen first, among the positively evidenced. A moment nobody recorded gives
            // no ordering benefit and goes last rather than pretending to be the oldest.
            .OrderBy(video => watched[video.VideoId] is null ? 1 : 0)
            .ThenBy(video => watched[video.VideoId] ?? DateTimeOffset.MaxValue)
            .ThenByDescending(video => video.Tier)
            .ThenByDescending(video => video.Score)
            .ThenBy(video => video.VideoId)
            .Take(take)
            .Select(video => new ResurfacedVideo(
                video.VideoId,
                [
                    watched[video.VideoId] is null
                        ? RecommendationReason.WatchedLongAgo
                        : RecommendationReason.NotWatchedForAWhile,
                ]))
            .ToArray();
    }

    /// <summary>
    /// Videos this Account has never watched here, two thirds of them led by a cautious affinity
    /// with the Actors and Sites its own evidence points at, and one third chosen without
    /// reference to any of that.
    /// </summary>
    /// <remarks>
    /// The independent third is not a garnish. It is the only part of the page that can reach a
    /// Video with barely any metadata to infer from, and the only part that does not get narrower
    /// the more somebody watches. A shortage on either side is filled from the other, and with no
    /// affinity evidence at all the whole page is independent.
    /// </remarks>
    public async Task<IReadOnlyList<ResurfacedVideo>> NeverWatchedAsync(
        Guid accountId,
        string clientContextKey,
        int take,
        int seed,
        IReadOnlyCollection<Guid> exclude,
        CancellationToken cancellationToken = default)
    {
        if (take <= 0)
        {
            return [];
        }

        var affinity = await AffinityAsync(accountId, cancellationToken);
        var unwatched = new LibraryDiscoveryRequest
        {
            WithoutWatchingEvidence = true,
            Take = LibraryPaging.MaximumPageSize,
        };

        var independentPool = await WindowAsync(
            accountId,
            clientContextKey,
            unwatched,
            seed,
            exclude,
            cancellationToken);
        var affinityPool = affinity.Names.Count == 0
            ? []
            : await WindowAsync(
                accountId,
                clientContextKey,
                unwatched with
                {
                    Actors = affinity.Actors,
                    Sites = affinity.Sites,
                },
                seed,
                exclude,
                cancellationToken);

        var affinityOrdered = (await OrderByAffinityAsync(affinityPool, affinity, seed, cancellationToken))
            .ToList();
        var independentOrdered = ResurfacingPolicy
            .InSeededOrder(independentPool, id => id, seed)
            .Except(affinityOrdered.Select(candidate => candidate.VideoId))
            .ToList();

        var (affinityCount, independentCount) = ResurfacingPolicy.Mix(
            take,
            affinityOrdered.Count,
            independentOrdered.Count);

        var chosen = affinityOrdered
            .Take(affinityCount)
            .Select(candidate => new ResurfacedVideo(
                candidate.VideoId,
                [RecommendationReason.NeverWatched, .. candidate.Reasons]))
            .Concat(independentOrdered
                .Take(independentCount)
                .Select(videoId => new ResurfacedVideo(
                    videoId,
                    [RecommendationReason.NeverWatched, RecommendationReason.SomethingDifferent])))
            .ToArray();

        // Interleaved rather than served in two blocks, so a page reads as one set of suggestions
        // instead of a taste list with an appendix.
        return Interleave(chosen, affinityCount);
    }

    /// <summary>
    /// A bounded window over what the request admits, starting at a place the seed rotates through
    /// the pool. Successive seeds therefore reach older overlooked Videos rather than returning to
    /// the newest part of the library every time.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> WindowAsync(
        Guid accountId,
        string clientContextKey,
        LibraryDiscoveryRequest request,
        int seed,
        IReadOnlyCollection<Guid> exclude,
        CancellationToken cancellationToken)
    {
        var total = (await discovery.GetAsync(
            accountId,
            clientContextKey,
            request with { Take = 1 },
            cancellationToken)).TotalMatches;

        if (total == 0)
        {
            return [];
        }

        var window = new List<Guid>();
        var start = ResurfacingPolicy.WindowStart(total, WindowSize, seed);

        // The window is read a page at a time, and wraps once where it runs off the end, so that a
        // late start still returns a full window rather than the tail of the pool.
        for (var skip = start; window.Count < Math.Min(WindowSize, total);)
        {
            var page = await discovery.GetAsync(
                accountId,
                clientContextKey,
                request with { Skip = skip, Take = LibraryPaging.MaximumPageSize },
                cancellationToken);

            if (page.Videos.Count == 0)
            {
                if (skip == 0)
                {
                    break;
                }

                skip = 0;
                continue;
            }

            window.AddRange(page.Videos
                .Select(video => video.Id)
                .Where(id => !exclude.Contains(id) && !window.Contains(id)));
            skip += page.Videos.Count;

            if (skip >= total)
            {
                if (start == 0)
                {
                    break;
                }

                skip = 0;
                start = 0;
            }
        }

        return window;
    }

    private async Task<IReadOnlyList<Guid>> AdmittedAsync(
        Guid accountId,
        string clientContextKey,
        LibraryDiscoveryRequest request,
        CancellationToken cancellationToken) =>
        (await discovery.GetAsync(accountId, clientContextKey, request, cancellationToken))
            .Videos
            .Select(video => video.Id)
            .ToArray();

    private async Task<Dictionary<Guid, DateTimeOffset?>> LastWatchedAsync(
        Guid accountId,
        IReadOnlyCollection<Guid> videoIds,
        CancellationToken cancellationToken) =>
        (await database.PersonalVideoStates
            .AsNoTracking()
            .Where(state =>
                state.AccountId == accountId &&
                videoIds.Contains(state.VideoId) &&
                (state.LastWatchedAt != null ||
                 state.PlayCount > 0 ||
                 state.AccumulatedWatchDurationMilliseconds > 0))
            .Select(state => new { state.VideoId, state.LastWatchedAt })
            .ToListAsync(cancellationToken))
        .ToDictionary(
            state => state.VideoId,
            state => state.LastWatchedAt is { } watched
                ? new DateTimeOffset(DateTime.SpecifyKind(watched, DateTimeKind.Utc))
                : (DateTimeOffset?)null);

    /// <summary>
    /// Which Actors and Sites this Account's own evidence points at, as rates rather than counts,
    /// plus the Actors it keeps explicitly. A Dislike is not consulted at all: it belongs to its
    /// Video, and holding it against everybody in that Video is exactly the narrow loop the
    /// independent third of the page exists to avoid.
    /// </summary>
    private async Task<Affinities> AffinityAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var liked = await returnInterest.CandidatesAsync(accountId, cancellationToken);
        var favouriteActors = await database.PersonalActorStates
            .AsNoTracking()
            .Where(state => state.AccountId == accountId)
            .Select(state => state.PrdbActorId)
            .ToListAsync(cancellationToken);
        var favouriteNames = favouriteActors.Count == 0
            ? []
            : await database.VideoActors
                .AsNoTracking()
                .Where(actor => actor.PrdbActorId != null && favouriteActors.Contains(actor.PrdbActorId))
                .Select(actor => actor.Name)
                .Distinct()
                .ToListAsync(cancellationToken);

        if (liked.Count == 0 && favouriteNames.Count == 0)
        {
            return new Affinities([], [], new Dictionary<string, double>(), favouriteNames.ToHashSet());
        }

        var actorEvidence = await database.VideoActors
            .AsNoTracking()
            .Where(actor => liked.Contains(actor.VideoId))
            .GroupBy(actor => actor.Name)
            .Select(group => new { Name = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var evidencedActors = actorEvidence.Select(evidence => evidence.Name).ToArray();
        var actorTotals = await database.VideoActors
            .AsNoTracking()
            .Where(actor => evidencedActors.Contains(actor.Name))
            .GroupBy(actor => actor.Name)
            .Select(group => new { Name = group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.Name, group => group.Count, cancellationToken);

        var siteEvidence = await database.Videos
            .AsNoTracking()
            .Where(video => liked.Contains(video.Id) && video.EstablishedSite != null)
            .GroupBy(video => video.EstablishedSite!)
            .Select(group => new { Name = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var evidencedSites = siteEvidence.Select(evidence => evidence.Name).ToArray();
        var siteTotals = await database.Videos
            .AsNoTracking()
            .Where(video =>
                video.EstablishedSite != null &&
                evidencedSites.Contains(video.EstablishedSite))
            .GroupBy(video => video.EstablishedSite!)
            .Select(group => new { Name = group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.Name, group => group.Count, cancellationToken);

        var weights = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var evidence in actorEvidence)
        {
            var weight = ResurfacingPolicy.Affinity(
                evidence.Count,
                actorTotals.GetValueOrDefault(evidence.Name, evidence.Count));
            if (weight > 0) weights[evidence.Name] = weight;
        }

        foreach (var name in favouriteNames)
        {
            weights[name] = Math.Max(
                weights.GetValueOrDefault(name),
                ResurfacingPolicy.FavouriteActorAffinity);
        }

        var siteWeights = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var evidence in siteEvidence)
        {
            var weight = ResurfacingPolicy.Affinity(
                evidence.Count,
                siteTotals.GetValueOrDefault(evidence.Name, evidence.Count));
            if (weight > 0) siteWeights[evidence.Name] = weight;
        }

        var actors = weights
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Take(AffinityNames)
            .ToArray();
        var sites = siteWeights
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Take(AffinityNames)
            .ToArray();

        foreach (var site in sites)
        {
            weights[site.Key] = site.Value;
        }

        return new Affinities(
            actors.Select(entry => entry.Key).ToArray(),
            sites.Select(entry => entry.Key).ToArray(),
            weights,
            favouriteNames.ToHashSet(StringComparer.Ordinal));
    }

    private async Task<IReadOnlyList<AffinityCandidate>> OrderByAffinityAsync(
        IReadOnlyList<Guid> candidates,
        Affinities affinity,
        int seed,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var actors = (await database.VideoActors
            .AsNoTracking()
            .Where(actor => candidates.Contains(actor.VideoId))
            .Select(actor => new { actor.VideoId, actor.Name })
            .ToListAsync(cancellationToken))
            .GroupBy(actor => actor.VideoId)
            .ToDictionary(group => group.Key, group => group.Select(actor => actor.Name).ToArray());
        var sites = await database.Videos
            .AsNoTracking()
            .Where(video => candidates.Contains(video.Id) && video.EstablishedSite != null)
            .ToDictionaryAsync(video => video.Id, video => video.EstablishedSite!, cancellationToken);

        var scored = candidates.Select(videoId =>
        {
            var names = actors.GetValueOrDefault(videoId, []);
            var site = sites.GetValueOrDefault(videoId);
            var contributions = names
                .Select(name => affinity.Weights.GetValueOrDefault(name))
                .Concat(site is null ? [] : [affinity.Weights.GetValueOrDefault(site)]);
            var reasons = new List<RecommendationReason>();

            if (names.Any(affinity.FavouriteActors.Contains))
            {
                reasons.Add(RecommendationReason.WithAFavouriteActor);
            }
            else if (names.Any(name => affinity.Weights.ContainsKey(name)))
            {
                reasons.Add(RecommendationReason.SharesAnActorYouWatch);
            }

            if (site is not null && affinity.Sites.Contains(site))
            {
                reasons.Add(RecommendationReason.FromASiteYouWatch);
            }

            return new AffinityCandidate(
                videoId,
                ResurfacingPolicy.CandidateAffinity(contributions),
                reasons);
        })
        .Where(candidate => candidate.Affinity > 0 && candidate.Reasons.Count > 0)
        .ToArray();

        // Strongest affinity first, and the seed decides between equals — which is most of them,
        // because the ceiling flattens the top. That is what makes Other suggestions offer a
        // different page rather than the same one with two swaps.
        return ResurfacingPolicy
            .InSeededOrder(scored, candidate => candidate.VideoId, seed)
            .OrderByDescending(candidate => Math.Round(candidate.Affinity, 2))
            .ToArray();
    }

    /// <summary>
    /// Mixes the affinity-led choices through the independent ones rather than putting one block
    /// after the other, so that the part of the page nobody's taste chose is not the part nobody
    /// scrolls to.
    /// </summary>
    private static IReadOnlyList<ResurfacedVideo> Interleave(
        IReadOnlyList<ResurfacedVideo> chosen,
        int affinityCount)
    {
        var led = chosen.Take(affinityCount).ToList();
        var independent = chosen.Skip(affinityCount).ToList();
        var mixed = new List<ResurfacedVideo>(chosen.Count);
        var everyNth = independent.Count == 0 ? int.MaxValue : Math.Max(1, led.Count / independent.Count);

        var next = 0;

        for (var index = 0; index < led.Count; index++)
        {
            mixed.Add(led[index]);

            if ((index + 1) % everyNth == 0 && next < independent.Count)
            {
                mixed.Add(independent[next++]);
            }
        }

        mixed.AddRange(independent.Skip(next));

        return mixed;
    }

    private sealed record Affinities(
        IReadOnlyList<string> Actors,
        IReadOnlyList<string> Sites,
        IReadOnlyDictionary<string, double> Weights,
        IReadOnlySet<string> FavouriteActors)
    {
        public IReadOnlyCollection<string> Names => [.. Actors, .. Sites];
    }

    private sealed record AffinityCandidate(
        Guid VideoId,
        double Affinity,
        IReadOnlyList<RecommendationReason> Reasons);
}
