using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

/// <summary>
/// What an installation discovers without anyone asking it to. A file copied into the mounted
/// library has to become a Video on its own, and the period that promise is kept on is durable
/// rather than held by a timer this process owns.
/// </summary>
public sealed class PeriodicLibraryScanTests
{
    [Fact]
    public async Task A_file_added_after_the_initial_scan_is_discovered_once_the_period_elapses()
    {
        var time = Clock();
        await using var store = await TestDatabase.CreateAsync(
            timeProvider: time,
            mediaProbe: new FixtureProbe());
        var source = SourceDirectory(store);
        await WriteVideoAsync(source, "first.mp4");
        await ActivateAsync(store, source, time);
        await DrainAsync(store);

        await WriteVideoAsync(source, "second.mp4");
        await DrainAsync(store);

        // Nothing is due yet, so the new file is still only a file. This is the half of the
        // promise a timer that fired on every tick would hide.
        Assert.Single(await VideoFilesAsync(store));
        Assert.Single(await ScansAsync(store));

        time.Advance(LibraryScanSchedule.Interval);
        await DrainAsync(store);

        Assert.Equal(2, (await VideoFilesAsync(store)).Count);
        var scans = await ScansAsync(store);
        Assert.Equal(
            [BackgroundWorkTrigger.Activation, BackgroundWorkTrigger.Periodic],
            scans.Select(scan => scan.Trigger));
        Assert.All(scans, scan => Assert.Equal(BackgroundWorkState.Completed, scan.State));
    }

    [Fact]
    public async Task The_period_runs_again_from_the_scan_that_just_finished()
    {
        var time = Clock();
        await using var store = await TestDatabase.CreateAsync(
            timeProvider: time,
            mediaProbe: new FixtureProbe());
        var source = SourceDirectory(store);
        await WriteVideoAsync(source, "first.mp4");
        await ActivateAsync(store, source, time);
        await DrainAsync(store);

        time.Advance(LibraryScanSchedule.Interval - TimeSpan.FromMinutes(1));
        await DrainAsync(store);
        Assert.Single(await ScansAsync(store));

        time.Advance(TimeSpan.FromMinutes(1));
        await DrainAsync(store);
        Assert.Equal(2, (await ScansAsync(store)).Count);

        // The second Scan settled at the moment it ran, and the period is counted from there. A
        // Scan every tick from here on would be the failure this asserts against.
        time.Advance(LibraryScanSchedule.Interval - TimeSpan.FromMinutes(1));
        await DrainAsync(store);
        Assert.Equal(2, (await ScansAsync(store)).Count);
    }

    [Fact]
    public async Task An_administrator_scan_postpones_the_one_nobody_asked_for()
    {
        var time = Clock();
        await using var store = await TestDatabase.CreateAsync(
            timeProvider: time,
            mediaProbe: new FixtureProbe());
        var source = SourceDirectory(store);
        await WriteVideoAsync(source, "first.mp4");
        var directoryId = await ActivateAsync(store, source, time);
        await DrainAsync(store);

        time.Advance(LibraryScanSchedule.Interval / 2);
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
        time.Advance(LibraryScanSchedule.Interval / 2);
        await DrainAsync(store);

        // Scanning on the hour it was configured rather than the hour it was last observed would
        // walk the whole library again minutes after an Administrator already had.
        Assert.Equal(
            [BackgroundWorkTrigger.Activation, BackgroundWorkTrigger.Administrator],
            (await ScansAsync(store)).Select(scan => scan.Trigger));
    }

    [Fact]
    public async Task A_period_that_elapses_while_work_is_paused_is_held_rather_than_lost()
    {
        var time = Clock();
        await using var store = await TestDatabase.CreateAsync(
            timeProvider: time,
            mediaProbe: new FixtureProbe());
        var source = SourceDirectory(store);
        await WriteVideoAsync(source, "first.mp4");
        await ActivateAsync(store, source, time);
        await DrainAsync(store);
        await SetPausedAsync(store, paused: true);

        time.Advance(LibraryScanSchedule.Interval);
        await DrainAsync(store);

        Assert.Single(await ScansAsync(store));

        await SetPausedAsync(store, paused: false);
        await DrainAsync(store);

        // A pause is not a skipped period: the Scan that fell due while work was stopped is the
        // one that runs when it starts again.
        Assert.Equal(
            [BackgroundWorkTrigger.Activation, BackgroundWorkTrigger.Periodic],
            (await ScansAsync(store)).Select(scan => scan.Trigger));
    }

    private static FakeTimeProvider Clock() =>
        new(new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero));

    private static string SourceDirectory(TestDatabase store)
    {
        var source = Path.Combine(store.LibraryMountRoot.Path, "source");
        Directory.CreateDirectory(source);
        return source;
    }

    private static Task WriteVideoAsync(string source, string name) =>
        File.WriteAllBytesAsync(
            Path.Combine(source, name),
            [1, 2, 3, 4],
            TestContext.Current.CancellationToken);

    private static async Task<Guid> ActivateAsync(
        TestDatabase store,
        string path,
        TimeProvider time)
    {
        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var now = time.GetUtcNow().UtcDateTime;
        var directory = new LibraryDirectoryRow
        {
            Id = Guid.CreateVersion7(),
            Name = "Fixture Library",
            ContainerPath = path,
            State = LibraryDirectoryState.Active,
            Health = LibraryDirectoryHealth.Healthy,
            ConfigurationGeneration = 1,
            CreatedAt = now,
            ActivatedAt = now,
        };
        database.LibraryDirectories.Add(directory);
        scope.ServiceProvider.GetRequiredService<LibraryWorkScheduler>()
            .QueueInitialScan(directory, now);
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        return directory.Id;
    }

    private static async Task SetPausedAsync(TestDatabase store, bool paused)
    {
        await using var scope = store.Scope();
        await scope.ServiceProvider
            .GetRequiredService<BackgroundWorkOperations>()
            .SetPausedAsync(paused, TestContext.Current.CancellationToken);
    }

    private static async Task<IReadOnlyList<BackgroundWorkRow>> ScansAsync(TestDatabase store)
    {
        await using var scope = store.Scope();
        return await scope.ServiceProvider.GetRequiredService<ViewerDbContext>()
            .BackgroundWork
            .Where(work => work.Category == BackgroundWorkCategory.LibraryScan)
            .OrderBy(work => work.RequestedAt)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<IReadOnlyList<VideoFileRow>> VideoFilesAsync(TestDatabase store)
    {
        await using var scope = store.Scope();
        return await scope.ServiceProvider.GetRequiredService<ViewerDbContext>()
            .VideoFiles
            .OrderBy(file => file.RelativePath)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Drives the two lanes a Library Scan needs to turn a file into a Video, the way the hosted
    /// workers do, until neither has anything left to advance.
    /// </summary>
    private static async Task DrainAsync(TestDatabase store)
    {
        for (var pass = 0; pass < 20; pass++)
        {
            var handled =
                await RunAsync<LibraryScanRunner>(store, runner => runner.RunNextSliceAsync) |
                await RunAsync<TechnicalInspectionRunner>(store, runner => runner.RunNextSliceAsync);

            if (!handled)
            {
                return;
            }
        }

        Assert.Fail("The library lanes did not settle.");
    }

    private static async Task<bool> RunAsync<TRunner>(
        TestDatabase store,
        Func<TRunner, Func<CancellationToken, Task<bool>>> slice)
        where TRunner : notnull
    {
        var advanced = false;

        while (true)
        {
            await using var scope = store.Scope();
            var runner = scope.ServiceProvider.GetRequiredService<TRunner>();

            if (!await slice(runner)(TestContext.Current.CancellationToken))
            {
                return advanced;
            }

            advanced = true;
        }
    }
}
