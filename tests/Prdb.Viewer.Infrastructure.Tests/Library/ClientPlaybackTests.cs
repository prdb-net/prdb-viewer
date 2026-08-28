using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Viewer.Core.Access;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

/// <summary>
/// The account-and-client layers of the direct-play contract: what one browser makes of the
/// library's media configurations, what it observed when it played them, and how both decide what
/// that Account sees and what a play action tries.
/// </summary>
public sealed class ClientPlaybackTests
{
    private const string Chrome = "client-chrome";
    private const string Firefox = "client-firefox";

    [Fact]
    public async Task A_client_dependent_video_stays_out_of_ordinary_results_until_this_client_accepts_it()
    {
        await using var store = await CreateAsync();
        var accountId = await AccountAsync(store, "viewer");
        await SourceAsync(store, ("ordinary.mp4", FixtureProbe.ClientDependent));
        await LibraryPipeline.DrainAsync(store);

        await using (var scope = store.Scope())
        {
            var page = await Discovery(scope).GetAsync(
                accountId,
                Chrome,
                new LibraryDiscoveryRequest(),
                TestContext.Current.CancellationToken);

            // Nothing has asked this browser yet, so nothing may be offered without a warning.
            Assert.Empty(page.Videos);
            Assert.Equal(1, page.HiddenNotReadyForDirectPlay);
        }

        await AssessAsync(store, accountId, Chrome, ClientPlaybackAssessmentVerdict.Positive);

        await using var qualified = store.Scope();
        var admitted = await Discovery(qualified).GetAsync(
            accountId,
            Chrome,
            new LibraryDiscoveryRequest(),
            TestContext.Current.CancellationToken);
        var video = Assert.Single(admitted.Videos);
        Assert.Equal(ClientVideoPlayability.ReadyForDirectPlay, video.Playability);
        Assert.Equal(0, admitted.HiddenNotReadyForDirectPlay);

        // The same Account on another browser is a different question, still unanswered.
        var elsewhere = await Discovery(qualified).GetAsync(
            accountId,
            Firefox,
            new LibraryDiscoveryRequest(),
            TestContext.Current.CancellationToken);
        Assert.Empty(elsewhere.Videos);
    }

    [Fact]
    public async Task A_negative_assessment_removes_a_video_from_this_clients_results_only()
    {
        await using var store = await CreateAsync();
        var accountId = await AccountAsync(store, "viewer");
        await SourceAsync(store, ("baseline.webm", FixtureProbe.Baseline));
        await LibraryPipeline.DrainAsync(store);
        await AssessAsync(store, accountId, Firefox, ClientPlaybackAssessmentVerdict.Negative);

        await using var scope = store.Scope();
        Assert.Empty((await Discovery(scope).GetAsync(
            accountId,
            Firefox,
            new LibraryDiscoveryRequest(),
            TestContext.Current.CancellationToken)).Videos);

        // The Video is not gone: it is Not Directly Playable here, and says so when asked for.
        var revealed = await Discovery(scope).GetAsync(
            accountId,
            Firefox,
            new LibraryDiscoveryRequest
            {
                Playability = [ClientVideoPlayability.NotDirectlyPlayable],
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(
            ClientVideoPlayability.NotDirectlyPlayable,
            Assert.Single(revealed.Videos).Playability);

        // Another browser of the same Account is unaffected.
        Assert.Single((await Discovery(scope).GetAsync(
            accountId,
            Chrome,
            new LibraryDiscoveryRequest(),
            TestContext.Current.CancellationToken)).Videos);
    }

    [Fact]
    public async Task An_observed_failure_is_private_to_the_account_that_produced_it()
    {
        await using var store = await CreateAsync();
        var first = await AccountAsync(store, "first");
        var second = await AccountAsync(store, "second");
        await SourceAsync(store, ("baseline.webm", FixtureProbe.Baseline));
        await LibraryPipeline.DrainAsync(store);

        var fileId = await FileIdAsync(store);

        await using (var scope = store.Scope())
        {
            Assert.True(await scope.ServiceProvider
                .GetRequiredService<ClientPlaybackService>()
                .RecordOutcomeAsync(
                    first,
                    Chrome,
                    fileId,
                    ObservedPlaybackOutcome.Failed,
                    PlaybackFailureCategory.Media,
                    TestContext.Current.CancellationToken));
        }

        await using var scope2 = store.Scope();
        Assert.Empty((await Discovery(scope2).GetAsync(
            first,
            Chrome,
            new LibraryDiscoveryRequest(),
            TestContext.Current.CancellationToken)).Videos);

        // The other Account, on the same physical browser, sees no trace of it.
        Assert.Single((await Discovery(scope2).GetAsync(
            second,
            Chrome,
            new LibraryDiscoveryRequest(),
            TestContext.Current.CancellationToken)).Videos);
    }

    [Fact]
    public async Task Only_a_media_failure_is_remembered_about_a_video_file()
    {
        await using var store = await CreateAsync();
        var accountId = await AccountAsync(store, "viewer");
        await SourceAsync(store, ("baseline.webm", FixtureProbe.Baseline));
        await LibraryPipeline.DrainAsync(store);
        var fileId = await FileIdAsync(store);

        await using var scope = store.Scope();
        var service = scope.ServiceProvider.GetRequiredService<ClientPlaybackService>();

        foreach (var category in new[]
        {
            PlaybackFailureCategory.Delivery,
            PlaybackFailureCategory.Network,
            PlaybackFailureCategory.Availability,
        })
        {
            Assert.False(await service.RecordOutcomeAsync(
                accountId,
                Chrome,
                fileId,
                ObservedPlaybackOutcome.Failed,
                category,
                TestContext.Current.CancellationToken));
        }

        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        Assert.Empty(await database.ObservedPlaybackOutcomes
            .ToListAsync(TestContext.Current.CancellationToken));

        // A broken delivery path therefore cannot empty a library one variant at a time.
        Assert.Single((await Discovery(scope).GetAsync(
            accountId,
            Chrome,
            new LibraryDiscoveryRequest(),
            TestContext.Current.CancellationToken)).Videos);
    }

    [Fact]
    public async Task An_outcome_about_replaced_content_stops_applying_to_the_new_content()
    {
        await using var store = await CreateAsync();
        var accountId = await AccountAsync(store, "viewer");
        await SourceAsync(store, ("baseline.webm", FixtureProbe.Baseline));
        await LibraryPipeline.DrainAsync(store);
        var fileId = await FileIdAsync(store);

        await using (var scope = store.Scope())
        {
            await scope.ServiceProvider
                .GetRequiredService<ClientPlaybackService>()
                .RecordOutcomeAsync(
                    accountId,
                    Chrome,
                    fileId,
                    ObservedPlaybackOutcome.Failed,
                    PlaybackFailureCategory.Media,
                    TestContext.Current.CancellationToken);
        }

        // The file's content changes; the failure was about bytes that are no longer there.
        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            await database.VideoFiles
                .Where(file => file.Id == fileId)
                .ExecuteUpdateAsync(
                    update => update.SetProperty(file => file.Sha256, new string('c', 64)),
                    TestContext.Current.CancellationToken);
        }

        await using var verification = store.Scope();
        Assert.Single((await Discovery(verification).GetAsync(
            accountId,
            Chrome,
            new LibraryDiscoveryRequest(),
            TestContext.Current.CancellationToken)).Videos);
    }

    [Fact]
    public async Task The_variants_arrive_in_the_order_one_play_action_would_try_them()
    {
        await using var store = await CreateAsync();
        var accountId = await AccountAsync(store, "viewer");
        await SourceAsync(
            store,
            ("baseline.webm", FixtureProbe.Baseline),
            ("ordinary.mp4", FixtureProbe.ClientDependent));
        await LibraryPipeline.DrainAsync(store);

        // Both files describe one work, so they are one Video with two variants.
        await MergeIntoOneVideoAsync(store);
        await AssessAsync(
            store,
            accountId,
            Chrome,
            ClientPlaybackAssessmentVerdict.Positive,
            FixtureProbe.ClientDependent.ProfileKey,
            smooth: true,
            powerEfficient: true);

        await using var scope = store.Scope();
        var video = Assert.Single((await Discovery(scope).GetAsync(
            accountId,
            Chrome,
            new LibraryDiscoveryRequest(),
            TestContext.Current.CancellationToken)).Videos);

        // Exact client evidence outranks the conservative static baseline.
        Assert.Equal(
            ["ordinary.mp4", "baseline.webm"],
            video.VideoFiles.Select(variant => variant.ContainerFormat == "mov,mp4,m4a,3gp,3g2,mj2"
                ? "ordinary.mp4"
                : "baseline.webm"));
        Assert.Equal(
            VariantSelectionReason.PositivelyAssessedAndSmooth,
            video.VideoFiles[0].SelectionReason);
        Assert.Equal(
            VariantSelectionReason.BaselineCandidate,
            video.VideoFiles[1].SelectionReason);
        Assert.True(video.VideoFiles[0].ReadyForDirectPlay);
    }

    [Fact]
    public async Task A_client_is_asked_only_about_configurations_it_has_not_answered_for()
    {
        await using var store = await CreateAsync();
        var accountId = await AccountAsync(store, "viewer");
        await SourceAsync(
            store,
            ("baseline.webm", FixtureProbe.Baseline),
            ("ordinary.mp4", FixtureProbe.ClientDependent));
        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        var service = scope.ServiceProvider.GetRequiredService<ClientPlaybackService>();
        var outstanding = await service.UnassessedProfilesAsync(
            accountId,
            Chrome,
            TestContext.Current.CancellationToken);
        Assert.Equal(2, outstanding.Count);

        // The question carries what Media Capabilities needs to answer it.
        var mp4 = Assert.Single(
            outstanding,
            profile => profile.ProfileKey == FixtureProbe.ClientDependent.ProfileKey);
        Assert.Equal("video/mp4; codecs=\"avc1.640028\"", mp4.VideoContentType);
        Assert.Equal("audio/mp4; codecs=\"mp4a.40.2\"", mp4.AudioContentType);
        Assert.Equal(1920, mp4.Width);
        Assert.Equal(25, mp4.FrameRate);

        await service.RecordAssessmentsAsync(
            accountId,
            Chrome,
            [
                new ClientPlaybackAssessmentReport(
                    mp4.ProfileKey,
                    ClientPlaybackAssessmentVerdict.Positive,
                    true,
                    false,
                    "MediaCapabilities"),
            ],
            TestContext.Current.CancellationToken);

        Assert.Single(await service.UnassessedProfilesAsync(
            accountId,
            Chrome,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_explicit_retry_forgets_what_this_client_observed()
    {
        await using var store = await CreateAsync();
        var accountId = await AccountAsync(store, "viewer");
        await SourceAsync(store, ("baseline.webm", FixtureProbe.Baseline));
        await LibraryPipeline.DrainAsync(store);
        var fileId = await FileIdAsync(store);

        await using var scope = store.Scope();
        var service = scope.ServiceProvider.GetRequiredService<ClientPlaybackService>();
        await service.RecordOutcomeAsync(
            accountId,
            Chrome,
            fileId,
            ObservedPlaybackOutcome.Failed,
            PlaybackFailureCategory.Media,
            TestContext.Current.CancellationToken);
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var videoId = await database.VideoFiles
            .Where(file => file.Id == fileId)
            .Select(file => file.VideoId)
            .SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, await service.ForgetOutcomesAsync(
            accountId,
            Chrome,
            videoId,
            TestContext.Current.CancellationToken));
        Assert.Single((await Discovery(scope).GetAsync(
            accountId,
            Chrome,
            new LibraryDiscoveryRequest(),
            TestContext.Current.CancellationToken)).Videos);
    }

    private static LibraryDiscovery Discovery(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<LibraryDiscovery>();

    private static async Task AssessAsync(
        TestDatabase store,
        Guid accountId,
        string clientContextKey,
        ClientPlaybackAssessmentVerdict verdict,
        string? profileKey = null,
        bool? smooth = null,
        bool? powerEfficient = null)
    {
        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var keys = profileKey is null
            ? await database.VideoFiles
                .Select(file => file.ProfileKey)
                .Distinct()
                .ToListAsync(TestContext.Current.CancellationToken)
            : [profileKey];

        await scope.ServiceProvider
            .GetRequiredService<ClientPlaybackService>()
            .RecordAssessmentsAsync(
                accountId,
                clientContextKey,
                keys
                    .Select(key => new ClientPlaybackAssessmentReport(
                        key,
                        verdict,
                        smooth,
                        powerEfficient,
                        "MediaCapabilities"))
                    .ToArray(),
                TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Associates the seeded files with one Video, the way a shared work identity would, so a test
    /// can observe variant selection without depending on the identification lane.
    /// </summary>
    private static async Task MergeIntoOneVideoAsync(TestDatabase store)
    {
        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var files = await database.VideoFiles
            .AsTracking()
            .OrderBy(file => file.RelativePath)
            .ToListAsync(TestContext.Current.CancellationToken);
        var survivor = files[0].VideoId;

        foreach (var file in files.Skip(1))
        {
            var merged = await database.Videos
                .AsTracking()
                .SingleAsync(video => video.Id == file.VideoId, TestContext.Current.CancellationToken);
            merged.SurvivingVideoId = survivor;
            file.VideoId = survivor;
        }

        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        await scope.ServiceProvider
            .GetRequiredService<VideoProjection>()
            .RefreshAsync(survivor, TestContext.Current.CancellationToken);
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<Guid> FileIdAsync(TestDatabase store)
    {
        await using var scope = store.Scope();

        return await scope.ServiceProvider
            .GetRequiredService<ViewerDbContext>()
            .VideoFiles
            .OrderBy(file => file.RelativePath)
            .Select(file => file.Id)
            .FirstAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<Guid> AccountAsync(TestDatabase store, string username)
    {
        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var account = new AccountRow
        {
            Id = Guid.CreateVersion7(),
            Username = username,
            NormalizedUsername = username,
            PasswordHash = new string('h', 84),
            Authority = AccountAuthority.User,
            State = AccountState.Approved,
            RegisteredAt = DateTime.SpecifyKind(new DateTime(2026, 8, 1), DateTimeKind.Utc),
        };
        database.Accounts.Add(account);
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        return account.Id;
    }

    private static async Task SourceAsync(
        TestDatabase store,
        params (string Name, MediaConfiguration Media)[] files)
    {
        var source = Path.Combine(store.LibraryMountRoot.Path, "source");
        Directory.CreateDirectory(source);

        for (var index = 0; index < files.Length; index++)
        {
            await File.WriteAllBytesAsync(
                Path.Combine(source, files[index].Name),
                [1, 2, 3, (byte)index],
                TestContext.Current.CancellationToken);
        }

        await LibraryPipeline.ActivateAsync(store, source);
    }

    private static Task<TestDatabase> CreateAsync() =>
        TestDatabase.CreateAsync(
            mediaProbe: new ConfiguredProbe(),
            hasher: new FixtureHasher(),
            previewGenerator: new FixturePreviewGenerator(),
            identificationClient: new FixtureIdentificationClient());

    /// <summary>
    /// Reports the configuration the file name implies, so a test can seed a library with the
    /// exact media questions it wants to ask a client about.
    /// </summary>
    private sealed class ConfiguredProbe : IMediaProbe
    {
        public Task<MediaProbeFacts?> InspectAsync(
            string path,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<MediaProbeFacts?>(new MediaProbeFacts(
                Path.GetExtension(path) == ".webm"
                    ? FixtureProbe.Baseline
                    : FixtureProbe.ClientDependent,
                12_345));
    }
}
