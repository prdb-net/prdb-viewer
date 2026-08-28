using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

public sealed class BackgroundWorkOperationsTests
{
    [Fact]
    public async Task Pause_survives_a_restart_and_resume_continues_from_retained_checkpoints()
    {
        await using var store = await TestDatabase.CreateAsync(
            mediaProbe: new FixtureProbe(),
            hasher: new FixtureHasher(),
            previewGenerator: new FixturePreviewGenerator());
        var source = await SourceAsync(store, 12);
        var directoryId = await LibraryPipeline.ActivateAsync(store, source);

        await PauseAsync(store, paused: true);
        await LibraryPipeline.DrainAsync(store);

        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            var work = await database.BackgroundWork.ToListAsync(
                TestContext.Current.CancellationToken);
            Assert.All(work, row => Assert.Equal(BackgroundWorkState.Paused, row.State));
            Assert.Empty(await database.VideoFiles.ToListAsync(
                TestContext.Current.CancellationToken));
        }

        // A restart reads the pause from durable state, so nothing starts by itself.
        await using (var scope = store.Scope())
        {
            var status = await scope.ServiceProvider
                .GetRequiredService<BackgroundWorkQuery>()
                .GetAsync(TestContext.Current.CancellationToken);
            Assert.True(status.Paused);
            Assert.NotNull(status.PausedAt);
        }

        await PauseAsync(store, paused: false);
        await LibraryPipeline.DrainAsync(store);

        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            Assert.Equal(
                12,
                await database.VideoFiles.CountAsync(TestContext.Current.CancellationToken));
            Assert.Single(await database.BackgroundWork
                .Where(work => work.Category == BackgroundWorkCategory.LibraryScan)
                .ToListAsync(TestContext.Current.CancellationToken));
            Assert.DoesNotContain(
                await database.BackgroundWork.ToListAsync(TestContext.Current.CancellationToken),
                work => work.State == BackgroundWorkState.Paused);
            Assert.Equal(
                directoryId,
                (await database.VideoFiles.FirstAsync(TestContext.Current.CancellationToken))
                    .LibraryDirectoryId);
        }
    }

    [Fact]
    public async Task A_cancelled_scan_keeps_its_observations_and_supplies_no_absence_evidence()
    {
        await using var store = await TestDatabase.CreateAsync(
            mediaProbe: new FixtureProbe(),
            hasher: new FixtureHasher(),
            previewGenerator: new FixturePreviewGenerator());
        var source = await SourceAsync(store, 30);
        var directoryId = await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.DrainAsync(store);

        File.Delete(Path.Combine(source, "part-00", "video-00.mp4"));
        Guid scanId;
        await using (var scope = store.Scope())
        {
            var result = await scope.ServiceProvider
                .GetRequiredService<LibraryWorkScheduler>()
                .QueueScanAsync(
                    directoryId,
                    BackgroundWorkTrigger.Administrator,
                    TestContext.Current.CancellationToken);
            scanId = result.WorkId!.Value;
        }

        // One slice commits part of the traversal before the Administrator stops the run.
        await using (var scope = store.Scope())
        {
            Assert.True(await scope.ServiceProvider
                .GetRequiredService<LibraryScanRunner>()
                .RunNextSliceAsync(TestContext.Current.CancellationToken));
        }

        await using (var scope = store.Scope())
        {
            Assert.Equal(
                BackgroundWorkActionVerdict.Accepted,
                (await scope.ServiceProvider
                    .GetRequiredService<BackgroundWorkOperations>()
                    .CancelAsync(scanId, TestContext.Current.CancellationToken)).Verdict);
        }

        await LibraryPipeline.DrainAsync(store);

        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            var scan = await database.BackgroundWork.SingleAsync(
                work => work.Id == scanId,
                TestContext.Current.CancellationToken);
            Assert.Equal(BackgroundWorkState.Cancelled, scan.State);
            Assert.All(
                await database.VideoFiles.ToListAsync(TestContext.Current.CancellationToken),
                file => Assert.Equal(VideoFileAvailability.Available, file.Availability));
        }

        // A later complete scan reconciles the whole directory normally.
        await LibraryPipeline.RescanAsync(store, directoryId);

        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            var deleted = await database.VideoFiles.SingleAsync(
                file => file.RelativePath == "part-00/video-00.mp4",
                TestContext.Current.CancellationToken);
            Assert.Equal(VideoFileAvailability.Unreachable, deleted.Availability);
        }
    }

    [Fact]
    public async Task Equivalent_item_issues_aggregate_without_establishing_operational_attention()
    {
        var probe = new FixtureProbe(path => !path.Contains("broken", StringComparison.Ordinal));
        await using var store = await TestDatabase.CreateAsync(mediaProbe: probe);
        var source = Path.Combine(store.LibraryMountRoot.Path, "source");
        Directory.CreateDirectory(source);

        foreach (var index in Enumerable.Range(0, 5))
        {
            await File.WriteAllBytesAsync(
                Path.Combine(source, $"broken-{index}.mp4"),
                [(byte)index],
                TestContext.Current.CancellationToken);
        }

        await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        var status = await scope.ServiceProvider
            .GetRequiredService<BackgroundWorkQuery>()
            .GetAsync(TestContext.Current.CancellationToken);
        var issue = Assert.Single(status.Issues);
        Assert.Equal(WorkIssueCause.InvalidContent, issue.Cause);
        Assert.Equal(WorkIssueSeverity.ScopedIssue, issue.Severity);
        Assert.Equal(5, issue.AffectedItemCount);
        Assert.Equal(5, issue.OccurrenceCount);
        Assert.StartsWith("WI-", issue.Reference, StringComparison.Ordinal);
        Assert.False(status.OperationalAttention);
        Assert.Contains(WorkIssueAction.ViewAffectedItems, issue.Actions);

        var items = await scope.ServiceProvider
            .GetRequiredService<BackgroundWorkQuery>()
            .GetAffectedItemsAsync(issue.Id, TestContext.Current.CancellationToken);
        Assert.Equal(5, items!.Count);
    }

    [Fact]
    public async Task Unwritable_application_storage_stops_previews_and_clears_only_with_evidence()
    {
        await using var store = await TestDatabase.CreateAsync(
            mediaProbe: new FixtureProbe(),
            hasher: new FixtureHasher(),
            previewGenerator: new FixturePreviewGenerator(),
            identificationClient: new FixtureIdentificationClient()
                .Unmatched("video-00.mp4"));
        var source = await SourceAsync(store, 1);

        // A regular file where the preview directory belongs makes application storage refuse
        // every durable write without touching the source library.
        var previews = Path.Combine(store.Directory, "previews");
        await File.WriteAllTextAsync(previews, "blocked", TestContext.Current.CancellationToken);
        await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");
        await LibraryPipeline.DrainAsync(store);

        Guid issueId;
        int version;
        await using (var scope = store.Scope())
        {
            var status = await scope.ServiceProvider
                .GetRequiredService<BackgroundWorkQuery>()
                .GetAsync(TestContext.Current.CancellationToken);
            var issue = Assert.Single(
                status.Issues,
                candidate => candidate.Cause == WorkIssueCause.Capacity);
            issueId = issue.Id;
            version = issue.Version;
            Assert.Equal(WorkIssueSeverity.SafetyStop, issue.Severity);
            Assert.True(status.OperationalAttention);
            Assert.Equal([WorkIssueAction.CopyOperatorHandoff], issue.Actions);
            Assert.NotNull(issue.OperatorHandoff);
            Assert.Contains(BackgroundWorkPhases.GeneratingPreviews, issue.OperatorHandoff);
        }

        // A Safety Stop offers no action at all, so `Check again` cannot be used against it.
        await using (var scope = store.Scope())
        {
            var refused = await scope.ServiceProvider
                .GetRequiredService<BackgroundWorkOperations>()
                .AdvanceIssueAsync(
                    issueId,
                    version,
                    WorkIssueAction.CheckAgain,
                    TestContext.Current.CancellationToken);
            Assert.Equal(BackgroundWorkActionVerdict.NotApplicable, refused.Verdict);
        }

        File.Delete(previews);
        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            var issue = await database.WorkIssues
                .AsTracking()
                .SingleAsync(row => row.Id == issueId, TestContext.Current.CancellationToken);
            issue.Severity = WorkIssueSeverity.OperationalBlocker;
            issue.RetryDisposition = WorkIssueRetryDisposition.RetriesExhausted;
            issue.Version++;
            version = issue.Version;
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var scope = store.Scope())
        {
            var accepted = await scope.ServiceProvider
                .GetRequiredService<BackgroundWorkOperations>()
                .AdvanceIssueAsync(
                    issueId,
                    version,
                    WorkIssueAction.CheckAgain,
                    TestContext.Current.CancellationToken);
            Assert.Equal(BackgroundWorkActionVerdict.Accepted, accepted.Verdict);

            // A successful storage check alone transfers ownership but never closes the issue.
            Assert.Null(accepted.Issue!.ResolvedAt);
            Assert.Equal(RemediationOwner.AutomaticRecovery, accepted.Issue.RemediationOwner);
        }

        await LibraryPipeline.DrainAsync(store);

        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            var issue = await database.WorkIssues.SingleAsync(
                row => row.Id == issueId,
                TestContext.Current.CancellationToken);
            Assert.NotNull(issue.ResolvedAt);
            Assert.NotNull(issue.ResolutionEvidence);
            Assert.Equal(
                VideoFilePreviewState.Generated,
                (await database.VideoFiles.SingleAsync(TestContext.Current.CancellationToken))
                    .PreviewState);
            var status = await scope.ServiceProvider
                .GetRequiredService<BackgroundWorkQuery>()
                .GetAsync(TestContext.Current.CancellationToken);
            Assert.False(status.OperationalAttention);
        }
    }

    [Fact]
    public async Task A_stale_action_is_refused_and_returns_the_current_detail()
    {
        var probe = new FixtureProbe(path => !path.Contains("broken", StringComparison.Ordinal));
        await using var store = await TestDatabase.CreateAsync(mediaProbe: probe);
        var source = Path.Combine(store.LibraryMountRoot.Path, "source");
        Directory.CreateDirectory(source);
        await File.WriteAllBytesAsync(
            Path.Combine(source, "broken.mp4"),
            [1],
            TestContext.Current.CancellationToken);
        await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        var status = await scope.ServiceProvider
            .GetRequiredService<BackgroundWorkQuery>()
            .GetAsync(TestContext.Current.CancellationToken);
        var issue = Assert.Single(status.Issues);
        var refused = await scope.ServiceProvider
            .GetRequiredService<BackgroundWorkOperations>()
            .AdvanceIssueAsync(
                issue.Id,
                issue.Version + 1,
                WorkIssueAction.RetryNow,
                TestContext.Current.CancellationToken);

        Assert.Equal(BackgroundWorkActionVerdict.Stale, refused.Verdict);
        Assert.Equal(issue.Version, refused.Issue!.Version);
    }

    private static async Task PauseAsync(TestDatabase store, bool paused)
    {
        await using var scope = store.Scope();
        var result = await scope.ServiceProvider
            .GetRequiredService<BackgroundWorkOperations>()
            .SetPausedAsync(paused, TestContext.Current.CancellationToken);
        Assert.Equal(paused, result.Paused);
    }

    private static async Task<string> SourceAsync(TestDatabase store, int files)
    {
        var source = Path.Combine(store.LibraryMountRoot.Path, "source");

        foreach (var index in Enumerable.Range(0, files))
        {
            var directory = Path.Combine(source, $"part-{index:00}");
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(
                Path.Combine(directory, $"video-{index:00}.mp4"),
                [(byte)index, 1, 2],
                TestContext.Current.CancellationToken);
        }

        return source;
    }
}
