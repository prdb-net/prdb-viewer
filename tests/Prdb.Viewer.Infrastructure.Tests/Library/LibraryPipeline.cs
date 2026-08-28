using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

/// <summary>
/// Drives the durable lanes the way the hosted workers do, so a test can observe the same
/// end-to-end path a running installation takes.
/// </summary>
internal static class LibraryPipeline
{
    public static async Task<Guid> ActivateAsync(TestDatabase store, string path)
    {
        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var activatedAt = DateTime.SpecifyKind(new DateTime(2026, 8, 27), DateTimeKind.Utc);
        var directory = new LibraryDirectoryRow
        {
            Id = Guid.CreateVersion7(),
            Name = "Fixture Library",
            ContainerPath = path,
            State = LibraryDirectoryState.Active,
            Health = LibraryDirectoryHealth.Healthy,
            ConfigurationGeneration = 1,
            CreatedAt = activatedAt,
            ActivatedAt = activatedAt,
        };
        database.LibraryDirectories.Add(directory);
        scope.ServiceProvider.GetRequiredService<LibraryWorkScheduler>()
            .QueueInitialScan(directory, activatedAt);
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        return directory.Id;
    }

    public static async Task SetCredentialAsync(TestDatabase store, string? credential)
    {
        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var configuration = await database.InstallationConfigurations
            .AsTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        configuration.ActivePrdbCredential = credential;
        configuration.PrdbConnectionStatus = credential is null
            ? PrdbConnectionStatus.Missing
            : PrdbConnectionStatus.Verified;
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Offers one Video File to prdb again, the way a revocation or a content change does, so a
    /// test can observe what a changed remote answer does to established knowledge.
    /// </summary>
    public static async Task ReofferAsync(TestDatabase store, string relativePath)
    {
        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        await database.VideoFiles
            .Where(file => file.RelativePath == relativePath)
            .ExecuteUpdateAsync(
                update => update.SetProperty(file => file.IdentifiedSha256, (string?)null),
                TestContext.Current.CancellationToken);
        var directory = await database.LibraryDirectories
            .FirstAsync(TestContext.Current.CancellationToken);
        await database.BackgroundWork
            .Where(work => work.Category == BackgroundWorkCategory.Identification)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(work => work.State, BackgroundWorkState.Queued)
                    .SetProperty(work => work.FinishedAt, (DateTime?)null)
                    .SetProperty(work => work.NextAttemptAt, (DateTime?)null),
                TestContext.Current.CancellationToken);
        await DrainAsync(store);
    }

    public static async Task RescanAsync(TestDatabase store, Guid directoryId)
    {
        await using (var scope = store.Scope())
        {
            await scope.ServiceProvider
                .GetRequiredService<LibraryWorkScheduler>()
                .QueueScanAsync(directoryId, TestContext.Current.CancellationToken);
        }

        await DrainAsync(store);
    }

    public static async Task DrainAsync(TestDatabase store)
    {
        for (var pass = 0; pass < 20; pass++)
        {
            var handled = await RunAsync<LibraryScanRunner>(store, runner => runner.RunNextSliceAsync) |
                await RunAsync<TechnicalInspectionRunner>(store, runner => runner.RunNextSliceAsync) |
                await RunAsync<HashingRunner>(store, runner => runner.RunNextSliceAsync) |
                await RunAsync<PreviewGenerationRunner>(store, runner => runner.RunNextSliceAsync) |
                await RunAsync<IdentificationRunner>(store, runner => runner.RunNextSliceAsync);

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
