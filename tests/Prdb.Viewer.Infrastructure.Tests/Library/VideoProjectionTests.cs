using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

/// <summary>
/// ADR 0013 names the way this projection goes wrong: a write path changes a projected fact and
/// forgets to rebuild it, so discovery disagrees with the Video page. These tests walk the paths
/// that have one.
/// </summary>
public sealed class VideoProjectionTests
{
    private const string WorkId = "6f1a2c34-0000-4000-8000-000000000001";

    [Fact]
    public async Task Inspection_projects_an_unknown_video_from_its_own_file()
    {
        await using var store = await CreateAsync();
        var source = Path.Combine(store.LibraryMountRoot.Path, "source");
        Directory.CreateDirectory(source);
        await File.WriteAllBytesAsync(
            Path.Combine(source, "Beach Day 2019.mp4"),
            [1, 2, 3, 4],
            TestContext.Current.CancellationToken);
        await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        var video = await Projected(scope);
        Assert.Equal("Beach Day 2019", video.DisplayLabel);
        Assert.Equal(DirectPlayClassification.BaselineCandidate, video.BestClassification);
        Assert.Equal(VideoQualityBand.FullHd1080, video.Quality);
        Assert.Equal(VideoAvailability.Available, video.Availability);
        Assert.False(video.HasEstablishedWork);
        Assert.Null(video.EstablishedSite);
        Assert.NotNull(video.ProjectedAt);

        // The search text carries the local label and the file name, normalised.
        Assert.Contains("beach day 2019", video.SearchText);
        Assert.Empty(await scope.ServiceProvider
            .GetRequiredService<ViewerDbContext>()
            .VideoActors
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Identification_projects_the_established_title_site_and_actors()
    {
        await using var store = await CreateAsync(new FixtureIdentificationClient()
            .Conclusive("first.mp4", WorkId, "A Known Work", new RemoteSite("s1", "Example Site", null)));
        var source = Path.Combine(store.LibraryMountRoot.Path, "source");
        Directory.CreateDirectory(source);
        await File.WriteAllBytesAsync(
            Path.Combine(source, "first.mp4"),
            [1, 2, 3, 4],
            TestContext.Current.CancellationToken);
        await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");
        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        var video = await Projected(scope);
        Assert.Equal("A Known Work", video.DisplayLabel);
        Assert.True(video.HasEstablishedWork);
        Assert.Equal("Example Site", video.EstablishedSite);
        Assert.False(video.ReviewNeeded);
        Assert.Contains("a known work", video.SearchText);
        Assert.Contains("example site", video.SearchText);
        Assert.Contains("alex doe", video.SearchText);

        var actor = Assert.Single(await scope.ServiceProvider
            .GetRequiredService<ViewerDbContext>()
            .VideoActors
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Alex Doe", actor.Name);
        Assert.Equal("alex doe", actor.NormalizedName);

        // The identity prdb sent with the credit, kept rather than reduced to its name.
        Assert.Equal(FixtureIdentificationClient.Actor("Alex Doe").Id, actor.PrdbActorId);
    }

    [Fact]
    public async Task A_name_retained_before_actors_had_an_identity_still_projects()
    {
        await using var store = await IdentifiedAsync();

        await ReprojectAsync(store, """["Alex Doe","Sam Roe"]""");

        await using var scope = store.Scope();
        var projected = await ActorsAsync(scope);
        Assert.Equal(["Alex Doe", "Sam Roe"], projected.Select(actor => actor.Name));

        // Nothing is invented: a document that never carried an identity does not gain one, and
        // the facet these rows answer works exactly as it did.
        Assert.All(projected, actor => Assert.Null(actor.PrdbActorId));
    }

    [Fact]
    public async Task An_identity_arriving_later_lands_on_the_row_the_name_already_had()
    {
        await using var store = await IdentifiedAsync();
        await ReprojectAsync(store, """["Alex Doe"]""");

        await ReprojectAsync(
            store,
            """[{"name":"Alex Doe","actorId":"6f1a2c34-0000-4000-8000-00000000000a"}]""");

        await using var scope = store.Scope();
        var actor = Assert.Single(await ActorsAsync(scope));
        Assert.Equal("6f1a2c34-0000-4000-8000-00000000000a", actor.PrdbActorId);
        Assert.Equal("Alex Doe", actor.Name);
    }

    [Fact]
    public async Task A_respelt_actor_moves_its_row_rather_than_leaving_two()
    {
        await using var store = await IdentifiedAsync();
        await ReprojectAsync(
            store,
            """[{"name":"Alex Doe","actorId":"6f1a2c34-0000-4000-8000-00000000000a"}]""");

        // The same person, spelled in full the second time prdb was asked.
        await ReprojectAsync(
            store,
            """[{"name":"Alexandra Doe","actorId":"6f1a2c34-0000-4000-8000-00000000000a"}]""");

        await using var scope = store.Scope();
        var actor = Assert.Single(await ActorsAsync(scope));
        Assert.Equal("Alexandra Doe", actor.Name);
        Assert.Equal("alexandra doe", actor.NormalizedName);
        Assert.Equal("6f1a2c34-0000-4000-8000-00000000000a", actor.PrdbActorId);
    }

    [Fact]
    public async Task Two_actors_of_one_name_do_not_collide_in_the_projection()
    {
        await using var store = await IdentifiedAsync();

        await ReprojectAsync(
            store,
            """[{"name":"Alex Doe","actorId":"6f1a2c34-0000-4000-8000-00000000000a"},""" +
            """{"name":"Alex Doe","actorId":"6f1a2c34-0000-4000-8000-00000000000b"}]""");

        // The facet is keyed by the name, which cannot tell these two apart. One row stands for
        // the name, and the retained document keeps both credits for anything that can.
        await using var scope = store.Scope();
        var actor = Assert.Single(await ActorsAsync(scope));
        Assert.Equal("Alex Doe", actor.Name);
        Assert.Equal("6f1a2c34-0000-4000-8000-00000000000a", actor.PrdbActorId);
    }

    /// <summary>An installation with one identified Video, which is what an Actor needs.</summary>
    private static async Task<TestDatabase> IdentifiedAsync()
    {
        var store = await CreateAsync(new FixtureIdentificationClient()
            .Conclusive("first.mp4", WorkId, "A Known Work", new RemoteSite("s1", "Example Site", null)));
        var source = Path.Combine(store.LibraryMountRoot.Path, "source");
        Directory.CreateDirectory(source);
        await File.WriteAllBytesAsync(
            Path.Combine(source, "first.mp4"),
            [1, 2, 3, 4],
            TestContext.Current.CancellationToken);
        await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");
        await LibraryPipeline.DrainAsync(store);
        return store;
    }

    /// <summary>
    /// Rewrites the retained actor document and rebuilds the projection from it, which is what
    /// every path that changes the metadata does.
    /// </summary>
    private static async Task ReprojectAsync(TestDatabase store, string actorsJson)
    {
        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var metadata = await database.VideoMetadata
            .AsTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        metadata.ActorsJson = actorsJson;
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);

        await scope.ServiceProvider
            .GetRequiredService<VideoProjection>()
            .RefreshAsync(metadata.VideoId, TestContext.Current.CancellationToken);
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<IReadOnlyList<VideoActorRow>> ActorsAsync(AsyncServiceScope scope) =>
        await scope.ServiceProvider
            .GetRequiredService<ViewerDbContext>()
            .VideoActors
            .OrderBy(actor => actor.NormalizedName)
            .ToListAsync(TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_complete_absence_reprojects_availability_and_readiness()
    {
        await using var store = await CreateAsync();
        var source = Path.Combine(store.LibraryMountRoot.Path, "source");
        Directory.CreateDirectory(source);
        var file = Path.Combine(source, "gone.mp4");
        await File.WriteAllBytesAsync(file, [1, 2, 3, 4], TestContext.Current.CancellationToken);
        var directoryId = await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.DrainAsync(store);
        File.Delete(file);
        await LibraryPipeline.RescanAsync(store, directoryId);

        await using var scope = store.Scope();
        var video = await Projected(scope);

        // Unreachable is not Available, and a Video with no Available occurrence claims nothing
        // about direct play.
        Assert.Equal(VideoAvailability.Unavailable, video.Availability);
        Assert.Equal(DirectPlayClassification.Unsupported, video.BestClassification);

        // Nor about its quality: a band the library cannot reach is not one it holds.
        Assert.Equal(VideoQualityBand.Unknown, video.Quality);

        // The label survives the loss, because losing a file is not losing what it was called.
        Assert.Equal("gone", video.DisplayLabel);
    }

    [Fact]
    public async Task An_unprojected_video_is_built_before_the_application_serves_it()
    {
        await using var store = await CreateAsync();
        var source = Path.Combine(store.LibraryMountRoot.Path, "source");
        Directory.CreateDirectory(source);
        await File.WriteAllBytesAsync(
            Path.Combine(source, "backfilled.mp4"),
            [1, 2, 3, 4],
            TestContext.Current.CancellationToken);
        await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.DrainAsync(store);

        // Exactly the state an upgrade leaves behind: rows that predate the projection.
        await using (var scope = store.Scope())
        {
            await scope.ServiceProvider
                .GetRequiredService<ViewerDbContext>()
                .Videos
                .ExecuteUpdateAsync(
                    update => update
                        .SetProperty(video => video.ProjectedAt, (DateTime?)null)
                        .SetProperty(video => video.DisplayLabel, string.Empty)
                        .SetProperty(video => video.SearchText, string.Empty),
                    TestContext.Current.CancellationToken);
        }

        await store.MigrateAsync();
        await using (var scope = store.Scope())
        {
            await scope.ServiceProvider
                .GetRequiredService<DatabaseMigrator>()
                .PrepareAsync(TestContext.Current.CancellationToken);
        }

        await using var verification = store.Scope();
        var projected = await Projected(verification);
        Assert.Equal("backfilled", projected.DisplayLabel);
        Assert.NotNull(projected.ProjectedAt);
    }

    private static async Task<VideoRow> Projected(AsyncServiceScope scope) =>
        await scope.ServiceProvider
            .GetRequiredService<ViewerDbContext>()
            .Videos
            .SingleAsync(TestContext.Current.CancellationToken);

    private static Task<TestDatabase> CreateAsync(FixtureIdentificationClient? prdb = null) =>
        TestDatabase.CreateAsync(
            mediaProbe: new FixtureProbe(),
            hasher: new FixtureHasher(),
            previewGenerator: new FixturePreviewGenerator(),
            identificationClient: prdb ?? new FixtureIdentificationClient());
}
