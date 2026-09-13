using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Personal;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Personal;

/// <summary>One Video offered on a page of Recommendations, with the facts that chose it.</summary>
public sealed record RecommendedVideo(
    VideoSummary Video,
    IReadOnlyList<RecommendationReason> Reasons);

/// <summary>
/// One section of the page. <paramref name="Exhausted"/> says that the section's pool holds
/// nothing further, which is what lets the screen say so rather than looking broken.
/// </summary>
public sealed record RecommendationSectionPage(
    RecommendationSection Section,
    IReadOnlyList<RecommendedVideo> Videos,
    bool Exhausted);

/// <summary>
/// One page of Recommendations. The seed is answered as well as taken, so a screen can page and
/// re-render against the same selection and ask for a different one by changing it.
/// </summary>
public sealed record RecommendationPage(
    IReadOnlyList<RecommendationSectionPage> Sections,
    int Seed,
    /// <summary>
    /// Whether this Account has anything for the recommender to work from. With nothing, the page
    /// is honest discovery rather than a preference nobody expressed.
    /// </summary>
    bool HasHistory);

public enum DismissalVerdict
{
    Updated,
    VideoNotFound,
}

/// <summary>
/// The three Recommendation Sections, composed for one Account and one client.
///
/// Everything here is Personal State. No other Account and no Administrator can reach any of it,
/// nothing about a User's viewing leaves the installation, and the whole page is computed from
/// what this Account did on this installation.
/// </summary>
public sealed class RecommendationService(
    ViewerDbContext database,
    LibraryDiscovery discovery,
    ReturnInterestService returnInterest,
    ResurfacingService resurfacing,
    TimeProvider timeProvider)
{
    /// <summary>How long a Temporary Dismissal lasts, from the moment it was made.</summary>
    public static readonly TimeSpan DismissalDuration = TimeSpan.FromHours(24);

    public const int DefaultSectionSize = 12;

    public const int MaximumSectionSize = 24;

    /// <summary>
    /// The page, section by section.
    /// </summary>
    /// <remarks>
    /// Long unseen is chosen first and its choices are reserved, because it draws from the same
    /// pool of positively evidenced Videos that For you to watch again does and is the narrower of
    /// the two: filling the wider one first would leave the narrower with nothing. The reader sees
    /// return interest first all the same, because that is the question the page is answering.
    /// </remarks>
    public async Task<RecommendationPage> GetAsync(
        Guid accountId,
        string clientContextKey,
        int? seed = null,
        int take = DefaultSectionSize,
        CancellationToken cancellationToken = default)
    {
        var size = Math.Clamp(take, 1, MaximumSectionSize);
        var now = timeProvider.GetUtcNow();
        var chosenSeed = seed ?? DefaultSeed(accountId, now);
        var excluded = await ExcludedAsync(accountId, now, cancellationToken);

        var longUnseen = await resurfacing.LongUnseenAsync(
            accountId,
            clientContextKey,
            now,
            size,
            excluded,
            cancellationToken);
        var reserved = excluded
            .Concat(longUnseen.Select(video => video.VideoId))
            .ToArray();

        var again = await WatchAgainAsync(accountId, clientContextKey, size, reserved, cancellationToken);
        var taken = reserved.Concat(again.Select(video => video.VideoId)).ToArray();

        var discovered = await resurfacing.NeverWatchedAsync(
            accountId,
            clientContextKey,
            size,
            chosenSeed,
            taken,
            cancellationToken);

        var summaries = await SummariesAsync(
            accountId,
            clientContextKey,
            [
                .. again.Select(video => video.VideoId),
                .. longUnseen.Select(video => video.VideoId),
                .. discovered.Select(video => video.VideoId),
            ],
            cancellationToken);

        return new RecommendationPage(
            [
                Section(RecommendationSection.ForYouToWatchAgain, again, summaries, size),
                Section(RecommendationSection.LongUnseen, longUnseen, summaries, size),
                Section(RecommendationSection.NotYetDiscovered, discovered, summaries, size),
            ],
            chosenSeed,
            (await returnInterest.CandidatesAsync(accountId, cancellationToken)).Count > 0);
    }

    /// <summary>
    /// Puts a Video aside for twenty-four hours. It changes no preference and no playback state,
    /// and it holds across this Account's clients rather than only the one that asked.
    /// </summary>
    public async Task<DismissalVerdict> DismissAsync(
        Guid accountId,
        Guid videoId,
        CancellationToken cancellationToken = default)
    {
        if (!await database.Videos.AnyAsync(video => video.Id == videoId, cancellationToken))
        {
            return DismissalVerdict.VideoNotFound;
        }

        var held = await database.RecommendationDismissals
            .AsTracking()
            .SingleOrDefaultAsync(
                dismissal => dismissal.AccountId == accountId && dismissal.VideoId == videoId,
                cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (held is null)
        {
            database.RecommendationDismissals.Add(new RecommendationDismissalRow
            {
                AccountId = accountId,
                VideoId = videoId,
                DismissedAt = now,
            });
        }
        else
        {
            // Dismissing again is another twenty-four hours from now rather than an error: the
            // reader said "not today" today.
            held.DismissedAt = now;
        }

        await database.SaveChangesAsync(cancellationToken);
        return DismissalVerdict.Updated;
    }

    /// <summary>
    /// Takes the dismissal back. It is deleting the statement rather than reasoning about a
    /// deadline, so undoing something that has already expired is the same harmless nothing.
    /// </summary>
    public async Task<DismissalVerdict> UndoDismissalAsync(
        Guid accountId,
        Guid videoId,
        CancellationToken cancellationToken = default)
    {
        await database.RecommendationDismissals
            .Where(dismissal => dismissal.AccountId == accountId && dismissal.VideoId == videoId)
            .ExecuteDeleteAsync(cancellationToken);
        Forget(dismissal => dismissal.AccountId == accountId && dismissal.VideoId == videoId);

        return DismissalVerdict.Updated;
    }

    /// <summary>
    /// The Videos this Account has put aside and whose twenty-four hours have not run out. Expired
    /// rows are deleted as they are met, so the table holds today rather than a history of every
    /// "not today" anybody ever said.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> ActiveDismissalsAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var boundary = timeProvider.GetUtcNow().UtcDateTime - DismissalDuration;
        var held = await database.RecommendationDismissals
            .AsNoTracking()
            .Where(dismissal => dismissal.AccountId == accountId)
            .Select(dismissal => new { dismissal.VideoId, dismissal.DismissedAt })
            .ToListAsync(cancellationToken);

        if (held.Any(dismissal => dismissal.DismissedAt <= boundary))
        {
            await database.RecommendationDismissals
                .Where(dismissal =>
                    dismissal.AccountId == accountId && dismissal.DismissedAt <= boundary)
                .ExecuteDeleteAsync(cancellationToken);
            Forget(dismissal =>
                dismissal.AccountId == accountId && dismissal.DismissedAt <= boundary);
        }

        return held
            .Where(dismissal => dismissal.DismissedAt > boundary)
            .Select(dismissal => dismissal.VideoId)
            .ToArray();
    }

    /// <summary>
    /// What every section keeps out: a Dislike, which is a hard exclusion until it is changed or
    /// cleared, and a Temporary Dismissal, which is one for a day. They are applied in one place
    /// so that the three sections cannot disagree about them.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ExcludedAsync(
        Guid accountId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var disliked = await database.PersonalVideoStates
            .AsNoTracking()
            .Where(state =>
                state.AccountId == accountId && state.Reaction == PersonalReaction.Dislike)
            .Select(state => state.VideoId)
            .ToListAsync(cancellationToken);

        return [.. disliked, .. await ActiveDismissalsAsync(accountId, cancellationToken)];
    }

    private async Task<IReadOnlyList<ResurfacedVideo>> WatchAgainAsync(
        Guid accountId,
        string clientContextKey,
        int take,
        IReadOnlyCollection<Guid> exclude,
        CancellationToken cancellationToken)
    {
        var candidates = (await returnInterest.CandidatesAsync(accountId, cancellationToken))
            .Except(exclude)
            .ToArray();

        if (candidates.Length == 0)
        {
            return [];
        }

        // Only what Ordinary Discovery admits for this client, so nothing is offered that cannot
        // be pressed play on. The candidate list is this Account's own history, so it is bounded
        // by what one person has watched rather than by the size of the library.
        var admitted = (await discovery.GetAsync(
            accountId,
            clientContextKey,
            new LibraryDiscoveryRequest
            {
                Videos = candidates,
                Take = LibraryPaging.MaximumPageSize,
            },
            cancellationToken))
            .Videos
            .Select(video => video.Id)
            .ToArray();

        return (await returnInterest.RankAsync(accountId, clientContextKey, admitted, cancellationToken))
            .Where(video => video.Tier > 0 || video.Score > 0)
            .Take(take)
            .Select(video => new ResurfacedVideo(video.VideoId, video.Reasons))
            .ToArray();
    }

    /// <summary>
    /// The cards, loaded once for the whole page. Every section shows the Video the Library shows,
    /// with this Account's Personal State and this client's playback plan on it, which is what
    /// makes a recommendation card the same card as everywhere else.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, VideoSummary>> SummariesAsync(
        Guid accountId,
        string clientContextKey,
        IReadOnlyList<Guid> videoIds,
        CancellationToken cancellationToken) =>
        (await discovery.LoadAsync(accountId, clientContextKey, videoIds, cancellationToken))
            .ToDictionary(video => video.Id);

    private static RecommendationSectionPage Section(
        RecommendationSection section,
        IReadOnlyList<ResurfacedVideo> chosen,
        IReadOnlyDictionary<Guid, VideoSummary> summaries,
        int size) =>
        new(
            section,
            chosen
                .Where(video => summaries.ContainsKey(video.VideoId))
                .Select(video => new RecommendedVideo(summaries[video.VideoId], video.Reasons))
                .ToArray(),
            chosen.Count < size);

    /// <summary>
    /// Drops rows the change tracker is still holding after they have been deleted straight in the
    /// database. Without it, putting the same Video aside again in one scope meets an instance of
    /// a row that no longer exists, and fails on a key conflict with nothing.
    /// </summary>
    private void Forget(Func<RecommendationDismissalRow, bool> deleted)
    {
        foreach (var entry in database.ChangeTracker
                     .Entries<RecommendationDismissalRow>()
                     .Where(entry => deleted(entry.Entity))
                     .ToArray())
        {
            entry.State = EntityState.Detached;
        }
    }

    /// <summary>
    /// The seed a page uses when nobody named one: stable for this Account for the day, so that
    /// rendering, paging and coming back an hour later show the same selection, and different
    /// tomorrow without anybody asking.
    /// </summary>
    private static int DefaultSeed(Guid accountId, DateTimeOffset now) =>
        HashCode.Combine(accountId, now.UtcDateTime.Date);
}
