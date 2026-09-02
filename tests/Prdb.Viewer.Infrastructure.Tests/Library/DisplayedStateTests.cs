using Microsoft.Extensions.DependencyInjection;

using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Configuration;
using Prdb.Viewer.Infrastructure.Library;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

/// <summary>
/// Holds the numbers the Administrator screens put in front of someone against what a real
/// installation produces.
///
/// The other suites assert the facts a lane establishes — the Video File exists, its codec was
/// read, its identity was claimed. None of them looked at the progress and status fields those
/// lanes leave behind, which is what the screens actually render. A Library Scan spent three
/// releases reporting none of the candidates it had recorded, and nothing went red, because no
/// test had ever named <c>CompletedItemCount</c>. These tests exist so that a field cannot be
/// wrong on a screen while the suite stays green.
/// </summary>
public sealed class DisplayedStateTests
{
    private static readonly string[] Files = ["first.mp4", "second.mp4", "third.mkv"];

    [Fact]
    public async Task Every_lane_the_Background_work_screen_shows_reports_its_own_counts()
    {
        await using var store = await CreateAsync();
        await SourceAsync(store);
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");
        await LibraryPipeline.DrainAsync(store);

        var lanes = await LanesAsync(store);

        // Every lane the screen draws a row for ran, so a lane that never started cannot pass as
        // one that finished with nothing to show.
        Assert.Equal(
            [
                "Hashing",
                "Identification",
                "LibraryScan",
                "PreviewGeneration",
                "SiteRecognition",
                "TechnicalInspection",
            ],
            lanes.Keys.Select(category => category.ToString()).Order());

        // The traversal discovered three candidates and is done with all three. Counted before the
        // slice rather than after it, this settled at nought of three and stayed there for good.
        var scan = lanes[BackgroundWorkCategory.LibraryScan];
        Assert.Equal(BackgroundWorkState.Completed, scan.State);
        Assert.Equal(BackgroundWorkPhases.Settled, scan.Phase);
        Assert.Equal(3, scan.DiscoveredCandidateCount);
        Assert.Equal(3, scan.CompletedItemCount);
        Assert.Equal(0, scan.IssueCount);

        // Discovery is open-ended, so a Library Scan is the one lane that offers no percentage: a
        // ratio against what it has found so far would always read as complete.
        Assert.Null(scan.CompletedPercent);

        foreach (var lane in Derived(lanes)
                     .Where(lane => lane.Category != BackgroundWorkCategory.SiteRecognition))
        {
            Assert.Equal(BackgroundWorkState.Completed, lane.State);
            Assert.Equal(BackgroundWorkPhases.Settled, lane.Phase);
            Assert.Equal(3, lane.DiscoveredCandidateCount);
            Assert.Equal(3, lane.CompletedItemCount);
            Assert.Equal(0, lane.IssueCount);

            // A derived lane knows how many admitted Video Files it must advance, so it does offer
            // a percentage, and having advanced all of them it reads as complete.
            Assert.Equal(100, lane.CompletedPercent);
        }

        // Site Recognition reads a Video File's own path to answer a question prdb has not
        // answered. Every file here came back with its Site, so the lane ran and had nothing to
        // do: nought of nought, which is a different fact from a lane that never started, and the
        // one an installation whose catalogue knows its library should show.
        var recognition = lanes[BackgroundWorkCategory.SiteRecognition];
        Assert.Equal(BackgroundWorkState.Completed, recognition.State);
        Assert.Equal(BackgroundWorkPhases.Settled, recognition.Phase);
        Assert.Equal(0, recognition.DiscoveredCandidateCount);
        Assert.Equal(0, recognition.CompletedItemCount);
        Assert.Equal(0, recognition.IssueCount);
        Assert.Null(recognition.CompletedPercent);

        // A settled run cannot be cancelled and is not waiting for anything, so neither a Cancel
        // button nor a waiting reason belongs on any of these rows.
        Assert.All(lanes.Values, lane => Assert.False(lane.Cancellable));
        Assert.All(lanes.Values, lane => Assert.False(lane.CancellationRequested));
        Assert.All(lanes.Values, lane => Assert.Null(lane.WaitingReason));
        Assert.All(lanes.Values, lane => Assert.NotNull(lane.FinishedAt));
    }

    [Fact]
    public async Task The_Installation_screen_reports_what_the_last_completed_Scan_found()
    {
        await using var store = await CreateAsync();
        await SourceAsync(store);
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");
        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        var summary = await scope.ServiceProvider
            .GetRequiredService<InstallationConfigurationService>()
            .GetAsync(TestContext.Current.CancellationToken);
        var directory = Assert.Single(summary.LibraryDirectories);

        Assert.Equal(LibraryDirectoryState.Active, directory.State);
        Assert.Equal(LibraryDirectoryHealth.Healthy, directory.Health);
        Assert.True(directory.InitialProcessingStarted);

        // The sentences this screen writes about a directory, each out of its own field: how much
        // is available beneath it, what its last completed Scan found, and whether that Scan saw
        // the whole directory or only the part of it that could be reached.
        Assert.Equal(3, directory.AvailableVideoFileCount);
        Assert.Equal(3, directory.LastScanCandidateCount);
        Assert.True(directory.LastScanCoveredEverything);
        Assert.NotNull(directory.LastScanStartedAt);
        Assert.NotNull(directory.LastScanCompletedAt);
        Assert.True(directory.LastScanCompletedAt >= directory.LastScanStartedAt);
    }

    /// <summary>
    /// The state an installation spends nearly all of its life in, and the one no fixture had: a
    /// Scan of a library where nothing has changed since the last one.
    /// </summary>
    [Fact]
    public async Task A_rescan_that_finds_nothing_new_leaves_most_lanes_with_nothing_to_do()
    {
        await using var store = await CreateAsync();
        var directoryId = await SourceAsync(store);
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");
        await LibraryPipeline.DrainAsync(store);

        await LibraryPipeline.RescanAsync(store, directoryId);

        var lanes = await LanesAsync(store);

        // The second traversal walked the same three files and recorded them again, so the Scan
        // answers for three even though nothing about the library changed. Technical Inspection
        // follows it, because deciding that a candidate is a file already known is itself the
        // work — it is what recognises a rename, and it cannot be skipped on the strength of a
        // path that may no longer mean the same file.
        foreach (var category in new[]
                 {
                     BackgroundWorkCategory.LibraryScan,
                     BackgroundWorkCategory.TechnicalInspection,
                 })
        {
            Assert.Equal(3, lanes[category].DiscoveredCandidateCount);
            Assert.Equal(3, lanes[category].CompletedItemCount);
        }

        // The lanes past it have nothing outstanding: content that hashes the same is not hashed
        // again, a preview that matches the content is not regenerated, and an identity already
        // claimed against this exact content is not asked for a second time.
        foreach (var lane in Derived(lanes)
                     .Where(lane => lane.Category != BackgroundWorkCategory.TechnicalInspection))
        {
            Assert.Equal(BackgroundWorkState.Completed, lane.State);
            Assert.Equal(BackgroundWorkPhases.Settled, lane.Phase);

            // Nought of nought is what a lane that was asked and had nothing to do leaves behind.
            // It is a different fact from a lane that never ran, and the screen has to say which
            // of the two it is rather than printing the pair and leaving it to the reader.
            Assert.Equal(0, lane.DiscoveredCandidateCount);
            Assert.Equal(0, lane.CompletedItemCount);
            Assert.Equal(0, lane.IssueCount);

            // No denominator, so no percentage is offered rather than one being invented.
            Assert.Null(lane.CompletedPercent);
        }

        // Nothing about a rescan that changed nothing is worth an Administrator's attention.
        await using var scope = store.Scope();
        var status = await scope.ServiceProvider
            .GetRequiredService<BackgroundWorkQuery>()
            .GetAsync(TestContext.Current.CancellationToken);
        Assert.Empty(status.Issues);
        Assert.False(status.OperationalAttention);
        Assert.Equal(0, status.OperationalAttentionCount);
        Assert.False(status.Paused);
    }

    private static IEnumerable<BackgroundWorkSummary> Derived(
        Dictionary<BackgroundWorkCategory, BackgroundWorkSummary> lanes) =>
        lanes
            .Where(lane => lane.Key != BackgroundWorkCategory.LibraryScan)
            .Select(lane => lane.Value);

    /// <summary>
    /// One row per lane at its newest run, which is what the Background work screen draws. The
    /// endpoint answers with a history, and an older run of a lane is not what its row is about.
    /// </summary>
    private static async Task<Dictionary<BackgroundWorkCategory, BackgroundWorkSummary>> LanesAsync(
        TestDatabase store)
    {
        await using var scope = store.Scope();
        var status = await scope.ServiceProvider
            .GetRequiredService<BackgroundWorkQuery>()
            .GetAsync(TestContext.Current.CancellationToken);

        return status.Work
            .GroupBy(work => work.Category)
            .ToDictionary(
                lane => lane.Key,
                lane => lane.OrderByDescending(work => work.RequestedAt).First());
    }

    private static async Task<Guid> SourceAsync(TestDatabase store)
    {
        var source = Path.Combine(store.LibraryMountRoot.Path, "source");
        Directory.CreateDirectory(source);

        for (var index = 0; index < Files.Length; index++)
        {
            await File.WriteAllBytesAsync(
                Path.Combine(source, Files[index]),
                [1, 2, 3, (byte)index],
                TestContext.Current.CancellationToken);
        }

        return await LibraryPipeline.ActivateAsync(store, source);
    }

    /// <summary>
    /// An installation whose files prdb recognises, which is the ordinary case and the one whose
    /// second Scan has nothing left to do. A fixture that identifies nothing keeps every Video
    /// Unknown, and an Unknown Video is deliberately offered again on the next run in case the
    /// remote catalogue has learnt about it since — a real state, but a different one.
    /// </summary>
    private static Task<TestDatabase> CreateAsync()
    {
        var identification = new FixtureIdentificationClient();

        for (var index = 0; index < Files.Length; index++)
        {
            identification.Conclusive(
                Files[index],
                $"01a01a22-70e8-7bd0-a34e-00000000000{index}",
                $"Known Work {index}",
                new RemoteSite("example-site", "Example Site", null));
        }

        return TestDatabase.CreateAsync(
            mediaProbe: new FixtureProbe(),
            hasher: new FixtureHasher(),
            previewGenerator: new FixturePreviewGenerator(),
            identificationClient: identification);
    }
}
