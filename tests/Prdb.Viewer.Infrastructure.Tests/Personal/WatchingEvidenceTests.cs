using Microsoft.EntityFrameworkCore;
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
/// The evidence the recommendation rules read, held to the worked examples ADR 0022 states.
/// </summary>
public sealed class WatchingEvidenceTests
{
    private const string Client = "test-client";

    [Fact]
    public async Task Six_ten_second_runs_are_a_minute_watched_and_ten_seconds_uninterrupted()
    {
        var time = At(12, 0);
        await using var store = await TestDatabase.CreateAsync(timeProvider: time);
        var seeded = await SeedAsync(store);
        await using var scope = store.Scope();
        var service = scope.ServiceProvider.GetRequiredService<PersonalStateService>();
        var attemptId = await StartAsync(service, seeded);

        // Six ten-second stretches, each of them somewhere else in the Video.
        for (var index = 0; index < 6; index++)
        {
            time.Advance(TimeSpan.FromSeconds(10));
            await ReportAsync(
                service,
                seeded,
                attemptId,
                sequence: index,
                position: 100_000 + (index * 200_000) + 10_000,
                active: 10_000);
        }

        var attempt = await AttemptAsync(scope, attemptId);
        Assert.Equal(60_000, attempt.ActiveWatchDurationMilliseconds);
        // Seeking is neutral: it adds nothing to the total and takes nothing away, and it ends the
        // run without being negative.
        Assert.Equal(10_000, attempt.LongestUninterruptedRunMilliseconds);
        Assert.Equal(
            60_000,
            (await service.GetSummaryAsync(
                seeded.AccountId,
                seeded.VideoId,
                TestContext.Current.CancellationToken)).AccumulatedWatchDurationMilliseconds);
    }

    [Fact]
    public async Task One_continuous_minute_is_a_minute_watched_and_a_minute_uninterrupted()
    {
        var time = At(12, 0);
        await using var store = await TestDatabase.CreateAsync(timeProvider: time);
        var seeded = await SeedAsync(store);
        await using var scope = store.Scope();
        var service = scope.ServiceProvider.GetRequiredService<PersonalStateService>();
        var attemptId = await StartAsync(service, seeded);

        for (var index = 0; index < 6; index++)
        {
            time.Advance(TimeSpan.FromSeconds(10));
            await ReportAsync(
                service,
                seeded,
                attemptId,
                sequence: index,
                position: (index + 1) * 10_000,
                active: 10_000);
        }

        var attempt = await AttemptAsync(scope, attemptId);
        Assert.Equal(60_000, attempt.ActiveWatchDurationMilliseconds);
        Assert.Equal(60_000, attempt.LongestUninterruptedRunMilliseconds);
    }

    [Fact]
    public async Task A_pause_ends_the_run_without_costing_the_session_anything()
    {
        var time = At(12, 0);
        await using var store = await TestDatabase.CreateAsync(timeProvider: time);
        var seeded = await SeedAsync(store);
        await using var scope = store.Scope();
        var service = scope.ServiceProvider.GetRequiredService<PersonalStateService>();
        var attemptId = await StartAsync(service, seeded);

        time.Advance(TimeSpan.FromSeconds(15));
        await ReportAsync(service, seeded, attemptId, 0, position: 15_000, active: 15_000);

        // Five minutes paused, then watching resumes exactly where it stopped.
        time.Advance(TimeSpan.FromMinutes(5));
        time.Advance(TimeSpan.FromSeconds(10));
        await ReportAsync(service, seeded, attemptId, 1, position: 25_000, active: 10_000);

        var attempt = await AttemptAsync(scope, attemptId);
        Assert.Equal(25_000, attempt.ActiveWatchDurationMilliseconds);
        Assert.Equal(15_000, attempt.LongestUninterruptedRunMilliseconds);
    }

    [Fact]
    public async Task Duplicate_and_overlapping_reports_do_not_inflate_anything()
    {
        var time = At(12, 0);
        await using var store = await TestDatabase.CreateAsync(timeProvider: time);
        var seeded = await SeedAsync(store);
        await using var scope = store.Scope();
        var service = scope.ServiceProvider.GetRequiredService<PersonalStateService>();
        var attemptId = await StartAsync(service, seeded);

        time.Advance(TimeSpan.FromSeconds(10));
        var reportId = Guid.NewGuid();
        await ReportAsync(service, seeded, attemptId, 0, 10_000, 10_000, reportId);
        var duplicate = await ReportAsync(service, seeded, attemptId, 0, 10_000, 10_000, reportId);
        Assert.Equal(PlaybackReportVerdict.Duplicate, duplicate.Verdict);

        // A second player of the same Account watching the same ten seconds of the same Video.
        var concurrent = await StartAsync(service, seeded);
        await ReportAsync(service, seeded, concurrent, 0, 10_000, 10_000);

        Assert.Equal(10_000, (await AttemptAsync(scope, attemptId)).ActiveWatchDurationMilliseconds);
        Assert.Equal(
            10_000,
            (await service.GetSummaryAsync(
                seeded.AccountId,
                seeded.VideoId,
                TestContext.Current.CancellationToken)).AccumulatedWatchDurationMilliseconds);
    }

    [Fact]
    public async Task A_departure_is_recorded_only_where_it_was_observed_and_never_overwritten()
    {
        var time = At(12, 0);
        await using var store = await TestDatabase.CreateAsync(timeProvider: time);
        var seeded = await SeedAsync(store);
        await using var scope = store.Scope();
        var service = scope.ServiceProvider.GetRequiredService<PersonalStateService>();

        // A session nobody said anything about stays Unknown, which is never read as a dislike.
        var silent = await StartAsync(service, seeded);
        time.Advance(TimeSpan.FromSeconds(8));
        await ReportAsync(service, seeded, silent, 0, 8_000, 8_000);
        await service.EndPlaybackAttemptAsync(
            seeded.AccountId,
            silent,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(PlaybackDeparture.Unknown, (await AttemptAsync(scope, silent)).Departure);

        // A failure is evidence about a Video File and about nothing else.
        var failed = await StartAsync(service, seeded);
        time.Advance(TimeSpan.FromSeconds(8));
        await ReportAsync(service, seeded, failed, 0, 8_000, 8_000);
        await service.EndPlaybackAttemptAsync(
            seeded.AccountId,
            failed,
            PlaybackDeparture.TechnicalFailure,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            PlaybackDeparture.TechnicalFailure,
            (await AttemptAsync(scope, failed)).Departure);

        // The first observation is the one that saw what happened; a later Unknown does not erase
        // it, and neither does signing out.
        await service.EndPlaybackAttemptAsync(
            seeded.AccountId,
            failed,
            cancellationToken: TestContext.Current.CancellationToken);
        await service.EndAccountPlaybackAttemptsAsync(
            seeded.AccountId,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            PlaybackDeparture.TechnicalFailure,
            (await AttemptAsync(scope, failed)).Departure);

        var departed = await StartAsync(service, seeded);
        time.Advance(TimeSpan.FromSeconds(8));
        await ReportAsync(service, seeded, departed, 0, 8_000, 8_000);
        await service.EndPlaybackAttemptAsync(
            seeded.AccountId,
            departed,
            PlaybackDeparture.AnotherVideo,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            PlaybackDeparture.AnotherVideo,
            (await AttemptAsync(scope, departed)).Departure);
    }

    [Fact]
    public async Task An_abandoned_session_ends_by_inactivity_rather_than_by_anybody()
    {
        var time = At(12, 0);
        await using var store = await TestDatabase.CreateAsync(timeProvider: time);
        var seeded = await SeedAsync(store);
        await using var scope = store.Scope();
        var service = scope.ServiceProvider.GetRequiredService<PersonalStateService>();
        var attemptId = await StartAsync(service, seeded);

        time.Advance(TimeSpan.FromSeconds(10));
        await ReportAsync(service, seeded, attemptId, 0, 10_000, 10_000);

        time.Advance(TimeSpan.FromMinutes(45));
        var late = await ReportAsync(service, seeded, attemptId, 1, 20_000, 10_000);

        Assert.Equal(PlaybackReportVerdict.AttemptEnded, late.Verdict);
        var attempt = await AttemptAsync(scope, attemptId);
        Assert.Equal(PlaybackDeparture.Inactivity, attempt.Departure);
        // The evidence that arrived too late is not counted, and it is not held against anybody.
        Assert.Equal(10_000, attempt.ActiveWatchDurationMilliseconds);
    }

    [Fact]
    public async Task The_last_watched_moment_is_a_summary_and_never_a_guess()
    {
        var time = At(12, 0);
        await using var store = await TestDatabase.CreateAsync(timeProvider: time);
        var seeded = await SeedAsync(store);
        await using var scope = store.Scope();
        var service = scope.ServiceProvider.GetRequiredService<PersonalStateService>();

        // A Video with an explicit reference but no watching says nothing about being watched.
        await service.SetFavouriteAsync(
            seeded.AccountId,
            seeded.VideoId,
            selected: true,
            TestContext.Current.CancellationToken);
        Assert.Null((await service.GetSummaryAsync(
            seeded.AccountId,
            seeded.VideoId,
            TestContext.Current.CancellationToken)).LastWatchedAt);

        var attemptId = await StartAsync(service, seeded);
        time.Advance(TimeSpan.FromSeconds(10));
        await ReportAsync(service, seeded, attemptId, 0, 10_000, 10_000);

        Assert.Equal(
            time.GetUtcNow(),
            (await service.GetSummaryAsync(
                seeded.AccountId,
                seeded.VideoId,
                TestContext.Current.CancellationToken)).LastWatchedAt);

        // A report that carries no confirmed watching is not a watch.
        var before = time.GetUtcNow();
        time.Advance(TimeSpan.FromSeconds(30));
        await ReportAsync(service, seeded, attemptId, 1, 10_000, active: 0);
        Assert.Equal(
            before,
            (await service.GetSummaryAsync(
                seeded.AccountId,
                seeded.VideoId,
                TestContext.Current.CancellationToken)).LastWatchedAt);
    }

    [Fact]
    public async Task A_browsing_visit_holds_this_visit_and_forgets_the_last_one()
    {
        var time = At(12, 0);
        await using var store = await TestDatabase.CreateAsync(timeProvider: time);
        var seeded = await SeedAsync(store);
        await using var scope = store.Scope();
        var service = scope.ServiceProvider.GetRequiredService<PersonalStateService>();

        Assert.Empty(await service.CurrentBrowsingVisitAsync(
            seeded.AccountId,
            Client,
            TestContext.Current.CancellationToken));

        var attemptId = await StartAsync(service, seeded);
        time.Advance(TimeSpan.FromSeconds(10));
        await ReportAsync(service, seeded, attemptId, 0, 10_000, 10_000);

        Assert.Equal(
            [seeded.VideoId],
            await service.CurrentBrowsingVisitAsync(
                seeded.AccountId,
                Client,
                TestContext.Current.CancellationToken));

        // Another client of the same Account browses separately: what was watched here does not
        // rearrange what is offered there.
        Assert.Empty(await service.CurrentBrowsingVisitAsync(
            seeded.AccountId,
            "another-client",
            TestContext.Current.CancellationToken));

        // Half an hour without anything is the end of the visit, and a later one starts empty
        // rather than carrying the last one's marks.
        time.Advance(TimeSpan.FromMinutes(31));
        Assert.Empty(await service.CurrentBrowsingVisitAsync(
            seeded.AccountId,
            Client,
            TestContext.Current.CancellationToken));

        var later = await StartAsync(service, seeded);
        time.Advance(TimeSpan.FromSeconds(10));
        await ReportAsync(service, seeded, later, 0, 10_000, 10_000);
        Assert.Equal(
            [seeded.VideoId],
            await service.CurrentBrowsingVisitAsync(
                seeded.AccountId,
                Client,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            1,
            await scope.ServiceProvider.GetRequiredService<ViewerDbContext>().BrowsingVisitWatches
                .CountAsync(TestContext.Current.CancellationToken));
    }

    private static FakeTimeProvider At(int hour, int minute) =>
        new(new DateTimeOffset(2026, 9, 13, hour, minute, 0, TimeSpan.Zero));

    private static async Task<Guid> StartAsync(PersonalStateService service, SeededIds seeded) =>
        (await service.StartPlaybackAttemptAsync(
            seeded.AccountId,
            seeded.VideoId,
            seeded.VideoFileId,
            TestContext.Current.CancellationToken)).PlaybackAttemptId!.Value;

    private static Task<PlaybackReportResult> ReportAsync(
        PersonalStateService service,
        SeededIds seeded,
        Guid attemptId,
        int sequence,
        long position,
        long active,
        Guid? reportId = null) =>
        service.ReportPlaybackAsync(
            seeded.AccountId,
            attemptId,
            reportId ?? Guid.NewGuid(),
            sequence,
            seeded.VideoFileId,
            position,
            active,
            naturalEndConfirmed: false,
            endSession: false,
            Client,
            TestContext.Current.CancellationToken);

    private static Task<PlaybackAttemptRow> AttemptAsync(AsyncServiceScope scope, Guid attemptId) =>
        scope.ServiceProvider.GetRequiredService<ViewerDbContext>().PlaybackAttempts
            .AsNoTracking()
            .SingleAsync(attempt => attempt.Id == attemptId, TestContext.Current.CancellationToken);

    private static async Task<SeededIds> SeedAsync(TestDatabase store)
    {
        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var now = new DateTime(2026, 9, 13, 11, 0, 0, DateTimeKind.Utc);
        var accountId = Guid.CreateVersion7();
        var directoryId = Guid.CreateVersion7();
        var videoId = Guid.CreateVersion7();
        var videoFileId = Guid.CreateVersion7();
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
        database.Videos.Add(new VideoRow { Id = videoId, DiscoveryDate = now });
        database.VideoFiles.Add(new VideoFileRow
        {
            Id = videoFileId,
            VideoId = videoId,
            LibraryDirectoryId = directoryId,
            RelativePath = "watched.mp4",
            Size = 100,
            LastWriteTimeUtc = now,
            Sha256 = new string('C', 64),
            PublicDeliveryId = Guid.NewGuid(),
            ContainerFormat = "mp4",
            VideoCodec = "h264",
            AudioCodec = "aac",
            DurationMilliseconds = 3_600_000,
            Width = 640,
            Height = 360,
            Availability = VideoFileAvailability.Available,
            DirectPlayClassification = DirectPlayClassification.BaselineCandidate,
            LastObservedScanId = Guid.CreateVersion7(),
            InspectedAt = now,
        });
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new SeededIds(accountId, videoId, videoFileId);
    }

    private sealed record SeededIds(Guid AccountId, Guid VideoId, Guid VideoFileId);
}
