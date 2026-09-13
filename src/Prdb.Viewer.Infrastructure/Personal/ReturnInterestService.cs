using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Personal;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Personal;

/// <summary>
/// One Video as the ranking leaves it: what it scored, why, and when this Account last watched it.
/// </summary>
public sealed record RankedVideo(
    Guid VideoId,
    int Tier,
    double Score,
    IReadOnlyList<RecommendationReason> Reasons,
    DateTimeOffset? LastWatchedAt);

/// <summary>
/// Return Interest for one Account, gathered from its own Personal State and decided by
/// <see cref="ReturnInterestPolicy"/>.
///
/// The gathering is here and the deciding is in the Core, which is the point: the policy is a pure
/// function of the evidence, so an ordering can be reproduced from what it was given and a test of
/// the rules needs no database at all. Nothing here reads another Account's state, and nothing
/// here leaves the installation.
/// </summary>
public sealed class ReturnInterestService(
    ViewerDbContext database,
    PersonalStateService personalState,
    PlaylistService playlists)
{
    /// <summary>
    /// The Videos this Account has said or done anything positive about: watched, liked, loved,
    /// filed in a Playlist, or kept as a Favourite. Disliked Videos are never among them.
    /// </summary>
    /// <remarks>
    /// This is the pool For you to watch again draws from, and it is deliberately generous: what
    /// is actually offerable is decided afterwards, by Ordinary Discovery and by the exclusions
    /// every section shares. Deciding both here would mean one query that knows about clients,
    /// dismissals and playability, which is three things this is not about.
    /// </remarks>
    public async Task<IReadOnlyList<Guid>> CandidatesAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var stated = await database.PersonalVideoStates
            .AsNoTracking()
            .Where(state =>
                state.AccountId == accountId &&
                state.Reaction != PersonalReaction.Dislike &&
                (state.Reaction == PersonalReaction.Like ||
                 state.Reaction == PersonalReaction.Love ||
                 state.FavouriteAddedAt != null ||
                 state.LastWatchedAt != null ||
                 state.PlayCount > 0 ||
                 state.AccumulatedWatchDurationMilliseconds > 0))
            .Select(state => state.VideoId)
            .ToListAsync(cancellationToken);

        var filed = await database.PlaylistEntries
            .AsNoTracking()
            .Where(entry => entry.Playlist.AccountId == accountId)
            .Select(entry => entry.VideoId)
            .ToListAsync(cancellationToken);

        var disliked = await database.PersonalVideoStates
            .AsNoTracking()
            .Where(state =>
                state.AccountId == accountId && state.Reaction == PersonalReaction.Dislike)
            .Select(state => state.VideoId)
            .ToListAsync(cancellationToken);

        return stated.Concat(filed).Distinct().Except(disliked).ToArray();
    }

    /// <summary>
    /// Ranks the named Videos, warmest first. Explicit Like and Love are tiers above everything
    /// behaviour can produce; within a tier the evidence decides, and the most recently watched
    /// leads where two Videos are otherwise equal. A Video with no evidence at all still comes
    /// back, scoring nothing, because whether to offer it is the caller's question.
    /// </summary>
    public async Task<IReadOnlyList<RankedVideo>> RankAsync(
        Guid accountId,
        string clientContextKey,
        IReadOnlyCollection<Guid> videoIds,
        CancellationToken cancellationToken = default)
    {
        if (videoIds.Count == 0)
        {
            return [];
        }

        var evidence = await EvidenceAsync(accountId, clientContextKey, videoIds, cancellationToken);

        return videoIds
            .Select(videoId =>
            {
                var (input, lastWatchedAt) = evidence[videoId];
                var interest = ReturnInterestPolicy.Evaluate(input);
                return new RankedVideo(
                    videoId,
                    interest.Tier,
                    interest.Score,
                    interest.Reasons,
                    lastWatchedAt);
            })
            .OrderByDescending(ranked => ranked.Tier)
            .ThenByDescending(ranked => ranked.Score)
            .ThenByDescending(ranked => ranked.LastWatchedAt ?? DateTimeOffset.MinValue)
            // Two Videos with identical evidence are ordered by their identity rather than by
            // whatever the database happened to return first, so a page is the same page twice.
            .ThenBy(ranked => ranked.VideoId)
            .ToArray();
    }

    /// <summary>
    /// Everything the policy needs about the named Videos, in four questions rather than four per
    /// Video. A page of candidates is dozens of Videos, and asking each of them separately is how
    /// a recommendation screen becomes the slowest thing in the application.
    /// </summary>
    private async Task<Dictionary<Guid, (ReturnInterestEvidence Evidence, DateTimeOffset? LastWatchedAt)>>
        EvidenceAsync(
            Guid accountId,
            string clientContextKey,
            IReadOnlyCollection<Guid> videoIds,
            CancellationToken cancellationToken)
    {
        var states = await database.PersonalVideoStates
            .AsNoTracking()
            .Where(state => state.AccountId == accountId && videoIds.Contains(state.VideoId))
            .Select(state => new
            {
                state.VideoId,
                state.Reaction,
                state.FavouriteAddedAt,
                state.LastWatchedAt,
                state.PlayCount,
                state.AccumulatedWatchDurationMilliseconds,
            })
            .ToDictionaryAsync(state => state.VideoId, cancellationToken);

        var sessions = (await database.PlaybackAttempts
            .AsNoTracking()
            .Where(attempt =>
                attempt.AccountId == accountId &&
                videoIds.Contains(attempt.VideoId) &&
                attempt.LastActivityAt != null)
            .Select(attempt => new
            {
                attempt.VideoId,
                attempt.ActiveWatchDurationMilliseconds,
                attempt.LongestUninterruptedRunMilliseconds,
                attempt.Departure,
            })
            .ToListAsync(cancellationToken))
            .GroupBy(attempt => attempt.VideoId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(attempt => new ViewingSessionEvidence(
                        attempt.ActiveWatchDurationMilliseconds,
                        attempt.LongestUninterruptedRunMilliseconds,
                        attempt.Departure))
                    .ToArray());

        var memberships = await playlists.MembershipsAsync(accountId, videoIds, cancellationToken);
        var visit = (await personalState.CurrentBrowsingVisitAsync(
            accountId,
            clientContextKey,
            cancellationToken)).ToHashSet();

        return videoIds.Distinct().ToDictionary(
            videoId => videoId,
            videoId =>
            {
                states.TryGetValue(videoId, out var state);
                var retained = sessions.GetValueOrDefault(videoId, []);
                // Watching that happened before the sessions were retained. It establishes that
                // this Account watched the Video and nothing else about it: no durations, no runs,
                // no departures. Inventing those is what ADR 0022 forbids.
                var prior = retained.Length == 0 &&
                    state is not null &&
                    (state.PlayCount > 0 ||
                     state.AccumulatedWatchDurationMilliseconds > 0 ||
                     state.LastWatchedAt is not null);

                return (
                    new ReturnInterestEvidence(
                        retained,
                        state?.Reaction,
                        state?.FavouriteAddedAt is not null,
                        memberships.GetValueOrDefault(videoId),
                        visit.Contains(videoId),
                        prior),
                    state?.LastWatchedAt is { } watched
                        ? new DateTimeOffset(DateTime.SpecifyKind(watched, DateTimeKind.Utc))
                        : (DateTimeOffset?)null);
            });
    }
}
