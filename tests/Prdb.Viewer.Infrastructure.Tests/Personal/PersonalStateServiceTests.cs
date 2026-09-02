using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Prdb.Viewer.Core.Access;
using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Core.Personal;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;
using Prdb.Viewer.Infrastructure.Personal;
using Prdb.Viewer.Infrastructure.Tests.Library;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Personal;

public sealed class PersonalStateServiceTests
{
    [Fact]
    public async Task Playback_is_idempotent_private_resumable_and_completion_aware()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 27, 20, 0, 0, TimeSpan.Zero));
        await using var store = await TestDatabase.CreateAsync(timeProvider: time);
        var seeded = await SeedAsync(store);

        await using var scope = store.Scope();
        var service = scope.ServiceProvider.GetRequiredService<PersonalStateService>();
        var attempt = await service.StartPlaybackAttemptAsync(
            seeded.FirstAccountId,
            seeded.VideoId,
            seeded.VideoFileId,
            TestContext.Current.CancellationToken);
        Assert.Equal(PlaybackAttemptVerdict.Started, attempt.Verdict);
        Assert.Null(attempt.ResumePositionMilliseconds);

        time.Advance(TimeSpan.FromSeconds(10));
        var reportId = Guid.NewGuid();
        var report = await service.ReportPlaybackAsync(
            seeded.FirstAccountId,
            attempt.PlaybackAttemptId!.Value,
            reportId,
            sequence: 0,
            seeded.VideoFileId,
            positionMilliseconds: 10_000,
            activeWatchingMilliseconds: 10_000,
            naturalEndConfirmed: false,
            endSession: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(PlaybackReportVerdict.Accepted, report.Verdict);
        Assert.Equal(PersonalPlayState.InProgress, report.PersonalState!.PlayState);
        Assert.True(report.PersonalState.ContinueWatching);
        Assert.Equal(10_000, report.PersonalState.PlaybackProgressMilliseconds);
        Assert.Equal(10_000, report.PersonalState.AccumulatedWatchDurationMilliseconds);
        Assert.Equal(1, report.PersonalState.PlayCount);

        var duplicate = await service.ReportPlaybackAsync(
            seeded.FirstAccountId,
            attempt.PlaybackAttemptId.Value,
            reportId,
            sequence: 0,
            seeded.VideoFileId,
            positionMilliseconds: 10_000,
            activeWatchingMilliseconds: 10_000,
            naturalEndConfirmed: false,
            endSession: false,
            TestContext.Current.CancellationToken);
        Assert.Equal(PlaybackReportVerdict.Duplicate, duplicate.Verdict);
        Assert.Equal(10_000, duplicate.PersonalState!.AccumulatedWatchDurationMilliseconds);
        Assert.Equal(1, duplicate.PersonalState.PlayCount);

        var otherAccount = await service.GetSummaryAsync(
            seeded.SecondAccountId,
            seeded.VideoId,
            TestContext.Current.CancellationToken);
        Assert.Equal(PersonalPlayState.Unplayed, otherAccount.PlayState);
        Assert.Equal(0, otherAccount.AccumulatedWatchDurationMilliseconds);

        var resumed = await service.StartPlaybackAttemptAsync(
            seeded.FirstAccountId,
            seeded.VideoId,
            seeded.VideoFileId,
            TestContext.Current.CancellationToken);
        Assert.Equal(10_000, resumed.ResumePositionMilliseconds);

        time.Advance(TimeSpan.FromSeconds(1));
        var completed = await service.ReportPlaybackAsync(
            seeded.FirstAccountId,
            resumed.PlaybackAttemptId!.Value,
            Guid.NewGuid(),
            sequence: 0,
            seeded.VideoFileId,
            positionMilliseconds: 90_000,
            activeWatchingMilliseconds: 1_000,
            naturalEndConfirmed: false,
            endSession: false,
            TestContext.Current.CancellationToken);
        Assert.Equal(PersonalPlayState.Completed, completed.PersonalState!.PlayState);
        Assert.True(completed.PersonalState.HasViewingCompletion);
        Assert.False(completed.PersonalState.ContinueWatching);
        Assert.Equal(1, completed.PersonalState.PlayCount);

        var afterCompletion = await service.ReportPlaybackAsync(
            seeded.FirstAccountId,
            resumed.PlaybackAttemptId.Value,
            Guid.NewGuid(),
            sequence: 1,
            seeded.VideoFileId,
            positionMilliseconds: 91_000,
            activeWatchingMilliseconds: 1_000,
            naturalEndConfirmed: false,
            endSession: false,
            TestContext.Current.CancellationToken);
        Assert.Equal(PlaybackReportVerdict.Accepted, afterCompletion.Verdict);
        Assert.Equal(PersonalPlayState.Completed, afterCompletion.PersonalState!.PlayState);
    }

    [Fact]
    public async Task Concurrent_activity_counts_once_but_each_session_can_qualify()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 27, 20, 0, 0, TimeSpan.Zero));
        await using var store = await TestDatabase.CreateAsync(timeProvider: time);
        var seeded = await SeedAsync(store);
        await using var scope = store.Scope();
        var service = scope.ServiceProvider.GetRequiredService<PersonalStateService>();
        var first = await service.StartPlaybackAttemptAsync(
            seeded.FirstAccountId,
            seeded.VideoId,
            seeded.VideoFileId,
            TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromMilliseconds(1));
        var second = await service.StartPlaybackAttemptAsync(
            seeded.FirstAccountId,
            seeded.VideoId,
            seeded.VideoFileId,
            TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromSeconds(10));

        await service.ReportPlaybackAsync(
            seeded.FirstAccountId,
            first.PlaybackAttemptId!.Value,
            Guid.NewGuid(),
            0,
            seeded.VideoFileId,
            10_000,
            10_000,
            false,
            false,
            TestContext.Current.CancellationToken);
        var result = await service.ReportPlaybackAsync(
            seeded.FirstAccountId,
            second.PlaybackAttemptId!.Value,
            Guid.NewGuid(),
            0,
            seeded.VideoFileId,
            25_000,
            10_000,
            false,
            false,
            TestContext.Current.CancellationToken);

        Assert.Equal(10_000, result.PersonalState!.AccumulatedWatchDurationMilliseconds);
        Assert.Equal(2, result.PersonalState.PlayCount);
        Assert.Equal(25_000, result.PersonalState.PlaybackProgressMilliseconds);

        time.Advance(TimeSpan.FromSeconds(1));
        var olderLateReport = await service.ReportPlaybackAsync(
            seeded.FirstAccountId,
            first.PlaybackAttemptId.Value,
            Guid.NewGuid(),
            1,
            seeded.VideoFileId,
            50_000,
            1_000,
            false,
            false,
            TestContext.Current.CancellationToken);
        Assert.Equal(25_000, olderLateReport.PersonalState!.PlaybackProgressMilliseconds);
    }

    [Fact]
    public async Task Personal_choices_are_idempotent_ordered_and_private()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 27, 20, 0, 0, TimeSpan.Zero));
        await using var store = await TestDatabase.CreateAsync(timeProvider: time);
        var seeded = await SeedAsync(store);
        await using var scope = store.Scope();
        var service = scope.ServiceProvider.GetRequiredService<PersonalStateService>();
        var discovery = scope.ServiceProvider.GetRequiredService<LibraryDiscovery>();

        var favourite = await service.SetFavouriteAsync(
            seeded.FirstAccountId,
            seeded.VideoId,
            selected: true,
            TestContext.Current.CancellationToken);
        Assert.True(favourite.PersonalState!.Favourite);
        await service.SetFavouriteAsync(
            seeded.FirstAccountId,
            seeded.VideoId,
            selected: true,
            TestContext.Current.CancellationToken);
        var watchLater = await service.SetWatchLaterAsync(
            seeded.FirstAccountId,
            seeded.VideoId,
            selected: true,
            TestContext.Current.CancellationToken);
        Assert.True(watchLater.PersonalState!.WatchLater);
        var rated = await service.SetRatingAsync(
            seeded.FirstAccountId,
            seeded.VideoId,
            5,
            TestContext.Current.CancellationToken);
        Assert.Equal(5, rated.PersonalState!.PersonalRating);
        Assert.Equal(
            PersonalStateMutationVerdict.InvalidRating,
            (await service.SetRatingAsync(
                seeded.FirstAccountId,
                seeded.VideoId,
                6,
                TestContext.Current.CancellationToken)).Verdict);

        Assert.Single((await OnShelfAsync(discovery, seeded.FirstAccountId, PersonalShelf.Favourites)).Videos);
        Assert.Single((await OnShelfAsync(discovery, seeded.FirstAccountId, PersonalShelf.WatchLater)).Videos);
        Assert.Empty((await OnShelfAsync(discovery, seeded.SecondAccountId, PersonalShelf.Favourites)).Videos);
        Assert.Empty((await OnShelfAsync(discovery, seeded.SecondAccountId, PersonalShelf.WatchLater)).Videos);
    }

    [Fact]
    public async Task Dismissal_inactivity_and_variant_changes_preserve_confirmed_state_without_guessing()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 27, 20, 0, 0, TimeSpan.Zero));
        await using var store = await TestDatabase.CreateAsync(timeProvider: time);
        var seeded = await SeedAsync(store);
        Guid otherVideoFileId;
        await using (var seedScope = store.Scope())
        {
            var database = seedScope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            var original = await database.VideoFiles.SingleAsync(
                file => file.Id == seeded.VideoFileId,
                TestContext.Current.CancellationToken);
            otherVideoFileId = Guid.CreateVersion7();
            database.VideoFiles.Add(new VideoFileRow
            {
                Id = otherVideoFileId,
                VideoId = original.VideoId,
                LibraryDirectoryId = original.LibraryDirectoryId,
                RelativePath = "different-cut.mp4",
                Size = 200,
                LastWriteTimeUtc = original.LastWriteTimeUtc,
                Sha256 = new string('C', 64),
                PublicDeliveryId = Guid.NewGuid(),
                ContainerFormat = "mp4",
                VideoCodec = "h264",
                AudioCodec = "aac",
                DurationMilliseconds = 120_000,
                Width = 640,
                Height = 360,
                Availability = VideoFileAvailability.Available,
                DirectPlayClassification = DirectPlayClassification.BaselineCandidate,
                LastObservedScanId = Guid.CreateVersion7(),
                InspectedAt = original.InspectedAt,
            });
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var scope = store.Scope();
        var service = scope.ServiceProvider.GetRequiredService<PersonalStateService>();
        var attempt = await service.StartPlaybackAttemptAsync(
            seeded.FirstAccountId,
            seeded.VideoId,
            seeded.VideoFileId,
            TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromSeconds(10));
        await service.ReportPlaybackAsync(
            seeded.FirstAccountId,
            attempt.PlaybackAttemptId!.Value,
            Guid.NewGuid(),
            0,
            seeded.VideoFileId,
            10_000,
            10_000,
            false,
            false,
            TestContext.Current.CancellationToken);

        Assert.False((await service.DismissContinueWatchingAsync(
            seeded.FirstAccountId,
            seeded.VideoId,
            TestContext.Current.CancellationToken)).PersonalState!.ContinueWatching);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True((await service.ReportPlaybackAsync(
            seeded.FirstAccountId,
            attempt.PlaybackAttemptId.Value,
            Guid.NewGuid(),
            1,
            seeded.VideoFileId,
            11_000,
            1_000,
            false,
            false,
            TestContext.Current.CancellationToken)).PersonalState!.ContinueWatching);

        var differentCut = await service.StartPlaybackAttemptAsync(
            seeded.FirstAccountId,
            seeded.VideoId,
            otherVideoFileId,
            TestContext.Current.CancellationToken);
        Assert.Null(differentCut.ResumePositionMilliseconds);

        time.Advance(TimeSpan.FromMinutes(31));
        var expired = await service.ReportPlaybackAsync(
            seeded.FirstAccountId,
            differentCut.PlaybackAttemptId!.Value,
            Guid.NewGuid(),
            0,
            otherVideoFileId,
            1_000,
            1_000,
            false,
            false,
            TestContext.Current.CancellationToken);
        Assert.Equal(PlaybackReportVerdict.AttemptEnded, expired.Verdict);
        Assert.Equal(11_000, expired.PersonalState!.PlaybackProgressMilliseconds);
    }

    [Fact]
    public async Task Personal_references_survive_unavailability_and_are_dormant_while_removed()
    {
        await using var store = await TestDatabase.CreateAsync();
        var seeded = await SeedAsync(store);
        await using var scope = store.Scope();
        var service = scope.ServiceProvider.GetRequiredService<PersonalStateService>();
        var discovery = scope.ServiceProvider.GetRequiredService<LibraryDiscovery>();
        await service.SetFavouriteAsync(
            seeded.FirstAccountId,
            seeded.VideoId,
            true,
            TestContext.Current.CancellationToken);

        await SetAvailabilityAsync(scope, seeded.VideoFileId, VideoFileAvailability.Missing);
        var unavailable = Assert.Single(
            (await OnShelfAsync(discovery, seeded.FirstAccountId, PersonalShelf.Favourites)).Videos);
        Assert.Equal(VideoAvailability.Unavailable, unavailable.Availability);
        Assert.Empty(unavailable.VideoFiles);

        await SetAvailabilityAsync(scope, seeded.VideoFileId, VideoFileAvailability.Removed);
        Assert.Empty((await OnShelfAsync(discovery, seeded.FirstAccountId, PersonalShelf.Favourites)).Videos);

        await SetAvailabilityAsync(scope, seeded.VideoFileId, VideoFileAvailability.Available);
        Assert.Single((await OnShelfAsync(discovery, seeded.FirstAccountId, PersonalShelf.Favourites)).Videos);
    }

    /// <summary>A Personal Shelf, read the way the browser reads it: as the Library narrowed to it.</summary>
    private static Task<LibraryPage> OnShelfAsync(
        LibraryDiscovery discovery,
        Guid accountId,
        PersonalShelf shelf) =>
        discovery.GetAsync(
            accountId,
            LibraryPipeline.ClientContext,
            new LibraryDiscoveryRequest { Shelf = [shelf] },
            TestContext.Current.CancellationToken);

    private static async Task SetAvailabilityAsync(
        AsyncServiceScope scope,
        Guid videoFileId,
        VideoFileAvailability availability)
    {
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var file = await database.VideoFiles.AsTracking().SingleAsync(
            candidate => candidate.Id == videoFileId,
            TestContext.Current.CancellationToken);
        file.Availability = availability;
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        // Whatever changes a file's availability refreshes the Video's projection in the same unit
        // of work (ADR 0013); the shelf reads the projection, so this stand-in for a scan does too.
        await scope.ServiceProvider
            .GetRequiredService<VideoProjection>()
            .RefreshAsync(file.VideoId, TestContext.Current.CancellationToken);
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        database.ChangeTracker.Clear();
    }

    private static async Task<SeededIds> SeedAsync(TestDatabase store)
    {
        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var now = new DateTime(2026, 8, 27, 20, 0, 0, DateTimeKind.Utc);
        var firstAccountId = Guid.CreateVersion7();
        var secondAccountId = Guid.CreateVersion7();
        var directoryId = Guid.CreateVersion7();
        var videoId = Guid.CreateVersion7();
        var videoFileId = Guid.CreateVersion7();
        database.Accounts.AddRange(
            Account(firstAccountId, "first", now),
            Account(secondAccountId, "second", now));
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
        database.Videos.Add(new VideoRow { Id = videoId, DiscoveryDate = now });
        database.VideoFiles.Add(new VideoFileRow
        {
            Id = videoFileId,
            VideoId = videoId,
            LibraryDirectoryId = directoryId,
            RelativePath = "sample.mp4",
            Size = 100,
            LastWriteTimeUtc = now,
            Sha256 = new string('A', 64),
            PublicDeliveryId = Guid.NewGuid(),
            ContainerFormat = "mp4",
            VideoCodec = "h264",
            AudioCodec = "aac",
            DurationMilliseconds = 100_000,
            Width = 640,
            Height = 360,
            Availability = VideoFileAvailability.Available,
            DirectPlayClassification = DirectPlayClassification.BaselineCandidate,
            LastObservedScanId = Guid.CreateVersion7(),
            InspectedAt = now,
        });
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new SeededIds(firstAccountId, secondAccountId, videoId, videoFileId);
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
        Guid FirstAccountId,
        Guid SecondAccountId,
        Guid VideoId,
        Guid VideoFileId);
}
