using System.Diagnostics;
using System.Globalization;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Viewer.Core.Access;
using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Core.Personal;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Tests.Library;
using Prdb.Viewer.Infrastructure.Persistence;
using Prdb.Viewer.Infrastructure.Personal;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Performance;

/// <summary>
/// The production-shaped SQLite workload the release requires evidence for. It builds a library far
/// larger than a first installation, then measures the read paths a signed-in User and an
/// Administrator actually wait for, plus the write path every playback report takes.
///
/// It is opt-in because it is a measurement rather than an assertion about a developer machine:
/// run it with <c>VIEWER_BENCHMARK=1</c>, point <c>VIEWER_BENCHMARK_REPORT</c> at a file, and
/// record the numbers in <c>docs/performance.md</c>.
/// </summary>
public sealed class SqliteWorkloadBenchmark
{
    private static readonly int[] Scales = [2_000, 20_000];

    private const int FilesPerExtraVideo = 10;
    private const int Accounts = 25;
    private const int Samples = 20;

    [Fact]
    public async Task A_production_shaped_library_stays_responsive()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("VIEWER_BENCHMARK") == "1",
            "Set VIEWER_BENCHMARK=1 to measure the production-shaped SQLite workload.");

        var report = new List<string>();

        foreach (var scale in Scales)
        {
            report.AddRange(await MeasureScaleAsync(scale));
        }

        foreach (var line in report)
        {
            TestContext.Current.TestOutputHelper?.WriteLine(line);
        }

        // The measurement is the deliverable, so it is written where it can be read back and
        // recorded rather than left in a test runner's captured output.
        if (Environment.GetEnvironmentVariable("VIEWER_BENCHMARK_REPORT") is { Length: > 0 } path)
        {
            await File.WriteAllLinesAsync(path, report, TestContext.Current.CancellationToken);
        }
    }

    private static async Task<IReadOnlyList<string>> MeasureScaleAsync(int videos)
    {
        await using var store = await TestDatabase.CreateAsync();
        var seed = Stopwatch.StartNew();
        var accounts = await SeedAsync(store, videos);
        seed.Stop();
        var report = new List<string>
        {
            string.Empty,
            $"## {videos:N0} Videos",
            string.Empty,
            $"Video Files: {videos + videos / FilesPerExtraVideo:N0} · Accounts: {Accounts} · " +
            $"seeded in {seed.Elapsed.TotalSeconds:N1} s · " +
            $"database {new FileInfo(store.Location.FilePath).Length / (1024 * 1024)} MiB",
            string.Empty,
        };

        report.Add(await MeasureAsync("Library, first page", store, async scope =>
            (await scope.ServiceProvider
                .GetRequiredService<LibraryDiscovery>()
                .GetAsync(
                    accounts[0],
                    LibraryPipeline.ClientContext,
                    new LibraryDiscoveryRequest(),
                    TestContext.Current.CancellationToken)).Videos.Count));
        report.Add(await MeasureAsync("Library, deep page", store, async scope =>
            (await scope.ServiceProvider
                .GetRequiredService<LibraryDiscovery>()
                .GetAsync(
                    accounts[0],
                    LibraryPipeline.ClientContext,
                    new LibraryDiscoveryRequest { Skip = videos - 100 },
                    TestContext.Current.CancellationToken)).Videos.Count));
        report.Add(await MeasureAsync("Library, search", store, async scope =>
            (await scope.ServiceProvider
                .GetRequiredService<LibraryDiscovery>()
                .GetAsync(
                    accounts[0],
                    LibraryPipeline.ClientContext,
                    new LibraryDiscoveryRequest { Query = "benchmark work 1234" },
                    TestContext.Current.CancellationToken)).Videos.Count));
        report.Add(await MeasureAsync("Library, title order", store, async scope =>
            (await scope.ServiceProvider
                .GetRequiredService<LibraryDiscovery>()
                .GetAsync(
                    accounts[0],
                    LibraryPipeline.ClientContext,
                    new LibraryDiscoveryRequest { Sort = LibrarySortOrder.TitleAscending },
                    TestContext.Current.CancellationToken)).Videos.Count));
        report.Add(await MeasureAsync("Library facets", store, async scope =>
            (await scope.ServiceProvider
                .GetRequiredService<LibraryDiscovery>()
                .GetFacetsAsync(accounts[0], new LibraryDiscoveryRequest(), cancellationToken: TestContext.Current.CancellationToken)).Sites.Count));
        report.Add(await MeasureAsync("Personal library shelves", store, async scope =>
            (await scope.ServiceProvider
                .GetRequiredService<LibraryDiscovery>()
                .GetAsync(
                    accounts[0],
                    LibraryPipeline.ClientContext,
                    new LibraryDiscoveryRequest
                    {
                        Shelf = [PersonalShelf.ContinueWatching],
                        Sort = LibrarySortOrder.ShelfOrder,
                    },
                    TestContext.Current.CancellationToken)).Videos.Count));
        // The whole Recommendations page: three sections, every exclusion, and the ranking over
        // one Account's own history. It is the newest read path a signed-in User waits for.
        report.Add(await MeasureAsync("Recommendations page", store, async scope =>
            (await scope.ServiceProvider
                .GetRequiredService<RecommendationService>()
                .GetAsync(
                    accounts[0],
                    LibraryPipeline.ClientContext,
                    seed: 1,
                    cancellationToken: TestContext.Current.CancellationToken))
                .Sections.Sum(section => section.Videos.Count)));
        // The same page for an Account with nothing behind it, which is the discovery path alone
        // and the one that reads the library rather than a person's history.
        report.Add(await MeasureAsync("Recommendations, cold start", store, async scope =>
            (await scope.ServiceProvider
                .GetRequiredService<RecommendationService>()
                .GetAsync(
                    accounts[^1],
                    LibraryPipeline.ClientContext,
                    seed: 1,
                    cancellationToken: TestContext.Current.CancellationToken))
                .Sections.Sum(section => section.Videos.Count)));
        report.Add(await MeasureAsync("Background work status", store, async scope =>
            (await scope.ServiceProvider
                .GetRequiredService<BackgroundWorkQuery>()
                .GetAsync(TestContext.Current.CancellationToken)).Work.Count));
        report.Add(await MeasureAsync("Identification review queue", store, async scope =>
            (await scope.ServiceProvider
                .GetRequiredService<IdentificationReviewService>()
                .QueueAsync(TestContext.Current.CancellationToken)).Count));
        report.Add(await MeasureAsync("Outstanding hashing lane query", store, async scope =>
            await scope.ServiceProvider
                .GetRequiredService<ViewerDbContext>()
                .VideoFiles
                .Where(file => file.Availability == VideoFileAvailability.Available &&
                               (file.HashedSha256 == null || file.HashedSha256 != file.Sha256))
                .OrderBy(file => file.RelativePath)
                .Take(1)
                .CountAsync(TestContext.Current.CancellationToken)));

        report.Add(await MeasureNeighbourhoodSearchAsync(store));

        var videoId = await FirstVideoAsync(store);
        report.Add(await MeasureAsync("Playback report write", store, async scope =>
        {
            var personal = scope.ServiceProvider.GetRequiredService<PersonalStateService>();
            var attempt = await personal.StartPlaybackAttemptAsync(
                accounts[1],
                videoId,
                await FirstVideoFileAsync(scope, videoId),
                TestContext.Current.CancellationToken);
            await personal.EndPlaybackAttemptAsync(
                accounts[1],
                attempt.PlaybackAttemptId!.Value,
                cancellationToken: TestContext.Current.CancellationToken);
            return 1;
        }));
        return report;
    }

    /// <summary>
    /// The whole perceptual neighbourhood backlog, drained once over a library of this size.
    ///
    /// It is measured as one pass rather than as twenty samples of a query, because that is what
    /// it is: a library's files are compared once each, for the hash value they carry, and the
    /// second run over an unchanged library does nothing at all. The figure the ticket wants is
    /// therefore what an installation pays once — and what it never pays again until a file is
    /// hashed to a different value.
    /// </summary>
    private static async Task<string> MeasureNeighbourhoodSearchAsync(TestDatabase store)
    {
        await using (var queue = store.Scope())
        {
            var database = queue.ServiceProvider.GetRequiredService<ViewerDbContext>();
            var directory = await database.LibraryDirectories
                .AsNoTracking()
                .FirstAsync(TestContext.Current.CancellationToken);
            database.BackgroundWork.Add(new BackgroundWorkRow
            {
                Id = Guid.CreateVersion7(),
                Category = BackgroundWorkCategory.PerceptualNeighbourhood,
                State = BackgroundWorkState.Queued,
                Trigger = BackgroundWorkTrigger.FollowUpWork,
                Phase = BackgroundWorkPhases.Queued,
                LibraryDirectoryId = directory.Id,
                ConfigurationGeneration = directory.ConfigurationGeneration,
                RequestedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var watch = Stopwatch.StartNew();
        var slices = 0;

        while (true)
        {
            await using var scope = store.Scope();

            if (!await scope.ServiceProvider
                .GetRequiredService<PerceptualNeighbourhoodRunner>()
                .RunNextSliceAsync(TestContext.Current.CancellationToken))
            {
                break;
            }

            slices++;
        }

        watch.Stop();

        await using var read = store.Scope();
        var found = await read.ServiceProvider
            .GetRequiredService<ViewerDbContext>()
            .PerceptualNeighbourhoods
            .CountAsync(TestContext.Current.CancellationToken);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Perceptual neighbourhood search, whole backlog: {watch.Elapsed.TotalSeconds:N1} s · " +
            $"{slices:N0} slices · {found:N0} neighbourhoods");
    }

    private static async Task<string> MeasureAsync(
        string name,
        TestDatabase store,
        Func<AsyncServiceScope, Task<int>> operation)
    {
        var timings = new List<double>();
        var observed = 0;

        for (var sample = 0; sample < Samples; sample++)
        {
            await using var scope = store.Scope();
            var watch = Stopwatch.StartNew();
            observed = await operation(scope);
            watch.Stop();
            timings.Add(watch.Elapsed.TotalMilliseconds);
        }

        timings.Sort();
        var median = timings[timings.Count / 2];
        var worst = timings[^1];

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{name}: median {median:N1} ms · slowest {worst:N1} ms · {observed:N0} rows");
    }

    private static async Task<Guid[]> SeedAsync(TestDatabase store, int videos)
    {
        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var at = DateTime.SpecifyKind(new DateTime(2026, 1, 1), DateTimeKind.Utc);
        var directoryId = Guid.CreateVersion7();
        database.LibraryDirectories.Add(new LibraryDirectoryRow
        {
            Id = directoryId,
            Name = "Benchmark Library",
            ContainerPath = "/library/benchmark",
            State = LibraryDirectoryState.Active,
            Health = LibraryDirectoryHealth.Healthy,
            ConfigurationGeneration = 1,
            CreatedAt = at,
            ActivatedAt = at,
        });
        var accounts = Enumerable.Range(0, Accounts)
            .Select(index => new AccountRow
            {
                Id = Guid.CreateVersion7(),
                Username = $"viewer-{index:00}",
                NormalizedUsername = $"viewer-{index:00}",
                PasswordHash = new string('h', 84),
                Authority = index == 0 ? AccountAuthority.Administrator : AccountAuthority.User,
                State = AccountState.Approved,
                RegisteredAt = at,
                ApprovedAt = at,
            })
            .ToArray();
        database.Accounts.AddRange(accounts);

        // Every Account's client has qualified the library's one configuration, as it would after
        // its first visit. Without that a signed-in User sees nothing and the measurement is of an
        // empty answer.
        foreach (var account in accounts)
        {
            database.ClientPlaybackAssessments.Add(new ClientPlaybackAssessmentRow
            {
                AccountId = account.Id,
                ClientContextKey = LibraryPipeline.ClientContext,
                ProfileKey = BenchmarkMedia.ProfileKey,
                Verdict = ClientPlaybackAssessmentVerdict.Positive,
                Smooth = true,
                PowerEfficient = true,
                Method = "MediaCapabilities",
                AssessedAt = at,
            });
        }

        await database.SaveChangesAsync(TestContext.Current.CancellationToken);

        for (var batch = 0; batch < videos; batch += 1_000)
        {
            database.ChangeTracker.Clear();

            for (var index = batch; index < Math.Min(batch + 1_000, videos); index++)
            {
                var video = new VideoRow
                {
                    Id = Guid.CreateVersion7(),
                    DiscoveryDate = at.AddMinutes(index),
                };
                database.Videos.Add(video);
                database.VideoFiles.Add(NewFile(video.Id, directoryId, index, 0, at));

                // Some Videos carry more than one occurrence, as an associated library does.
                if (index % FilesPerExtraVideo == 0)
                {
                    database.VideoFiles.Add(NewFile(video.Id, directoryId, index, 1, at));
                }

                if (index % 4 == 0)
                {
                    database.IdentificationClaims.Add(new IdentificationClaimRow
                    {
                        Id = Guid.CreateVersion7(),
                        VideoId = video.Id,
                        Dimension = IdentificationDimension.WorkIdentification,
                        Status = IdentificationClaimStatus.Current,
                        Source = IdentificationSource.PrdbIdentification,
                        EvidenceClass = IdentificationEvidenceClass.Conclusive,
                        TargetKey = $"work-{index}",
                        TargetTitle = $"Benchmark Work {index}",
                        EstablishedAt = at,
                        LastConfirmedAt = at,
                    });
                }

                // Every Account but the last keeps private state on a slice of the library. The
                // last one keeps none, so that a cold start is a case this measures rather than a
                // case it assumes away.
                foreach (var account in accounts.SkipLast(1).Where(_ => index % 50 == 0))
                {
                    database.PersonalVideoStates.Add(new PersonalVideoStateRow
                    {
                        AccountId = account.Id,
                        VideoId = video.Id,
                        PlaybackProgressMilliseconds = 120_000,
                        AccumulatedWatchDurationMilliseconds = 240_000,
                        PlayCount = 2,
                        PlayState = PersonalPlayState.InProgress,
                        LastQualifiedActivityAt = at.AddMinutes(index),
                        LastWatchedAt = at.AddMinutes(index),
                        Reaction = index % 100 == 0 ? PersonalReaction.Like : null,
                        FavouriteAddedAt = index % 100 == 0 ? at : null,
                        WatchLaterAddedAt = index % 200 == 0 ? at : null,
                    });
                }
            }

            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Discovery reads the projection, so a benchmark that skipped it would measure an empty
        // library. This is the same work the startup backfill does after an upgrade.
        var projection = scope.ServiceProvider.GetRequiredService<VideoProjection>();

        while (await projection.RefreshOutstandingAsync(
                   cancellationToken: TestContext.Current.CancellationToken))
        {
            database.ChangeTracker.Clear();
        }

        return accounts.Select(account => account.Id).ToArray();
    }

    /// <summary>
    /// Ordinary H.264/AAC in MP4: what a real library is mostly made of, and a Client-Dependent
    /// configuration, so the measurement includes the per-Account, per-client admission question
    /// rather than the one case that can skip it.
    /// </summary>
    private static readonly MediaConfiguration BenchmarkMedia =
        new("mov,mp4,m4a,3gp,3g2,mj2", "h264", "aac")
        {
            VideoProfile = "High",
            VideoLevel = 40,
            BitDepth = 8,
            Width = 1920,
            Height = 1080,
            FrameRate = 25,
            VideoBitrate = 6_000_000,
            AudioChannels = 2,
            AudioSampleRate = 48_000,
        };

    private static VideoFileRow NewFile(
        Guid videoId,
        Guid directoryId,
        int index,
        int occurrence,
        DateTime at) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            VideoId = videoId,
            LibraryDirectoryId = directoryId,
            RelativePath = $"part-{index % 200:000}/video-{index:000000}-{occurrence}.mp4",
            Size = 1_200_000_000 + index,
            LastWriteTimeUtc = at,
            Sha256 = Convert.ToHexString(BitConverter.GetBytes((long)index)).PadRight(64, '0'),
            PublicDeliveryId = Guid.NewGuid(),
            ContainerFormat = BenchmarkMedia.ContainerFormat,
            VideoCodec = BenchmarkMedia.VideoCodec,
            AudioCodec = BenchmarkMedia.AudioCodec,
            VideoProfile = BenchmarkMedia.VideoProfile,
            VideoLevel = BenchmarkMedia.VideoLevel,
            BitDepth = BenchmarkMedia.BitDepth,
            FrameRate = BenchmarkMedia.FrameRate,
            VideoBitrate = BenchmarkMedia.VideoBitrate,
            AudioChannels = BenchmarkMedia.AudioChannels,
            AudioSampleRate = BenchmarkMedia.AudioSampleRate,
            ProfileKey = BenchmarkMedia.ProfileKey,
            DurationMilliseconds = 3_600_000,
            Width = BenchmarkMedia.Width,
            Height = BenchmarkMedia.Height,
            Availability = VideoFileAvailability.Available,
            DirectPlayClassification = DirectPlayClassificationRule.Classify(BenchmarkMedia),
            LastObservedScanId = Guid.CreateVersion7(),
            InspectedAt = at,
            HashState = VideoFileHashState.Computed,
            HashedSha256 = Convert.ToHexString(BitConverter.GetBytes((long)index)).PadRight(64, '0'),
            OsHash = $"{index:x16}",
            PerceptualHash = BenchmarkPerceptualHash(index, occurrence),
            HashedAt = at,
            PreviewState = VideoFilePreviewState.Generated,
            PreviewSha256 = Convert.ToHexString(BitConverter.GetBytes((long)index)).PadRight(64, '0'),
            PreviewRelativePath = DerivedArtifactStore.PreviewRelativePath(Guid.CreateVersion7()),
            PublicPreviewId = Guid.NewGuid(),
            PreviewGeneratedAt = at,
            IdentifiedSha256 = Convert.ToHexString(BitConverter.GetBytes((long)index)).PadRight(64, '0'),
            IdentifiedAt = at,
        };

    /// <summary>
    /// A Perceptual Hash shaped the way real ones are: spread across the 64 bits, so two unrelated
    /// files sit about thirty-two apart rather than one apart. A running index would have made
    /// every file in the library a neighbour of every other and measured a search nobody will ever
    /// run.
    ///
    /// The second occurrence of a Video is a near-duplicate of its first by two bits, because that
    /// is what a second encode of one work actually looks like: the measurement has to find
    /// something, and what it finds has to be what the lane will find in a real library.
    /// </summary>
    private static string BenchmarkPerceptualHash(int index, int occurrence)
    {
        var digest = BitConverter.ToUInt64(
            System.Security.Cryptography.MD5.HashData(BitConverter.GetBytes(index)));
        var value = occurrence == 0 ? digest : digest ^ 0b11UL;

        return value.ToString("x16", CultureInfo.InvariantCulture);
    }

    private static async Task<Guid> FirstVideoAsync(TestDatabase store)
    {
        await using var scope = store.Scope();

        return await scope.ServiceProvider
            .GetRequiredService<ViewerDbContext>()
            .Videos
            .OrderBy(video => video.DiscoveryDate)
            .Select(video => video.Id)
            .FirstAsync(TestContext.Current.CancellationToken);
    }

    private static Task<Guid> FirstVideoFileAsync(AsyncServiceScope scope, Guid videoId) =>
        scope.ServiceProvider
            .GetRequiredService<ViewerDbContext>()
            .VideoFiles
            .Where(file => file.VideoId == videoId)
            .Select(file => file.Id)
            .FirstAsync(TestContext.Current.CancellationToken);
}
