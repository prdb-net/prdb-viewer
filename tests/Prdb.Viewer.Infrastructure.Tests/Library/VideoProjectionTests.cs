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
    }

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
