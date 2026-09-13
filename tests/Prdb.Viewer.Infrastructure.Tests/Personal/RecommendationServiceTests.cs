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
/// The page the three sections make together: what they share, what they must not repeat, and
/// what a reader can do to one of them.
/// </summary>
public sealed class RecommendationServiceTests
{
    private const string Client = "test-client";
    private const string OtherClient = "another-client";

    [Fact]
    public async Task The_three_sections_share_their_exclusions_and_never_repeat_a_video()
    {
        var time = Clock();
        await using var store = await TestDatabase.CreateAsync(timeProvider: time);
        var seeded = await SeedAsync(store, 20);
        await using var scope = store.Scope();
        var personal = scope.ServiceProvider.GetRequiredService<PersonalStateService>();
        var service = scope.ServiceProvider.GetRequiredService<RecommendationService>();

        // Watched a while ago, so it is both a return-interest candidate and long unseen; watched
        // recently, so it is only the first; and one disliked after real watching.
        await WatchAsync(personal, seeded, 0, time, TimeSpan.FromMinutes(11));
        time.Advance(TimeSpan.FromDays(30));
        await WatchAsync(personal, seeded, 1, time, TimeSpan.FromMinutes(11));
        await WatchAsync(personal, seeded, 2, time, TimeSpan.FromMinutes(11));
        await personal.SetReactionAsync(
            seeded.AccountId,
            seeded.VideoIds[2],
            PersonalReaction.Dislike,
            TestContext.Current.CancellationToken);

        var page = await service.GetAsync(
            seeded.AccountId,
            Client,
            seed: 5,
            take: 6,
            cancellationToken: TestContext.Current.CancellationToken);

        var shown = page.Sections.SelectMany(section => section.Videos).ToArray();
        Assert.Equal(
            shown.Select(video => video.Video.Id).Distinct().Count(),
            shown.Length);
        Assert.DoesNotContain(seeded.VideoIds[2], shown.Select(video => video.Video.Id));

        // Long unseen reserves its choice first; return interest is presented first.
        Assert.Equal(
            [
                RecommendationSection.ForYouToWatchAgain,
                RecommendationSection.LongUnseen,
                RecommendationSection.NotYetDiscovered,
            ],
            page.Sections.Select(section => section.Section));
        Assert.Equal(
            [seeded.VideoIds[0]],
            Videos(page, RecommendationSection.LongUnseen));
        Assert.Equal(
            [seeded.VideoIds[1]],
            Videos(page, RecommendationSection.ForYouToWatchAgain));
        Assert.All(
            Videos(page, RecommendationSection.NotYetDiscovered),
            id => Assert.DoesNotContain(id, new[] { seeded.VideoIds[0], seeded.VideoIds[1] }));
        Assert.True(page.HasHistory);
    }

    [Fact]
    public async Task A_dislike_takes_a_video_off_the_page_and_clearing_it_gives_it_back()
    {
        var time = Clock();
        await using var store = await TestDatabase.CreateAsync(timeProvider: time);
        var seeded = await SeedAsync(store, 4);
        await using var scope = store.Scope();
        var personal = scope.ServiceProvider.GetRequiredService<PersonalStateService>();
        var service = scope.ServiceProvider.GetRequiredService<RecommendationService>();

        await WatchAsync(personal, seeded, 0, time, TimeSpan.FromMinutes(11));

        Assert.Contains(
            seeded.VideoIds[0],
            All(await service.GetAsync(
                seeded.AccountId,
                Client,
                1,
                6,
                TestContext.Current.CancellationToken)));

        await personal.SetReactionAsync(
            seeded.AccountId,
            seeded.VideoIds[0],
            PersonalReaction.Dislike,
            TestContext.Current.CancellationToken);
        Assert.DoesNotContain(
            seeded.VideoIds[0],
            All(await service.GetAsync(
                seeded.AccountId,
                Client,
                1,
                6,
                TestContext.Current.CancellationToken)));

        // A Shrug is not a Dislike, so the Video comes back the moment the reaction changes.
        await personal.SetReactionAsync(
            seeded.AccountId,
            seeded.VideoIds[0],
            PersonalReaction.Shrug,
            TestContext.Current.CancellationToken);
        Assert.Contains(
            seeded.VideoIds[0],
            All(await service.GetAsync(
                seeded.AccountId,
                Client,
                1,
                6,
                TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Not_today_lasts_a_day_across_clients_and_can_be_taken_back()
    {
        var time = Clock();
        await using var store = await TestDatabase.CreateAsync(timeProvider: time);
        var seeded = await SeedAsync(store, 4);
        await using var scope = store.Scope();
        var personal = scope.ServiceProvider.GetRequiredService<PersonalStateService>();
        var service = scope.ServiceProvider.GetRequiredService<RecommendationService>();

        await WatchAsync(personal, seeded, 0, time, TimeSpan.FromMinutes(11));
        var before = await personal.GetSummaryAsync(
            seeded.AccountId,
            seeded.VideoIds[0],
            TestContext.Current.CancellationToken);

        Assert.Equal(
            DismissalVerdict.Updated,
            await service.DismissAsync(
                seeded.AccountId,
                seeded.VideoIds[0],
                TestContext.Current.CancellationToken));

        Assert.DoesNotContain(seeded.VideoIds[0], All(await PageAsync(service, seeded, Client)));
        // Across this Account's clients, because it is the Account that said not today.
        Assert.DoesNotContain(seeded.VideoIds[0], All(await PageAsync(service, seeded, OtherClient)));

        // It changes no preference and no playback state.
        var after = await personal.GetSummaryAsync(
            seeded.AccountId,
            seeded.VideoIds[0],
            TestContext.Current.CancellationToken);
        Assert.Equal(before, after);

        // Just short of the boundary it still holds; just past it, it is gone.
        time.Advance(TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59));
        Assert.DoesNotContain(seeded.VideoIds[0], All(await PageAsync(service, seeded, Client)));
        time.Advance(TimeSpan.FromMinutes(2));
        Assert.Contains(seeded.VideoIds[0], All(await PageAsync(service, seeded, Client)));

        // And it can be taken back before then.
        await service.DismissAsync(
            seeded.AccountId,
            seeded.VideoIds[0],
            TestContext.Current.CancellationToken);
        Assert.DoesNotContain(seeded.VideoIds[0], All(await PageAsync(service, seeded, Client)));
        await service.UndoDismissalAsync(
            seeded.AccountId,
            seeded.VideoIds[0],
            TestContext.Current.CancellationToken);
        Assert.Contains(seeded.VideoIds[0], All(await PageAsync(service, seeded, Client)));
    }

    [Fact]
    public async Task A_page_is_the_same_page_twice_and_another_seed_is_another_page()
    {
        var time = Clock();
        await using var store = await TestDatabase.CreateAsync(timeProvider: time);
        var seeded = await SeedAsync(store, 30);
        await using var scope = store.Scope();
        var service = scope.ServiceProvider.GetRequiredService<RecommendationService>();

        var first = await service.GetAsync(
            seeded.AccountId,
            Client,
            seed: 11,
            take: 6,
            cancellationToken: TestContext.Current.CancellationToken);
        var again = await service.GetAsync(
            seeded.AccountId,
            Client,
            seed: 11,
            take: 6,
            cancellationToken: TestContext.Current.CancellationToken);
        var other = await service.GetAsync(
            seeded.AccountId,
            Client,
            seed: 12,
            take: 6,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(All(first), All(again));
        Assert.NotEqual(All(first), All(other));
        Assert.Equal(11, first.Seed);
        Assert.Equal(12, other.Seed);

        // Without a seed the page answers the one it chose, so a screen can hold on to it.
        var chosen = await service.GetAsync(
            seeded.AccountId,
            Client,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(
            All(chosen),
            All(await service.GetAsync(
                seeded.AccountId,
                Client,
                chosen.Seed,
                cancellationToken: TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task A_cold_start_is_honest_discovery_rather_than_an_invented_preference()
    {
        var time = Clock();
        await using var store = await TestDatabase.CreateAsync(timeProvider: time);
        var seeded = await SeedAsync(store, 8);
        await using var scope = store.Scope();
        var service = scope.ServiceProvider.GetRequiredService<RecommendationService>();

        var page = await service.GetAsync(
            seeded.AccountId,
            Client,
            seed: 3,
            take: 6,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(page.HasHistory);
        Assert.Empty(Videos(page, RecommendationSection.ForYouToWatchAgain));
        Assert.Empty(Videos(page, RecommendationSection.LongUnseen));
        Assert.NotEmpty(Videos(page, RecommendationSection.NotYetDiscovered));
        // A section with nothing further to offer says so rather than looking broken.
        Assert.True(page.Sections
            .Single(section => section.Section == RecommendationSection.ForYouToWatchAgain)
            .Exhausted);
    }

    [Fact]
    public async Task A_tiny_library_says_it_has_run_out_rather_than_manufacturing_candidates()
    {
        var time = Clock();
        await using var store = await TestDatabase.CreateAsync(timeProvider: time);
        var seeded = await SeedAsync(store, 2);
        await using var scope = store.Scope();
        var service = scope.ServiceProvider.GetRequiredService<RecommendationService>();

        var page = await service.GetAsync(
            seeded.AccountId,
            Client,
            seed: 1,
            take: 12,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, All(page).Count);
        Assert.All(page.Sections, section => Assert.True(section.Exhausted));
    }

    [Fact]
    public async Task One_account_never_sees_another_s_recommendations_or_what_it_put_aside()
    {
        var time = Clock();
        await using var store = await TestDatabase.CreateAsync(timeProvider: time);
        var seeded = await SeedAsync(store, 6, second: true);
        await using var scope = store.Scope();
        var personal = scope.ServiceProvider.GetRequiredService<PersonalStateService>();
        var service = scope.ServiceProvider.GetRequiredService<RecommendationService>();

        await WatchAsync(personal, seeded, 0, time, TimeSpan.FromMinutes(11));
        await service.DismissAsync(
            seeded.AccountId,
            seeded.VideoIds[1],
            TestContext.Current.CancellationToken);

        var theirs = await service.GetAsync(
            seeded.SecondAccountId!.Value,
            Client,
            seed: 1,
            take: 6,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(theirs.HasHistory);
        Assert.Empty(Videos(theirs, RecommendationSection.ForYouToWatchAgain));
        // The other Account's dismissal keeps nothing from this one.
        Assert.Contains(seeded.VideoIds[1], All(theirs));
        Assert.Empty(await service.ActiveDismissalsAsync(
            seeded.SecondAccountId.Value,
            TestContext.Current.CancellationToken));
    }

    private static FakeTimeProvider Clock() =>
        new(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));

    private static Task<RecommendationPage> PageAsync(
        RecommendationService service,
        SeededIds seeded,
        string client) =>
        service.GetAsync(seeded.AccountId, client, 1, 6, TestContext.Current.CancellationToken);

    private static IReadOnlyList<Guid> All(RecommendationPage page) =>
        page.Sections.SelectMany(section => section.Videos.Select(video => video.Video.Id)).ToArray();

    private static IReadOnlyList<Guid> Videos(RecommendationPage page, RecommendationSection section) =>
        page.Sections
            .Single(candidate => candidate.Section == section)
            .Videos
            .Select(video => video.Video.Id)
            .ToArray();

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

    private static async Task<SeededIds> SeedAsync(TestDatabase store, int videos, bool second = false)
    {
        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var now = new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc);
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
