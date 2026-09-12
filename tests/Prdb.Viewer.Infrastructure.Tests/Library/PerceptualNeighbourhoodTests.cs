using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

/// <summary>
/// What the installation knows about its own files resembling each other. Nothing here is
/// user-visible yet: the lane ends with the installation knowing which of its files look alike,
/// and what is done with that knowledge belongs to the tickets that read it.
/// </summary>
public sealed class PerceptualNeighbourhoodTests
{
    /// <summary>Two encodes of one work: the same picture, two bits apart, the same running time.</summary>
    private const string Original = "0f0f0f0f0f0f0f0f";

    private const string Reencode = "0f0f0f0f0f0f0f0c";

    /// <summary>A different work: nowhere near, the way an unrelated pair measured out.</summary>
    private const string Unrelated = "f0f0f0f0f0f0f0f0";

    [Fact]
    public async Task Two_files_within_the_measured_band_become_a_neighbourhood()
    {
        await using var store = await CreateAsync(new Dictionary<string, string?>
        {
            ["original.mp4"] = Original,
            ["re-encode.mp4"] = Reencode,
            ["something else.mp4"] = Unrelated,
        });

        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var neighbourhood = Assert.Single(await database.PerceptualNeighbourhoods
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, neighbourhood.Distance);
        Assert.True(neighbourhood.DurationsAgree);
        Assert.Equal(
            new[] { Original, Reencode }.Order(),
            new[] { neighbourhood.LeftPerceptualHash, neighbourhood.RightPerceptualHash }.Order());
    }

    /// <summary>
    /// The pair is one fact. It is found from whichever side the backlog reaches first, and it must
    /// not become two facts because both sides were reached.
    /// </summary>
    [Fact]
    public async Task A_pair_is_one_row_however_many_times_the_search_meets_it()
    {
        await using var store = await CreateAsync(new Dictionary<string, string?>
        {
            ["original.mp4"] = Original,
            ["re-encode.mp4"] = Reencode,
        });

        await LibraryPipeline.DrainAsync(store);
        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        Assert.Single(await database.PerceptualNeighbourhoods
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Files_the_hash_puts_far_apart_are_not_neighbours()
    {
        await using var store = await CreateAsync(new Dictionary<string, string?>
        {
            ["one.mp4"] = Original,
            ["two.mp4"] = Unrelated,
        });

        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        Assert.Empty(await database.PerceptualNeighbourhoods
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A container ffmpeg cannot sample produces no Perceptual Hash at all. That silence is an
    /// absence rather than a value, and two silent files are not neighbours of each other.
    /// </summary>
    [Fact]
    public async Task A_file_without_a_hash_is_nobodys_neighbour()
    {
        await using var store = await CreateAsync(new Dictionary<string, string?>
        {
            ["silent one.mp4"] = null,
            ["silent two.mp4"] = null,
            ["heard.mp4"] = Original,
        });

        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        Assert.Empty(await database.PerceptualNeighbourhoods
            .ToListAsync(TestContext.Current.CancellationToken));

        // And no Work Issue either: the absence is the same one the remote ladder lives with.
        Assert.Empty(await database.WorkIssues
            .Where(issue => issue.Category == BackgroundWorkCategory.PerceptualNeighbourhood)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The durations are a fact about the pair, retained beside the distance, because a distance
    /// cannot be read without them: the sample grid the hash is built on is a function of the
    /// running time.
    /// </summary>
    [Fact]
    public async Task A_neighbourhood_records_whether_the_running_times_agree()
    {
        await using var store = await CreateAsync(
            new Dictionary<string, string?>
            {
                ["original.mp4"] = Original,
                ["trimmed.mp4"] = Reencode,
            },
            durations: new Dictionary<string, long>
            {
                ["original.mp4"] = 1_800_000,
                ["trimmed.mp4"] = 1_780_000,
            });

        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var neighbourhood = Assert.Single(await database.PerceptualNeighbourhoods
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, neighbourhood.Distance);
        Assert.False(neighbourhood.DurationsAgree);
        Assert.Equal(
            [1_780_000, 1_800_000],
            new[]
            {
                neighbourhood.LeftDurationMilliseconds,
                neighbourhood.RightDurationMilliseconds,
            }.Order());
    }

    /// <summary>
    /// A file hashed again to a different value invalidates what was computed from the old one,
    /// the way an identification is guarded by the content it was established against.
    /// </summary>
    [Fact]
    public async Task Rehashing_a_file_to_different_content_drops_what_its_old_value_concluded()
    {
        var hashes = new Dictionary<string, string?>
        {
            ["original.mp4"] = Original,
            ["re-encode.mp4"] = Reencode,
        };
        await using var store = await CreateAsync(hashes);
        await LibraryPipeline.DrainAsync(store);
        Assert.Single(await NeighbourhoodsAsync(store));

        hashes["re-encode.mp4"] = Unrelated;
        await RehashAsync(store, "re-encode.mp4");

        Assert.Empty(await NeighbourhoodsAsync(store));
        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var file = await database.VideoFiles.SingleAsync(
            row => row.RelativePath == "re-encode.mp4",
            TestContext.Current.CancellationToken);
        Assert.Equal(Unrelated, file.PerceptualHash);
        Assert.Equal(Unrelated, file.NeighbourhoodComparedHash);
    }

    /// <summary>
    /// A file is compared once for the value it carries. That is what makes the search a backlog
    /// rather than a sweep: a second run over an unchanged library does no work at all.
    /// </summary>
    [Fact]
    public async Task A_settled_library_is_not_compared_again()
    {
        await using var store = await CreateAsync(new Dictionary<string, string?>
        {
            ["original.mp4"] = Original,
            ["re-encode.mp4"] = Reencode,
        });
        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        Assert.False(await scope.ServiceProvider
            .GetRequiredService<PerceptualNeighbourhoodRunner>()
            .RunNextSliceAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The re-encode sitting in another Library Directory is the case the whole theme exists for,
    /// so the comparison is installation-wide even though the lane is per Library Directory.
    /// </summary>
    [Fact]
    public async Task Files_of_two_library_directories_are_compared_with_each_other()
    {
        var hashes = new Dictionary<string, string?>
        {
            ["original.mp4"] = Original,
            ["re-encode.mp4"] = Reencode,
        };
        await using var store = await TestDatabase.CreateAsync(
            mediaProbe: new FixtureProbe(),
            hasher: new FixtureHasher(path => Hashes(path, hashes)),
            previewGenerator: new FixturePreviewGenerator(),
            identificationClient: new FixtureIdentificationClient());
        await LibraryPipeline.ActivateAsync(
            store,
            await SourceAsync(store, "first", "original.mp4"));
        await LibraryPipeline.ActivateAsync(
            store,
            await SourceAsync(store, "second", "re-encode.mp4"));

        await LibraryPipeline.DrainAsync(store);

        var neighbourhood = Assert.Single(await NeighbourhoodsAsync(store));
        Assert.Equal(2, neighbourhood.Distance);
    }

    private static async Task<IReadOnlyList<PerceptualNeighbourhoodRow>> NeighbourhoodsAsync(
        TestDatabase store)
    {
        await using var scope = store.Scope();
        return await scope.ServiceProvider
            .GetRequiredService<ViewerDbContext>()
            .PerceptualNeighbourhoods
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Offers one Video File to the Hashing lane again, the way changed content does.</summary>
    private static async Task RehashAsync(TestDatabase store, string relativePath)
    {
        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            await database.VideoFiles
                .Where(file => file.RelativePath == relativePath)
                .ExecuteUpdateAsync(
                    update => update.SetProperty(file => file.HashedSha256, (string?)null),
                    TestContext.Current.CancellationToken);
            await database.BackgroundWork
                .Where(work => work.Category == BackgroundWorkCategory.Hashing)
                .ExecuteUpdateAsync(
                    update => update
                        .SetProperty(work => work.State, BackgroundWorkState.Queued)
                        .SetProperty(work => work.FinishedAt, (DateTime?)null)
                        .SetProperty(work => work.NextAttemptAt, (DateTime?)null),
                    TestContext.Current.CancellationToken);
        }

        await LibraryPipeline.DrainAsync(store);
    }

    private static async Task<TestDatabase> CreateAsync(
        IReadOnlyDictionary<string, string?> perceptualHashes,
        IReadOnlyDictionary<string, long>? durations = null)
    {
        var store = await TestDatabase.CreateAsync(
            mediaProbe: new FixtureProbe(duration: path => Duration(path, durations)),
            hasher: new FixtureHasher(path => Hashes(path, perceptualHashes)),
            previewGenerator: new FixturePreviewGenerator(),
            identificationClient: new FixtureIdentificationClient());
        await LibraryPipeline.ActivateAsync(
            store,
            await SourceAsync(store, "source", [.. perceptualHashes.Keys]));
        return store;
    }

    private static long Duration(string path, IReadOnlyDictionary<string, long>? durations) =>
        durations is not null && durations.TryGetValue(Path.GetFileName(path), out var configured)
            ? configured
            : 12_345;

    private static VideoFileHashes Hashes(
        string path,
        IReadOnlyDictionary<string, string?> perceptualHashes)
    {
        var name = Path.GetFileName(path);
        var perceptual = perceptualHashes.TryGetValue(name, out var configured)
            ? configured
            : FixtureHasher.PerceptualHashOf(path);

        return new VideoFileHashes(
            FixtureHasher.OsHashOf(path),
            perceptual,
            perceptual is null ? "This container could not be sampled." : null);
    }

    private static async Task<string> SourceAsync(
        TestDatabase store,
        string folder,
        params string[] names)
    {
        var source = Path.Combine(store.LibraryMountRoot.Path, folder);
        Directory.CreateDirectory(source);

        for (var index = 0; index < names.Length; index++)
        {
            await File.WriteAllBytesAsync(
                Path.Combine(source, names[index]),
                [1, 2, 3, (byte)index],
                TestContext.Current.CancellationToken);
        }

        return source;
    }
}
