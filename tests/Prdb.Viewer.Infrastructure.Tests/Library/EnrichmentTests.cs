using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

/// <summary>
/// The lane that asks prdb again about works this library already established.
///
/// It exists for the installation that was scanned once and has been quiet since: identification
/// never runs twice for a file it has answered, so every Actor that library credits would have
/// stayed a string forever. What it must not do is decide anything — it refreshes retained facts,
/// and an answer about a work a Video is no longer identified as is dropped.
/// </summary>
public sealed class EnrichmentTests
{
    private const string WorkId = "6f1a2c34-0000-4000-8000-000000000001";

    private const string ActorId = "6f1a2c34-0000-4000-8000-00000000000a";

    [Fact]
    public async Task A_library_identified_before_actors_had_an_identity_gains_one()
    {
        var detail = new FixtureWorkDetailClient().Answers(Work());
        await using var store = await CreateAsync(detail);
        var directoryId = await IdentifiedAsync(store);
        await ForgetActorIdentitiesAsync(store);

        await LibraryPipeline.RescanAsync(store, directoryId);

        await using var scope = store.Scope();
        var actor = Assert.Single(await scope.ServiceProvider
            .GetRequiredService<ViewerDbContext>()
            .VideoActors
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Alex Doe", actor.Name);
        Assert.Equal(ActorId, actor.PrdbActorId);
    }

    [Fact]
    public async Task A_work_asked_about_is_not_asked_about_again_inside_the_horizon()
    {
        var detail = new FixtureWorkDetailClient().Answers(Work());
        await using var store = await CreateAsync(detail);
        var directoryId = await IdentifiedAsync(store);
        var asked = detail.Calls;

        await LibraryPipeline.RescanAsync(store, directoryId);

        // A catalogue entry changes rarely and every request is counted against a published
        // limit. Having asked, the lane leaves the work alone until its facts are old.
        Assert.Equal(asked, detail.Calls);
        Assert.Equal(1, asked);
    }

    [Fact]
    public async Task An_answer_about_another_work_is_dropped_rather_than_applied()
    {
        await using var store = await CreateAsync(new FixtureWorkDetailClient());
        await IdentifiedAsync(store);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var videoId = await database.Videos.Select(video => video.Id)
            .SingleAsync(TestContext.Current.CancellationToken);

        var applied = await scope.ServiceProvider
            .GetRequiredService<IdentificationService>()
            .RefreshRetainedWorkAsync(
                videoId,
                new RemoteWork(
                    "6f1a2c34-0000-4000-8000-0000000000ff",
                    "Somebody Else's Work",
                    null,
                    [new RemoteActor("Sam Roe", ActorId)],
                    null,
                    null,
                    1),
                TestContext.Current.CancellationToken);

        // This lane refreshes facts. It does not identify, correct, or propose, so an answer about
        // a work this Video is not identified as changes nothing at all.
        Assert.False(applied);
        var metadata = await database.VideoMetadata
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("A Known Work", metadata.Title);
    }

    [Fact]
    public async Task An_outage_leaves_the_lane_waiting_without_a_second_blocker()
    {
        var detail = new FixtureWorkDetailClient
        {
            Status = WorkDetailFetchStatus.Unavailable,
        };
        await using var store = await CreateAsync(detail);
        var directoryId = await IdentifiedAsync(store);
        await ForgetActorIdentitiesAsync(store);

        await LibraryPipeline.RescanAsync(store, directoryId);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var lane = await database.BackgroundWork
            .Where(work => work.Category == BackgroundWorkCategory.Enrichment)
            .OrderByDescending(work => work.RequestedAt)
            .FirstAsync(TestContext.Current.CancellationToken);
        Assert.Equal(BackgroundWorkState.Waiting, lane.State);
        Assert.Equal("prdb is temporarily unavailable.", lane.WaitingReason);
        Assert.NotNull(lane.NextAttemptAt);

        // A Waiting run carries the condition needed to continue, which is what the screen shows.
        // What it does not do is raise an Operational Blocker of its own: the connection is
        // already Degraded installation-wide, and calling an Administrator twice to one repair is
        // noise rather than attention.
        Assert.Empty(await database.WorkIssues
            .Where(issue => issue.Category == BackgroundWorkCategory.Enrichment)
            .ToListAsync(TestContext.Current.CancellationToken));

        var configuration = await database.InstallationConfigurations
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(PrdbConnectionStatus.Degraded, configuration.PrdbConnectionStatus);
    }

    [Fact]
    public async Task A_withdrawn_credential_holds_the_lane_with_its_condition()
    {
        var detail = new FixtureWorkDetailClient().Answers(Work());
        await using var store = await CreateAsync(detail);
        var directoryId = await IdentifiedAsync(store);
        await ForgetActorIdentitiesAsync(store);
        await LibraryPipeline.SetCredentialAsync(store, null);
        var asked = detail.Calls;

        await LibraryPipeline.RescanAsync(store, directoryId);

        await using var scope = store.Scope();
        var lane = await scope.ServiceProvider
            .GetRequiredService<ViewerDbContext>()
            .BackgroundWork
            .Where(work => work.Category == BackgroundWorkCategory.Enrichment)
            .OrderByDescending(work => work.RequestedAt)
            .FirstAsync(TestContext.Current.CancellationToken);
        Assert.Equal(BackgroundWorkState.Waiting, lane.State);
        Assert.Null(lane.NextAttemptAt);
        Assert.Equal(
            "A verified prdb API key is required before established works can be enriched.",
            lane.WaitingReason);
        Assert.Equal(asked, detail.Calls);
    }

    private static RemoteWork Work() =>
        new(WorkId,
            "A Known Work",
            new RemoteSite("s1", "Example Site", null),
            [new RemoteActor("Alex Doe", ActorId)],
            null,
            null,
            12_345);

    /// <summary>An installation with one identified Video, the way an ordinary one arrives.</summary>
    private static async Task<Guid> IdentifiedAsync(TestDatabase store)
    {
        var source = Path.Combine(store.LibraryMountRoot.Path, "source");
        Directory.CreateDirectory(source);
        await File.WriteAllBytesAsync(
            Path.Combine(source, "first.mp4"),
            [1, 2, 3, 4],
            TestContext.Current.CancellationToken);
        var directoryId = await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");
        await LibraryPipeline.DrainAsync(store);
        return directoryId;
    }

    /// <summary>
    /// Exactly the state an upgrade leaves behind: a retained document of plain names, and a work
    /// nothing has ever asked prdb about in its own right.
    /// </summary>
    private static async Task ForgetActorIdentitiesAsync(TestDatabase store)
    {
        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        await database.VideoMetadata.ExecuteUpdateAsync(
            update => update
                .SetProperty(metadata => metadata.ActorsJson, """["Alex Doe"]""")
                .SetProperty(metadata => metadata.EnrichedAt, (DateTime?)null),
            TestContext.Current.CancellationToken);
        await database.VideoActors.ExecuteUpdateAsync(
            update => update.SetProperty(actor => actor.PrdbActorId, (string?)null),
            TestContext.Current.CancellationToken);
    }

    private static Task<TestDatabase> CreateAsync(FixtureWorkDetailClient detail) =>
        TestDatabase.CreateAsync(
            mediaProbe: new FixtureProbe(),
            hasher: new FixtureHasher(),
            previewGenerator: new FixturePreviewGenerator(),
            identificationClient: new FixtureIdentificationClient()
                .Conclusive("first.mp4", WorkId, "A Known Work", new RemoteSite("s1", "Example Site", null)),
            workDetailClient: detail);
}
