using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.FakeCatalogue;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

public sealed class CurrentCatalogueFactsTests
{
    private const string WorkId = "6f1a2c34-0000-4000-8000-000000000001";
    private const string SiteId = "6f1a2c34-0000-4000-8000-000000000002";
    private const string SceneId = "6f1a2c34-0000-4000-8000-000000000003";
    private const string Description = "<script>literal text</script>";
    private static readonly string VideoJson = $$"""
        {"id":"{{WorkId}}","title":"Work","description":"{{Description}}","stashdbSceneId":"{{SceneId}}",
         "site":{"id":"{{SiteId}}","title":"Creator","url":"https://platform.test/alex",
                 "platformId":"{{SiteId}}","platformKey":"platform","platformTitle":"Platform","accountHandle":"alex"},"actors":[],"images":[]}
        """;

    [Fact]
    public async Task A_legacy_platform_host_claim_is_retained_and_reviewed_once()
    {
        await using var store = await TestDatabase.CreateAsync(mediaProbe: new FixtureProbe(), hasher: new FixtureHasher(),
            previewGenerator: new FixturePreviewGenerator(), identificationClient: new FixtureIdentificationClient());
        var path = Path.Combine(store.LibraryMountRoot.Path, "source");
        Directory.CreateDirectory(Path.Combine(path, "platform"));
        await File.WriteAllBytesAsync(Path.Combine(path, "platform", "video.mp4"), [1, 2, 3], TestContext.Current.CancellationToken);
        var directory = await LibraryPipeline.ActivateAsync(store, path);
        await LibraryPipeline.SetCredentialAsync(store, "fixture");
        await LibraryPipeline.DrainAsync(store);
        await using (var scope = store.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            var file = await db.VideoFiles.SingleAsync(TestContext.Current.CancellationToken);
            await scope.ServiceProvider.GetRequiredService<IdentificationService>().ApplyLocalSiteRecognitionAsync(file.Id,
                SiteVocabulary.From([new(SiteId, "Alice", "https://platform.example/alice")]).Recognise(file.RelativePath),
                TestContext.Current.CancellationToken);
            await db.IdentificationClaims.ExecuteUpdateAsync(update => update.SetProperty(claim => claim.SiteRecognitionPolicyVersion, 0),
                TestContext.Current.CancellationToken);
            await db.InstallationConfigurations.ExecuteUpdateAsync(update => update.SetProperty(config => config.SiteDirectoryFetchedAt, DateTime.UtcNow),
                TestContext.Current.CancellationToken);
            db.SiteDirectoryEntries.Add(new SiteDirectoryEntryRow
            {
                SiteKey = SiteId,
                Title = "Alice",
                Url = "https://platform.example/alice",
                PlatformId = SiteId,
                PlatformKey = "platform",
                PlatformTitle = "platform",
                AccountHandle = "alice"
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        await LibraryPipeline.RescanAsync(store, directory);
        await LibraryPipeline.RescanAsync(store, directory);
        await using var final = store.Scope();
        var database = final.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var claim = await database.IdentificationClaims.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(IdentificationClaimStatus.Current, claim.Status);
        Assert.Equal(IdentificationSource.LocalInference, claim.Source);
        Assert.Equal(1, claim.SiteRecognitionPolicyVersion);
        Assert.Single(await database.IdentificationCandidates.ToListAsync(TestContext.Current.CancellationToken));
        Assert.True((await database.Videos.SingleAsync(TestContext.Current.CancellationToken)).ReviewNeeded);
    }

    [Fact]
    public async Task Identification_and_batch_detail_read_the_same_current_facts_through_the_real_SDK()
    {
        var fileId = Guid.NewGuid();
        var prdb = new FakePrdb { RawBody = $$"""{"results":[{"ref":"{{fileId:n}}","matchedBy":5,"confidence":4,"videoId":"{{WorkId}}","candidates":[],"video":{{VideoJson}}}]}""" };
        var identified = await new PrdbIdentificationClient(new FakePrdbTransport(prdb))
            .IdentifyAsync("fixture", [new(fileId, "file.mp4", 123, null, null)], TestContext.Current.CancellationToken);
        var work = Assert.Single(identified.Results).Work;
        Assert.Equal(Description, work?.Description);
        Assert.Equal(Guid.Parse(SceneId), work?.StashdbSceneId);
        Assert.Equal("alex", work?.Site?.AccountHandle);
        Assert.Equal("Platform", work?.Site?.PlatformTitle);
        Assert.Null(work?.Network);
        prdb.RawBody = "[" + VideoJson + "]";
        var detail = await new PrdbWorkDetailClient(new FakePrdbTransport(prdb))
            .FetchAsync("fixture", [WorkId], TestContext.Current.CancellationToken);
        Assert.Equal(work?.Description, Assert.Single(detail.Works).Description);
        Assert.Equal(work?.Site, Assert.Single(detail.Works).Site);
    }

    [Fact]
    public async Task Published_Site_links_and_platform_facts_are_read_without_inventing_nested_links()
    {
        var prdb = new FakePrdb { RawBody = $$$"""
            {"hasMore":false,"serverTimeUtc":"2026-10-01T00:00:00Z","nextCursor":{"updatedAtUtc":"2026-09-30T00:00:00Z","id":"{{{SiteId}}}"},"items":[{"eventType":"updated","site":{"isDeleted":false,"updatedAtUtc":"2026-09-30T00:00:00Z","id":"{{{SiteId}}}","title":"Creator","platformId":"{{{SiteId}}}","platformKey":"platform",
             "platformTitle":"Platform","accountHandle":"alex","links":[{"label":"Profile","url":"https://example.test/alex"},
             {"label":"Unsafe","url":"javascript:alert(1)"},{"url":"/relative"}]}}]}
            """ };
        var result = await new PrdbSiteDirectoryClient(new FakePrdbTransport(prdb))
            .FetchAsync("fixture", TestContext.Current.CancellationToken);
        var site = Assert.Single(result.Sites);
        Assert.Equal("alex", site.AccountHandle);
        Assert.Equal("https://example.test/alex", Assert.Single(site.Links).Url);
    }

    [Fact]
    public async Task Proposed_facts_stay_separate_and_retained_facts_can_be_cleared_by_enrichment()
    {
        await using var store = await TestDatabase.CreateAsync(mediaProbe: new FixtureProbe(), hasher: new FixtureHasher(),
            previewGenerator: new FixturePreviewGenerator(), identificationClient: new FixtureIdentificationClient());
        var path = Path.Combine(store.LibraryMountRoot.Path, "source");
        Directory.CreateDirectory(path);
        await File.WriteAllBytesAsync(Path.Combine(path, "video.mp4"), [1, 2, 3], TestContext.Current.CancellationToken);
        await LibraryPipeline.ActivateAsync(store, path);
        await LibraryPipeline.SetCredentialAsync(store, "fixture");
        await LibraryPipeline.DrainAsync(store);
        await using var scope = store.Scope();
        var db = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var file = await db.VideoFiles.SingleAsync(TestContext.Current.CancellationToken);
        var service = scope.ServiceProvider.GetRequiredService<IdentificationService>();
        var work = new RemoteWork(WorkId, "Work", new RemoteSite(SiteId, "Creator", null) { PlatformTitle = "Platform", AccountHandle = "alex" }, [], null, null, null)
        { Description = Description, StashdbSceneId = Guid.Parse(SceneId) };
        await service.ApplyRemoteIdentificationAsync(new(file.Id, RemoteMatchKind.Filename, RemoteMatchConfidence.Probable,
            WorkId, [], work.Site, work), TestContext.Current.CancellationToken);
        Assert.Empty(await db.VideoMetadata.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(Description, (await db.ProposedWorks.SingleAsync(TestContext.Current.CancellationToken)).Description);
        await service.ApplyRemoteIdentificationAsync(new(file.Id, RemoteMatchKind.Md5, RemoteMatchConfidence.Exact,
            WorkId, [], work.Site, work), TestContext.Current.CancellationToken);
        var metadata = await db.VideoMetadata.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Description, metadata.Description);
        Assert.Equal("alex", RetainedWorkFacts.Site(metadata.SiteFactsJson)?.AccountHandle);
        await service.RefreshRetainedWorkAsync(file.VideoId, work with { Description = null, StashdbSceneId = null, Site = null }, TestContext.Current.CancellationToken);
        metadata = await db.VideoMetadata.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Null(metadata.Description);
        Assert.Null(metadata.StashdbSceneId);
        Assert.Null(RetainedWorkFacts.Site(metadata.SiteFactsJson));
    }
}
