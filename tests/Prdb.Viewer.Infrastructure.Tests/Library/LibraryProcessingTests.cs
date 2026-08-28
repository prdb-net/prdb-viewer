using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

public sealed class LibraryProcessingTests
{
    [Fact]
    public async Task Scan_inspect_rename_and_two_complete_absences_reconcile_one_video_file()
    {
        var probe = new FixtureProbe();
        await using var store = await TestDatabase.CreateAsync(mediaProbe: probe);
        var source = Path.Combine(store.LibraryMountRoot.Path, "source");
        var nested = Enumerable.Range(1, 10)
            .Aggregate(source, (path, number) => Path.Combine(path, $"level-{number}"));
        Directory.CreateDirectory(nested);
        var original = Path.Combine(nested, "first.mp4");
        await File.WriteAllBytesAsync(original, [1, 2, 3, 4], TestContext.Current.CancellationToken);
        var originalModified = File.GetLastWriteTimeUtc(original);
        await File.WriteAllTextAsync(
            Path.Combine(source, "ignored.txt"),
            "not a candidate",
            TestContext.Current.CancellationToken);
        var directoryId = await ActivateAsync(store, source);

        await DrainAsync(store);

        Guid videoFileId;
        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            var videoFile = await database.VideoFiles.SingleAsync(TestContext.Current.CancellationToken);
            videoFileId = videoFile.Id;
            Assert.Equal(
                "level-1/level-2/level-3/level-4/level-5/level-6/level-7/level-8/level-9/level-10/first.mp4",
                videoFile.RelativePath);
            Assert.Equal(VideoFileAvailability.Available, videoFile.Availability);
            Assert.Equal("vp8", videoFile.VideoCodec);
            Assert.Equal(DirectPlayClassification.BaselineCandidate, videoFile.DirectPlayClassification);
            Assert.NotEqual(Guid.Empty, videoFile.PublicDeliveryId);
            Assert.Equal(4, videoFile.Size);
            Assert.Single(await database.Videos.ToListAsync(TestContext.Current.CancellationToken));
            Assert.NotNull((await database.InstallationConfigurations.SingleAsync(
                TestContext.Current.CancellationToken)).FirstPlayableVideoReachedAt);
        }
        Assert.Equal([1, 2, 3, 4], await File.ReadAllBytesAsync(
            original,
            TestContext.Current.CancellationToken));
        Assert.Equal(originalModified, File.GetLastWriteTimeUtc(original));

        var renamed = Path.Combine(source, "renamed.mp4");
        File.Move(original, renamed);
        await QueueAndDrainAsync(store, directoryId);

        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            var videoFile = await database.VideoFiles.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(videoFileId, videoFile.Id);
            Assert.Equal("renamed.mp4", videoFile.RelativePath);
            Assert.Equal(VideoFileAvailability.Available, videoFile.Availability);
        }

        File.Delete(renamed);
        await QueueAndDrainAsync(store, directoryId);

        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            Assert.Equal(
                VideoFileAvailability.Unreachable,
                (await database.VideoFiles.SingleAsync(TestContext.Current.CancellationToken)).Availability);
        }

        await QueueAndDrainAsync(store, directoryId);

        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            Assert.Equal(
                VideoFileAvailability.Missing,
                (await database.VideoFiles.SingleAsync(TestContext.Current.CancellationToken)).Availability);
        }
    }

    [Fact]
    public async Task Invalid_candidate_becomes_a_scoped_issue_without_stopping_other_files()
    {
        var probe = new FixtureProbe(path => !path.EndsWith("broken.mp4", StringComparison.Ordinal));
        await using var store = await TestDatabase.CreateAsync(mediaProbe: probe);
        var source = Path.Combine(store.LibraryMountRoot.Path, "source");
        Directory.CreateDirectory(source);
        await File.WriteAllBytesAsync(
            Path.Combine(source, "good.mp4"),
            [1],
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(
            Path.Combine(source, "broken.mp4"),
            [2],
            TestContext.Current.CancellationToken);
        _ = await ActivateAsync(store, source);

        await DrainAsync(store);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        Assert.Single(await database.VideoFiles.ToListAsync(TestContext.Current.CancellationToken));
        var issue = await database.WorkIssues.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(WorkIssueCause.InvalidContent, issue.Cause);
        Assert.Equal(WorkIssueSeverity.ScopedIssue, issue.Severity);
        Assert.Equal("broken.mp4", issue.AffectedScope);
        Assert.Contains(
            await database.BackgroundWork.ToListAsync(TestContext.Current.CancellationToken),
            work => work.Category == BackgroundWorkCategory.TechnicalInspection &&
                    work.State == BackgroundWorkState.CompletedWithIssues);
    }

    private static async Task<Guid> ActivateAsync(TestDatabase store, string path)
    {
        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var directory = new LibraryDirectoryRow
        {
            Id = Guid.CreateVersion7(),
            Name = "Fixture Library",
            ContainerPath = path,
            State = LibraryDirectoryState.Active,
            Health = LibraryDirectoryHealth.Healthy,
            ConfigurationGeneration = 1,
            CreatedAt = DateTime.SpecifyKind(new DateTime(2026, 8, 27), DateTimeKind.Utc),
            ActivatedAt = DateTime.SpecifyKind(new DateTime(2026, 8, 27), DateTimeKind.Utc),
        };
        database.LibraryDirectories.Add(directory);
        scope.ServiceProvider.GetRequiredService<LibraryWorkScheduler>()
            .QueueInitialScan(directory, directory.ActivatedAt);
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        return directory.Id;
    }

    private static async Task QueueAndDrainAsync(TestDatabase store, Guid directoryId)
    {
        await using (var scope = store.Scope())
        {
            var result = await scope.ServiceProvider
                .GetRequiredService<LibraryWorkScheduler>()
                .QueueScanAsync(
                    directoryId,
                    BackgroundWorkTrigger.Administrator,
                    TestContext.Current.CancellationToken);
            Assert.Equal(QueueLibraryScanVerdict.Queued, result.Verdict);
        }

        await DrainAsync(store);
    }

    private static async Task DrainAsync(TestDatabase store)
    {
        while (true)
        {
            bool handled;
            await using (var scope = store.Scope())
            {
                handled = await scope.ServiceProvider
                    .GetRequiredService<LibraryScanRunner>()
                    .RunNextSliceAsync(TestContext.Current.CancellationToken);
            }

            if (!handled)
            {
                break;
            }
        }

        while (true)
        {
            bool handled;
            await using (var scope = store.Scope())
            {
                handled = await scope.ServiceProvider
                    .GetRequiredService<TechnicalInspectionRunner>()
                    .RunNextSliceAsync(TestContext.Current.CancellationToken);
            }

            if (!handled)
            {
                break;
            }
        }
    }
}
