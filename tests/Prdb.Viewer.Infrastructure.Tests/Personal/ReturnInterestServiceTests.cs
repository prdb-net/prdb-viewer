using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Prdb.Viewer.Core.Access;
using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Core.Personal;
using Prdb.Viewer.Infrastructure.Persistence;
using Prdb.Viewer.Infrastructure.Personal;
using Prdb.Viewer.Infrastructure.Tests.Library;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Personal;

/// <summary>
/// The ranking read from an Account's actual Personal State. The rules themselves are held to
/// their examples in the Core tests; what is asked here is that the right evidence reaches them.
/// </summary>
public sealed class ReturnInterestServiceTests
{
    private const string Client = "test-client";

    [Fact]
    public async Task Watching_reactions_playlists_and_this_visit_all_reach_the_ranking()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));
        await using var store = await TestDatabase.CreateAsync(timeProvider: time);
        var seeded = await SeedAsync(store, 4);
        await using var scope = store.Scope();
        var personal = scope.ServiceProvider.GetRequiredService<PersonalStateService>();
        var playlists = scope.ServiceProvider.GetRequiredService<PlaylistService>();
        var ranking = scope.ServiceProvider.GetRequiredService<ReturnInterestService>();

        // One Video is loved and never watched; one was watched properly three times; one is only
        // in a Playlist; one was disliked after hours of watching.
        await personal.SetReactionAsync(
            seeded.AccountId,
            seeded.VideoIds[0],
            PersonalReaction.Love,
            TestContext.Current.CancellationToken);

        for (var visit = 0; visit < 3; visit++)
        {
            await WatchAsync(personal, seeded, seeded.VideoIds[1], time, TimeSpan.FromMinutes(3));
            time.Advance(TimeSpan.FromMinutes(31));
        }

        var playlistId = (await playlists.CreateAsync(
            seeded.AccountId,
            "Kept",
            TestContext.Current.CancellationToken)).Playlist!.Id;
        await playlists.AddAsync(
            seeded.AccountId,
            playlistId,
            seeded.VideoIds[2],
            TestContext.Current.CancellationToken);

        await WatchAsync(personal, seeded, seeded.VideoIds[3], time, TimeSpan.FromHours(1));
        await personal.SetReactionAsync(
            seeded.AccountId,
            seeded.VideoIds[3],
            PersonalReaction.Dislike,
            TestContext.Current.CancellationToken);

        var candidates = await ranking.CandidatesAsync(
            seeded.AccountId,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            [seeded.VideoIds[0], seeded.VideoIds[1], seeded.VideoIds[2]],
            candidates.OrderBy(id => seeded.VideoIds.ToList().IndexOf(id)));
        Assert.DoesNotContain(seeded.VideoIds[3], candidates);

        var ranked = await ranking.RankAsync(
            seeded.AccountId,
            Client,
            seeded.VideoIds,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            [seeded.VideoIds[0], seeded.VideoIds[1], seeded.VideoIds[2]],
            ranked.Where(video => !DislikedIn(video, seeded)).Take(3).Select(video => video.VideoId));
        Assert.Contains(RecommendationReason.Loved, ranked[0].Reasons);
        Assert.Contains(RecommendationReason.WatchedRepeatedly, ranked[1].Reasons);
        Assert.Contains(RecommendationReason.InAPlaylist, ranked[2].Reasons);
        // The disliked Video comes back scoring nothing rather than being silently dropped: what
        // to do about an excluded Video is the caller's question.
        var disliked = Assert.Single(ranked, video => video.VideoId == seeded.VideoIds[3]);
        Assert.Equal(0, disliked.Score);
        Assert.Empty(disliked.Reasons);
    }

    [Fact]
    public async Task What_was_just_watched_moves_down_within_the_visit_and_returns_after_it()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));
        await using var store = await TestDatabase.CreateAsync(timeProvider: time);
        var seeded = await SeedAsync(store, 2);
        await using var scope = store.Scope();
        var personal = scope.ServiceProvider.GetRequiredService<PersonalStateService>();
        var ranking = scope.ServiceProvider.GetRequiredService<ReturnInterestService>();

        // The first is the stronger favourite by evidence; the second is watched right now.
        await WatchAsync(personal, seeded, seeded.VideoIds[0], time, TimeSpan.FromMinutes(11));
        time.Advance(TimeSpan.FromMinutes(31));
        await WatchAsync(personal, seeded, seeded.VideoIds[1], time, TimeSpan.FromMinutes(11));

        var duringVisit = await ranking.RankAsync(
            seeded.AccountId,
            Client,
            seeded.VideoIds,
            TestContext.Current.CancellationToken);
        Assert.Equal(seeded.VideoIds[0], duringVisit[0].VideoId);
        Assert.Contains(RecommendationReason.JustWatchedInThisVisit, duringVisit[1].Reasons);
        // Down, never out.
        Assert.True(duringVisit[1].Score > 0);

        // A later visit reads the same evidence without the adjustment, and the Video watched most
        // recently leads on the strength of that.
        time.Advance(TimeSpan.FromMinutes(31));
        var laterVisit = await ranking.RankAsync(
            seeded.AccountId,
            Client,
            seeded.VideoIds,
            TestContext.Current.CancellationToken);
        Assert.Equal(seeded.VideoIds[1], laterVisit[0].VideoId);
        Assert.DoesNotContain(
            RecommendationReason.JustWatchedInThisVisit,
            laterVisit[0].Reasons);
    }

    [Fact]
    public async Task Legacy_watching_is_a_candidate_that_claims_no_sessions_it_cannot_show()
    {
        await using var store = await TestDatabase.CreateAsync();
        var seeded = await SeedAsync(store, 1);
        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var ranking = scope.ServiceProvider.GetRequiredService<ReturnInterestService>();

        // What an installation restored from before per-session evidence looks like: an aggregate
        // and a Play Count, with no Playback Attempts behind them.
        database.PersonalVideoStates.Add(new PersonalVideoStateRow
        {
            AccountId = seeded.AccountId,
            VideoId = seeded.VideoIds[0],
            PlayCount = 4,
            AccumulatedWatchDurationMilliseconds = 3_000_000,
            PlayState = PersonalPlayState.Completed,
            HasViewingCompletion = true,
            UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Contains(
            seeded.VideoIds[0],
            await ranking.CandidatesAsync(seeded.AccountId, TestContext.Current.CancellationToken));

        var ranked = Assert.Single(await ranking.RankAsync(
            seeded.AccountId,
            Client,
            seeded.VideoIds,
            TestContext.Current.CancellationToken));
        Assert.Contains(RecommendationReason.WatchedBefore, ranked.Reasons);
        Assert.DoesNotContain(RecommendationReason.WatchedRepeatedly, ranked.Reasons);
        Assert.DoesNotContain(RecommendationReason.WatchedAtLength, ranked.Reasons);
        Assert.Equal(0, ranked.Score);
    }

    [Fact]
    public async Task One_account_is_ranked_from_its_own_state_and_nobody_else_s()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));
        await using var store = await TestDatabase.CreateAsync(timeProvider: time);
        var seeded = await SeedAsync(store, 1, second: true);
        await using var scope = store.Scope();
        var personal = scope.ServiceProvider.GetRequiredService<PersonalStateService>();
        var ranking = scope.ServiceProvider.GetRequiredService<ReturnInterestService>();

        await WatchAsync(personal, seeded, seeded.VideoIds[0], time, TimeSpan.FromMinutes(11));
        await personal.SetReactionAsync(
            seeded.AccountId,
            seeded.VideoIds[0],
            PersonalReaction.Love,
            TestContext.Current.CancellationToken);

        Assert.Empty(await ranking.CandidatesAsync(
            seeded.SecondAccountId!.Value,
            TestContext.Current.CancellationToken));
        var theirs = Assert.Single(await ranking.RankAsync(
            seeded.SecondAccountId.Value,
            Client,
            seeded.VideoIds,
            TestContext.Current.CancellationToken));
        Assert.Equal(0, theirs.Tier);
        Assert.Equal(0, theirs.Score);
        Assert.Empty(theirs.Reasons);
    }

    private static bool DislikedIn(RankedVideo video, SeededIds seeded) =>
        video.VideoId == seeded.VideoIds[3];

    /// <summary>One Viewing Session of the given length, reported in ten-second stretches.</summary>
    private static async Task WatchAsync(
        PersonalStateService service,
        SeededIds seeded,
        Guid videoId,
        FakeTimeProvider time,
        TimeSpan length)
    {
        var attemptId = (await service.StartPlaybackAttemptAsync(
            seeded.AccountId,
            videoId,
            seeded.VideoFiles[videoId],
            TestContext.Current.CancellationToken)).PlaybackAttemptId!.Value;
        var stretches = (int)(length.TotalSeconds / 10);

        for (var index = 0; index < stretches; index++)
        {
            time.Advance(TimeSpan.FromSeconds(10));
            await service.ReportPlaybackAsync(
                seeded.AccountId,
                attemptId,
                Guid.NewGuid(),
                index,
                seeded.VideoFiles[videoId],
                (index + 1) * 10_000,
                10_000,
                naturalEndConfirmed: false,
                endSession: false,
                Client,
                TestContext.Current.CancellationToken);
        }

        await service.EndPlaybackAttemptAsync(
            seeded.AccountId,
            attemptId,
            PlaybackDeparture.Closed,
            TestContext.Current.CancellationToken);
    }

    private static async Task<SeededIds> SeedAsync(TestDatabase store, int videos, bool second = false)
    {
        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var now = new DateTime(2026, 9, 13, 11, 0, 0, DateTimeKind.Utc);
        var accountId = Guid.CreateVersion7();
        var secondAccountId = second ? Guid.CreateVersion7() : (Guid?)null;
        var directoryId = Guid.CreateVersion7();
        database.Accounts.Add(Account(accountId, "watcher", now));

        if (secondAccountId is { } other)
        {
            database.Accounts.Add(Account(other, "another", now));
        }

        database.LibraryDirectories.Add(new LibraryDirectoryRow
        {
            Id = directoryId,
            Name = "Main Library",
            ContainerPath = store.LibraryMountRoot.Path,
            State = LibraryDirectoryState.Active,
            Health = LibraryDirectoryHealth.Healthy,
            ConfigurationGeneration = 1,
            CreatedAt = now,
            ActivatedAt = now,
        });

        var videoIds = new List<Guid>();
        var files = new Dictionary<Guid, Guid>();

        for (var index = 0; index < videos; index++)
        {
            var videoId = Guid.CreateVersion7();
            var videoFileId = Guid.CreateVersion7();
            videoIds.Add(videoId);
            files[videoId] = videoFileId;
            database.Videos.Add(new VideoRow
            {
                Id = videoId,
                DiscoveryDate = now.AddMinutes(index),
                DisplayLabel = $"video {index}",
                SearchText = $"video {index}",
            });
            database.VideoFiles.Add(new VideoFileRow
            {
                Id = videoFileId,
                VideoId = videoId,
                LibraryDirectoryId = directoryId,
                RelativePath = $"video-{index}.mp4",
                Size = 100,
                LastWriteTimeUtc = now,
                Sha256 = new string((char)('a' + index), 64),
                PublicDeliveryId = Guid.NewGuid(),
                ContainerFormat = "mp4",
                VideoCodec = "h264",
                AudioCodec = "aac",
                DurationMilliseconds = 7_200_000,
                Width = 640,
                Height = 360,
                Availability = VideoFileAvailability.Available,
                DirectPlayClassification = DirectPlayClassification.BaselineCandidate,
                LastObservedScanId = Guid.CreateVersion7(),
                InspectedAt = now,
            });
        }

        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new SeededIds(accountId, secondAccountId, videoIds, files);
    }

    private static AccountRow Account(Guid id, string username, DateTime now) => new()
    {
        Id = id,
        Username = username,
        NormalizedUsername = username.ToUpperInvariant(),
        PasswordHash = "not-used",
        Authority = AccountAuthority.User,
        State = AccountState.Approved,
        RegisteredAt = now,
        ApprovedAt = now,
    };

    private sealed record SeededIds(
        Guid AccountId,
        Guid? SecondAccountId,
        IReadOnlyList<Guid> VideoIds,
        IReadOnlyDictionary<Guid, Guid> VideoFiles);
}
