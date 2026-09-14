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

    [Fact]
    public async Task The_first_scan_to_meet_an_unreadable_subtree_settles_with_issues()
    {
        Assert.SkipWhen(
            OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess,
            "The test needs an unprivileged process on a Unix-like filesystem.");

        if (!OperatingSystem.IsWindows())
        {
            await FirstScanReportsWhatItCouldNotReadAsync();
        }
    }

    /// <summary>
    /// The run that records an obstacle and the run that reports one used to be different runs. A
    /// Library Scan records its issues and settles within one slice, and the state was decided by a
    /// database query that could not yet see what the slice had added — so the first scan over a
    /// newly unreadable subtree read `Completed`, and only the second one, which aggregated onto a
    /// row already committed, read `Completed with Issues`.
    /// </summary>
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static async Task FirstScanReportsWhatItCouldNotReadAsync()
    {
        await using var store = await TestDatabase.CreateAsync(mediaProbe: new FixtureProbe());
        var source = Path.Combine(store.LibraryMountRoot.Path, "source");
        var closed = Path.Combine(source, "re-encodes");
        Directory.CreateDirectory(closed);
        await File.WriteAllBytesAsync(
            Path.Combine(source, "readable.mp4"),
            [1],
            TestContext.Current.CancellationToken);
        File.SetUnixFileMode(closed, UnixFileMode.None);

        try
        {
            _ = await ActivateAsync(store, source);
            await DrainAsync(store);

            await using var scope = store.Scope();
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            var scan = await database.BackgroundWork.SingleAsync(
                work => work.Category == BackgroundWorkCategory.LibraryScan,
                TestContext.Current.CancellationToken);
            var issue = await database.WorkIssues.SingleAsync(
                row => row.Category == BackgroundWorkCategory.LibraryScan,
                TestContext.Current.CancellationToken);

            Assert.Equal(BackgroundWorkState.CompletedWithIssues, scan.State);
            Assert.Equal(WorkIssueCause.SourceAccess, issue.Cause);
            Assert.Equal(WorkIssueSeverity.OperationalBlocker, issue.Severity);
            Assert.Equal(1, issue.OccurrenceCount);
            // The readable sibling was still admitted: an incomplete observation stops the scan
            // claiming completeness, not the library from growing.
            Assert.Single(await database.VideoFiles.ToListAsync(
                TestContext.Current.CancellationToken));
        }
        finally
        {
            File.SetUnixFileMode(
                closed,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public async Task A_scan_that_finishes_in_one_slice_settles_at_the_candidates_it_recorded()
    {
        await using var store = await TestDatabase.CreateAsync(mediaProbe: new FixtureProbe());
        var source = Path.Combine(store.LibraryMountRoot.Path, "source");
        Directory.CreateDirectory(source);
        foreach (var name in new[] { "first.mp4", "second.mp4", "third.mp4" })
        {
            await File.WriteAllBytesAsync(
                Path.Combine(source, name),
                [1],
                TestContext.Current.CancellationToken);
        }
        _ = await ActivateAsync(store, source);

        await DrainAsync(store);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var scan = await database.BackgroundWork.SingleAsync(
            work => work.Category == BackgroundWorkCategory.LibraryScan,
            TestContext.Current.CancellationToken);

        // The tally used to be taken before the slice rather than after it, so a scan short enough
        // to finish in one slice settled at none of the three candidates it had just recorded, and
        // the screen reported it as 0 of 3 for good.
        Assert.Equal(BackgroundWorkState.Completed, scan.State);
        Assert.Equal(3, scan.DiscoveredCandidateCount);
        Assert.Equal(3, scan.CompletedItemCount);
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
