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
/// Long unseen and Not yet discovered, against a library built for the cases that decide them.
/// </summary>
public sealed class ResurfacingServiceTests
{
    private const string Client = "test-client";

    [Fact]
    public async Task Long_unseen_needs_positive_interest_and_watching_and_is_ordered_by_age()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));
        await using var store = await TestDatabase.CreateAsync(timeProvider: time);
        var seeded = await SeedAsync(store, 5);
        await using var scope = store.Scope();
        var personal = scope.ServiceProvider.GetRequiredService<PersonalStateService>();
        var resurfacing = scope.ServiceProvider.GetRequiredService<ResurfacingService>();

        // 0: watched properly, long ago. 1: watched properly, longer ago. 2: watched properly, but
        // yesterday. 3: sampled for eight seconds long ago. 4: never touched.
        await WatchAsync(personal, seeded, 1, time, TimeSpan.FromMinutes(11));
        time.Advance(TimeSpan.FromDays(40));
        await WatchAsync(personal, seeded, 0, time, TimeSpan.FromMinutes(11));
        time.Advance(TimeSpan.FromDays(40));
        await WatchAsync(personal, seeded, 3, time, TimeSpan.FromSeconds(8));
        time.Advance(TimeSpan.FromDays(40));
        await WatchAsync(personal, seeded, 2, time, TimeSpan.FromMinutes(11));
        time.Advance(TimeSpan.FromDays(1));

        var unseen = await resurfacing.LongUnseenAsync(
            seeded.AccountId,
            Client,
            time.GetUtcNow(),
            take: 10,
            exclude: [],
            TestContext.Current.CancellationToken);

        Assert.Equal(
            [seeded.VideoIds[1], seeded.VideoIds[0]],
            unseen.Select(video => video.VideoId));
        Assert.All(
            unseen,
            video => Assert.Contains(ResurfacingReason.NotWatchedForAWhile, video.Reasons));
    }

    [Fact]
    public async Task Long_unseen_takes_an_explicit_positive_and_leaves_a_video_nobody_watched()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));
        await using var store = await TestDatabase.CreateAsync(timeProvider: time);
        var seeded = await SeedAsync(store, 2);
        await using var scope = store.Scope();
        var personal = scope.ServiceProvider.GetRequiredService<PersonalStateService>();
        var resurfacing = scope.ServiceProvider.GetRequiredService<ResurfacingService>();

        // Loved and watched a long time ago belongs here; loved and never watched does not — it
        // has not been forgotten, it has not been seen.
        await WatchAsync(personal, seeded, 0, time, TimeSpan.FromMinutes(11));
        await personal.SetReactionAsync(
            seeded.AccountId,
            seeded.VideoIds[0],
            PersonalReaction.Love,
            TestContext.Current.CancellationToken);
        await personal.SetReactionAsync(
            seeded.AccountId,
            seeded.VideoIds[1],
            PersonalReaction.Love,
            TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromDays(20));

        var unseen = await resurfacing.LongUnseenAsync(
            seeded.AccountId,
            Client,
            time.GetUtcNow(),
            take: 10,
            exclude: [],
            TestContext.Current.CancellationToken);

        Assert.Equal([seeded.VideoIds[0]], unseen.Select(video => video.VideoId));
    }

    [Fact]
    public async Task Legacy_watching_is_long_unseen_and_says_so_without_naming_a_moment()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));
        await using var store = await TestDatabase.CreateAsync(timeProvider: time);
        var seeded = await SeedAsync(store, 1);
        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var resurfacing = scope.ServiceProvider.GetRequiredService<ResurfacingService>();

        database.PersonalVideoStates.Add(new PersonalVideoStateRow
        {
            AccountId = seeded.AccountId,
            VideoId = seeded.VideoIds[0],
            PlayCount = 6,
            AccumulatedWatchDurationMilliseconds = 4_000_000,
            Reaction = PersonalReaction.Like,
            PlayState = PersonalPlayState.Completed,
            HasViewingCompletion = true,
            UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);

        var unseen = Assert.Single(await resurfacing.LongUnseenAsync(
            seeded.AccountId,
            Client,
            time.GetUtcNow(),
            take: 10,
            exclude: [],
            TestContext.Current.CancellationToken));

        Assert.Equal(seeded.VideoIds[0], unseen.VideoId);
        // It was watched, at a moment nothing here recorded, and the reason says exactly that
        // rather than inventing a number of days.
        Assert.Equal([ResurfacingReason.WatchedLongAgo], unseen.Reasons);
    }

    [Fact]
    public async Task Never_watched_means_no_confirmed_watching_rather_than_an_unplayed_state()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));
        await using var store = await TestDatabase.CreateAsync(timeProvider: time);
        var seeded = await SeedAsync(store, 3);
        await using var scope = store.Scope();
        var personal = scope.ServiceProvider.GetRequiredService<PersonalStateService>();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var resurfacing = scope.ServiceProvider.GetRequiredService<ResurfacingService>();

        // 0 was watched. 1 was attempted and never confirmed a second of playback. 2 carries an
        // older installation's accumulated watching with nothing behind it.
        await WatchAsync(personal, seeded, 0, time, TimeSpan.FromMinutes(11));
        await personal.StartPlaybackAttemptAsync(
            seeded.AccountId,
            seeded.VideoIds[1],
            seeded.VideoFiles[seeded.VideoIds[1]],
            TestContext.Current.CancellationToken);
        database.PersonalVideoStates.Add(new PersonalVideoStateRow
        {
            AccountId = seeded.AccountId,
            VideoId = seeded.VideoIds[2],
            PlayCount = 2,
            AccumulatedWatchDurationMilliseconds = 500_000,
            PlayState = PersonalPlayState.Unplayed,
            UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);

        var discovered = await resurfacing.NeverWatchedAsync(
            seeded.AccountId,
            Client,
            take: 10,
            seed: 1,
            exclude: [],
            TestContext.Current.CancellationToken);

        // A failed attempt leaves eligibility intact; ambiguous legacy evidence does not, because
        // claiming somebody has never watched something they have is the worse mistake.
        Assert.Equal([seeded.VideoIds[1]], discovered.Select(video => video.VideoId));
        Assert.All(
            discovered,
            video => Assert.Contains(ResurfacingReason.NeverWatched, video.Reasons));
    }

    [Fact]
    public async Task Discovery_keeps_a_third_for_choices_nobody_s_taste_made()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));
        await using var store = await TestDatabase.CreateAsync(timeProvider: time);
        var seeded = await SeedAsync(store, 24, withActors: true);
        await using var scope = store.Scope();
        var personal = scope.ServiceProvider.GetRequiredService<PersonalStateService>();
        var resurfacing = scope.ServiceProvider.GetRequiredService<ResurfacingService>();

        // Two Videos with "Alex Doe" watched properly — every even index has them — which is
        // enough for an affinity where one would not have been.
        await WatchAsync(personal, seeded, 0, time, TimeSpan.FromMinutes(11));
        time.Advance(TimeSpan.FromMinutes(31));
        await WatchAsync(personal, seeded, 2, time, TimeSpan.FromMinutes(11));

        var discovered = await resurfacing.NeverWatchedAsync(
            seeded.AccountId,
            Client,
            take: 9,
            seed: 7,
            exclude: [],
            TestContext.Current.CancellationToken);

        Assert.Equal(9, discovered.Count);
        var led = discovered
            .Count(video => video.Reasons.Contains(ResurfacingReason.SharesAnActorYouWatch));
        var independent = discovered
            .Count(video => video.Reasons.Contains(ResurfacingReason.SomethingDifferent));
        Assert.Equal(6, led);
        Assert.Equal(3, independent);
        // Nothing watched is ever offered as a discovery, and nothing appears twice.
        Assert.DoesNotContain(seeded.VideoIds[0], discovered.Select(video => video.VideoId));
        Assert.DoesNotContain(seeded.VideoIds[2], discovered.Select(video => video.VideoId));
        Assert.Equal(
            discovered.Select(video => video.VideoId).Distinct().Count(),
            discovered.Count);
    }

    [Fact]
    public async Task With_no_evidence_at_all_the_whole_page_is_independent_and_stable_for_a_seed()
    {
        await using var store = await TestDatabase.CreateAsync();
        var seeded = await SeedAsync(store, 12, withActors: true);
        await using var scope = store.Scope();
        var resurfacing = scope.ServiceProvider.GetRequiredService<ResurfacingService>();

        var cold = await resurfacing.NeverWatchedAsync(
            seeded.AccountId,
            Client,
            take: 6,
            seed: 3,
            exclude: [],
            TestContext.Current.CancellationToken);

        Assert.Equal(6, cold.Count);
        Assert.All(
            cold,
            video => Assert.Contains(ResurfacingReason.SomethingDifferent, video.Reasons));

        // The same seed is the same page, twice: paging depends on it.
        var again = await resurfacing.NeverWatchedAsync(
            seeded.AccountId,
            Client,
            take: 6,
            seed: 3,
            exclude: [],
            TestContext.Current.CancellationToken);
        Assert.Equal(cold.Select(video => video.VideoId), again.Select(video => video.VideoId));

        // Another seed is another page, which is what Other suggestions is.
        var other = await resurfacing.NeverWatchedAsync(
            seeded.AccountId,
            Client,
            take: 6,
            seed: 4,
            exclude: [],
            TestContext.Current.CancellationToken);
        Assert.NotEqual(cold.Select(video => video.VideoId), other.Select(video => video.VideoId));

        // And what a caller says to leave out stays out.
        var without = await resurfacing.NeverWatchedAsync(
            seeded.AccountId,
            Client,
            take: 6,
            seed: 3,
            exclude: [cold[0].VideoId],
            TestContext.Current.CancellationToken);
        Assert.DoesNotContain(cold[0].VideoId, without.Select(video => video.VideoId));
    }

    [Fact]
    public async Task A_dislike_is_never_held_against_the_actors_or_the_site_of_its_video()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));
        await using var store = await TestDatabase.CreateAsync(timeProvider: time);
        var seeded = await SeedAsync(store, 12, withActors: true);
        await using var scope = store.Scope();
        var personal = scope.ServiceProvider.GetRequiredService<PersonalStateService>();
        var resurfacing = scope.ServiceProvider.GetRequiredService<ResurfacingService>();

        await WatchAsync(personal, seeded, 0, time, TimeSpan.FromMinutes(11));
        time.Advance(TimeSpan.FromMinutes(31));
        await WatchAsync(personal, seeded, 2, time, TimeSpan.FromMinutes(11));
        // Every Video with an even index shares "Alex Doe"; disliking one of them says nothing
        // about the rest.
        await personal.SetReactionAsync(
            seeded.AccountId,
            seeded.VideoIds[4],
            PersonalReaction.Dislike,
            TestContext.Current.CancellationToken);

        var discovered = await resurfacing.NeverWatchedAsync(
            seeded.AccountId,
            Client,
            take: 6,
            seed: 2,
            exclude: [],
            TestContext.Current.CancellationToken);

        Assert.Contains(
            discovered,
            video => video.Reasons.Contains(ResurfacingReason.SharesAnActorYouWatch));
    }

    private static async Task WatchAsync(
        PersonalStateService service,
        SeededIds seeded,
        int index,
        FakeTimeProvider time,
        TimeSpan length)
    {
        var videoId = seeded.VideoIds[index];
        var attemptId = (await service.StartPlaybackAttemptAsync(
            seeded.AccountId,
            videoId,
            seeded.VideoFiles[videoId],
            TestContext.Current.CancellationToken)).PlaybackAttemptId!.Value;
        var stretches = Math.Max(1, (int)(length.TotalSeconds / 10));
        var each = (long)(length.TotalMilliseconds / stretches);

        for (var stretch = 0; stretch < stretches; stretch++)
        {
            time.Advance(TimeSpan.FromMilliseconds(each));
            await service.ReportPlaybackAsync(
                seeded.AccountId,
                attemptId,
                Guid.NewGuid(),
                stretch,
                seeded.VideoFiles[videoId],
                (stretch + 1) * each,
                each,
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

    private static async Task<SeededIds> SeedAsync(
        TestDatabase store,
        int videos,
        bool withActors = false)
    {
        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var now = new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc);
        var accountId = Guid.CreateVersion7();
        var directoryId = Guid.CreateVersion7();
        database.Accounts.Add(new AccountRow
        {
            Id = accountId,
            Username = "watcher",
            NormalizedUsername = "WATCHER",
            PasswordHash = "not-used",
            Authority = AccountAuthority.User,
            State = AccountState.Approved,
            RegisteredAt = now,
            ApprovedAt = now,
        });
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
                // Every third Video has no Site at all, which is the sparse-metadata case the
                // independent third of a discovery page has to be able to reach.
                EstablishedSite = withActors && index % 3 != 0 ? "Known Site" : null,
                Availability = VideoAvailability.Available,
                BestClassification = DirectPlayClassification.BaselineCandidate,
            });
            database.VideoFiles.Add(new VideoFileRow
            {
                Id = videoFileId,
                VideoId = videoId,
                LibraryDirectoryId = directoryId,
                RelativePath = $"video-{index}.mp4",
                Size = 100,
                LastWriteTimeUtc = now,
                Sha256 = index.ToString("x64"),
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

            if (withActors && index % 2 == 0)
            {
                database.VideoActors.Add(new VideoActorRow
                {
                    Id = Guid.CreateVersion7(),
                    VideoId = videoId,
                    Name = "Alex Doe",
                    NormalizedName = "alex doe",
                });
            }
        }

        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new SeededIds(accountId, videoIds, files);
    }

    private sealed record SeededIds(
        Guid AccountId,
        IReadOnlyList<Guid> VideoIds,
        IReadOnlyDictionary<Guid, Guid> VideoFiles);
}
