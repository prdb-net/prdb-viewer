using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

public sealed class Md5HashingTests
{
    private const string Hash = "900150983cd24fb0d6963f7d28e17f72";

    [Fact]
    public async Task A_full_file_read_matches_the_standard_byte_vector_and_honours_cancellation()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "abc", TestContext.Current.CancellationToken);
            IVideoFileHasher hasher = new PrdbVideoFileHasher();
            Assert.Equal(Hash, await hasher.ComputeMd5Async(path, TestContext.Current.CancellationToken));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                hasher.ComputeMd5Async(path, new CancellationToken(true)));
            Assert.Null(await hasher.ComputeMd5Async(path + ".absent", TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Legacy_Unknown_files_are_backfilled_once_without_rehashing_established_works()
    {
        var hasher = new CountingHasher();
        var prdb = new FixtureIdentificationClient().Conclusive(
            "known.mp4", "6f1a2c34-0000-4000-8000-000000000001", "Known Work");
        await using var store = await TestDatabase.CreateAsync(
            mediaProbe: new FixtureProbe(), hasher: hasher,
            previewGenerator: new FixturePreviewGenerator(), identificationClient: prdb);
        var source = Path.Combine(store.Directory, "libraries", "md5");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "known.mp4"), "abc", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(source, "unknown.mp4"), "abc", TestContext.Current.CancellationToken);
        var directory = await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.SetCredentialAsync(store, "fixture-key");
        await LibraryPipeline.DrainAsync(store);
        await using (var scope = store.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            await db.VideoFiles.ExecuteUpdateAsync(u => u
                .SetProperty(f => f.Md5, (string?)null)
                .SetProperty(f => f.Md5AttemptedSha256, (string?)null), TestContext.Current.CancellationToken);
        }
        await LibraryPipeline.RescanAsync(store, directory);
        await LibraryPipeline.RescanAsync(store, directory);
        Assert.Equal(2, hasher.FullReads);
        Assert.Equal(1, hasher.Backfills);
        await using var finalScope = store.Scope();
        var database = finalScope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var files = await database.VideoFiles.ToListAsync(TestContext.Current.CancellationToken);
        Assert.Null(files.Single(f => f.RelativePath == "known.mp4").Md5);
        Assert.Equal(Hash, files.Single(f => f.RelativePath == "unknown.mp4").Md5);
    }

    [Fact]
    public async Task A_source_changed_during_hashing_never_commits_its_MD5()
    {
        var hasher = new CountingHasher { ChangeSource = true };
        await using var store = await TestDatabase.CreateAsync(
            mediaProbe: new FixtureProbe(), hasher: hasher,
            previewGenerator: new FixturePreviewGenerator(), identificationClient: new FixtureIdentificationClient());
        var source = Path.Combine(store.Directory, "libraries", "md5");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "changing.mp4"), "abc", TestContext.Current.CancellationToken);
        await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.SetCredentialAsync(store, "fixture-key");
        await LibraryPipeline.DrainAsync(store);
        await using var scope = store.Scope();
        var file = await scope.ServiceProvider.GetRequiredService<ViewerDbContext>()
            .VideoFiles.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Null(file.Md5);
        Assert.NotNull(file.Md5FailureReason);
    }

    private sealed class CountingHasher : IVideoFileHasher
    {
        public int FullReads { get; private set; }
        public int Backfills { get; private set; }
        public bool ChangeSource { get; init; }

        public async Task<VideoFileHashes> ComputeAsync(string path, CancellationToken cancellationToken = default)
        {
            FullReads++;
            if (ChangeSource)
            {
                await File.AppendAllTextAsync(path, "changed", cancellationToken);
            }
            return new VideoFileHashes("0123456789abcdef", null, null, Hash);
        }

        public Task<string?> ComputeMd5Async(string path, CancellationToken cancellationToken = default)
        {
            Backfills++;
            return Task.FromResult<string?>(Hash);
        }
    }
}
