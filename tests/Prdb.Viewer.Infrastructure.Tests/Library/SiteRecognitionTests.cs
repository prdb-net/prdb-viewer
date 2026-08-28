using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

public sealed class SiteRecognitionTests
{
    private const string WorkId = "6f1a2c34-0000-4000-8000-000000000101";
    private const string KnownSiteId = "5b1a2c34-0000-4000-8000-0000000001aa";
    private const string OtherSiteId = "5b1a2c34-0000-4000-8000-0000000001bb";

    private static readonly RemoteSite KnownSite =
        new(KnownSiteId, "Known Site", "https://knownsite.test");

    private static readonly RemoteSite OtherSite =
        new(OtherSiteId, "Other Site", "https://othersite.test");

    [Fact]
    public async Task A_path_naming_one_known_site_establishes_it_locally_with_its_own_provenance()
    {
        var prdb = new FixtureIdentificationClient().Unmatched("known site - scene.mp4");
        var sites = new FixtureSiteDirectoryClient(KnownSite, OtherSite);
        await using var store = await CreateAsync(prdb, sites);
        var source = await SourceAsync(store, "known site - scene.mp4");
        await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");

        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var claim = Assert.Single(await database.IdentificationClaims
            .Where(row => row.Status == IdentificationClaimStatus.Current)
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(IdentificationDimension.SiteRecognition, claim.Dimension);
        Assert.Equal(KnownSiteId, claim.TargetKey);
        Assert.Equal(IdentificationSource.LocalInference, claim.Source);
        Assert.Equal(IdentificationEvidenceClass.Conclusive, claim.EvidenceClass);
        Assert.False(claim.IsAdministrativeOverride);
        Assert.Empty(await database.IdentificationCandidates
            .ToListAsync(TestContext.Current.CancellationToken));

        // The Video stays Unknown: a recognised site says nothing about the work.
        var summary = (await scope.ServiceProvider
                .GetRequiredService<VideoCatalog>()
                .GetAsync(Guid.CreateVersion7(), TestContext.Current.CancellationToken))
            .Single();
        Assert.Equal(IdentificationResolution.Unknown, summary.Identification.Work.Resolution);
        Assert.Equal(IdentificationResolution.Established, summary.Identification.Site.Resolution);
        Assert.Equal("Known Site", summary.Identification.Site.TargetTitle);
        Assert.Equal(IdentificationSource.LocalInference, summary.Identification.Site.Source);
        Assert.Equal(IdentificationReviewStatus.Clear, summary.Identification.Site.ReviewStatus);

        var video = await database.Videos.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Known Site", video.EstablishedSite);
    }

    [Fact]
    public async Task A_path_naming_two_known_sites_proposes_both_and_establishes_neither()
    {
        var prdb = new FixtureIdentificationClient().Unmatched("known site meets other site.mp4");
        var sites = new FixtureSiteDirectoryClient(KnownSite, OtherSite);
        await using var store = await CreateAsync(prdb, sites);
        var source = await SourceAsync(store, "known site meets other site.mp4");
        await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");

        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        Assert.Empty(await database.IdentificationClaims
            .ToListAsync(TestContext.Current.CancellationToken));
        var candidates = await database.IdentificationCandidates
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, candidate =>
        {
            Assert.Equal(IdentificationDimension.SiteRecognition, candidate.Dimension);
            Assert.Equal(IdentificationCandidateStatus.Pending, candidate.Status);
            Assert.Equal(IdentificationEvidenceClass.Suggestive, candidate.EvidenceClass);
            Assert.Equal(IdentificationSource.LocalInference, candidate.Source);
        });

        var summary = (await scope.ServiceProvider
                .GetRequiredService<VideoCatalog>()
                .GetAsync(Guid.CreateVersion7(), TestContext.Current.CancellationToken))
            .Single();
        Assert.Equal(IdentificationResolution.Unknown, summary.Identification.Site.Resolution);
        Assert.Equal(
            IdentificationReviewStatus.ReviewNeeded,
            summary.Identification.Site.ReviewStatus);

        // A proposal is never searchable: searching for a guess would present it as a fact.
        var video = await database.Videos.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Null(video.EstablishedSite);
    }

    [Fact]
    public async Task A_site_prdb_established_is_confirmed_rather_than_proposed_again()
    {
        var prdb = new FixtureIdentificationClient()
            .Conclusive("known site - scene.mp4", WorkId, "A Known Work", KnownSite);
        var sites = new FixtureSiteDirectoryClient(KnownSite);
        await using var store = await CreateAsync(prdb, sites);
        var source = await SourceAsync(store, "known site - scene.mp4");
        await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");

        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var site = Assert.Single(await database.IdentificationClaims
            .Where(claim => claim.Dimension == IdentificationDimension.SiteRecognition)
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(IdentificationSource.PrdbIdentification, site.Source);
        Assert.Equal(IdentificationClaimStatus.Current, site.Status);
        Assert.Empty(await database.IdentificationCandidates
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_locally_recognised_site_gives_way_to_the_site_of_an_identified_work()
    {
        var prdb = new FixtureIdentificationClient().Unmatched("known site - scene.mp4");
        var sites = new FixtureSiteDirectoryClient(KnownSite, OtherSite);
        await using var store = await CreateAsync(prdb, sites);
        var source = await SourceAsync(store, "known site - scene.mp4");
        await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");
        await LibraryPipeline.DrainAsync(store);

        // prdb learns about the file and reports a different site as the work's canonical one.
        prdb.Conclusive("known site - scene.mp4", WorkId, "A Known Work", OtherSite);
        await LibraryPipeline.ReofferAsync(store, "known site - scene.mp4");

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var claims = await database.IdentificationClaims
            .Where(claim => claim.Dimension == IdentificationDimension.SiteRecognition)
            .ToListAsync(TestContext.Current.CancellationToken);
        var current = Assert.Single(
            claims,
            claim => claim.Status == IdentificationClaimStatus.Current);
        Assert.Equal(OtherSiteId, current.TargetKey);
        Assert.Equal(IdentificationSource.PrdbIdentification, current.Source);

        // The local reading is kept as history rather than overwritten.
        var superseded = Assert.Single(
            claims,
            claim => claim.Status == IdentificationClaimStatus.Superseded);
        Assert.Equal(KnownSiteId, superseded.TargetKey);
        Assert.Equal(IdentificationSource.LocalInference, superseded.Source);
        Assert.NotNull(superseded.EndedAt);
    }

    [Fact]
    public async Task A_site_the_installation_already_established_is_recognised_without_a_directory()
    {
        var prdb = new FixtureIdentificationClient()
            .Conclusive("known site - first.mp4", WorkId, "A Known Work", KnownSite)
            .Unmatched("known site - second.mp4");
        var sites = new FixtureSiteDirectoryClient { Status = SiteDirectoryFetchStatus.Unavailable };
        await using var store = await CreateAsync(prdb, sites);
        var source = await SourceAsync(store, "known site - first.mp4", "known site - second.mp4");
        await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");

        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var second = await database.VideoFiles
            .SingleAsync(
                file => file.RelativePath == "known site - second.mp4",
                TestContext.Current.CancellationToken);
        var claim = Assert.Single(await database.IdentificationClaims
            .Where(row => row.VideoId == second.VideoId &&
                          row.Dimension == IdentificationDimension.SiteRecognition)
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(KnownSiteId, claim.TargetKey);
        Assert.Equal(IdentificationSource.LocalInference, claim.Source);
    }

    [Fact]
    public async Task An_unfetchable_site_directory_is_reported_without_stopping_the_library()
    {
        var prdb = new FixtureIdentificationClient().Unmatched("known site - scene.mp4");
        var sites = new FixtureSiteDirectoryClient { Status = SiteDirectoryFetchStatus.Unavailable };
        await using var store = await CreateAsync(prdb, sites);
        var source = await SourceAsync(store, "known site - scene.mp4");
        await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");

        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var issue = Assert.Single(await database.WorkIssues
            .Where(row => row.Category == BackgroundWorkCategory.SiteRecognition)
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(WorkIssueSeverity.ScopedIssue, issue.Severity);
        Assert.Equal(WorkIssueCause.ExternalAvailability, issue.Cause);
        Assert.Null(issue.ResolvedAt);

        // Everything the library does without prdb still happened.
        var file = await database.VideoFiles.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(VideoFilePreviewState.Generated, file.PreviewState);
        Assert.Equal(DirectPlayClassification.BaselineCandidate, file.DirectPlayClassification);
    }

    [Fact]
    public async Task A_recognised_path_is_not_read_again_while_it_is_unchanged()
    {
        var prdb = new FixtureIdentificationClient().Unmatched("known site - scene.mp4");
        var sites = new FixtureSiteDirectoryClient(KnownSite);
        await using var store = await CreateAsync(prdb, sites);
        var source = await SourceAsync(store, "known site - scene.mp4");
        var directoryId = await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");
        await LibraryPipeline.DrainAsync(store);

        await LibraryPipeline.RescanAsync(store, directoryId);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var claim = Assert.Single(await database.IdentificationClaims
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(IdentificationClaimStatus.Current, claim.Status);
        Assert.Equal(1, sites.Calls);
    }

    private static Task<TestDatabase> CreateAsync(
        FixtureIdentificationClient prdb,
        FixtureSiteDirectoryClient sites) =>
        TestDatabase.CreateAsync(
            prdbConnectionVerifier: new FixtureConnectionVerifier(),
            mediaProbe: new FixtureProbe(),
            hasher: new FixtureHasher(),
            previewGenerator: new FixturePreviewGenerator(),
            identificationClient: prdb,
            siteDirectoryClient: sites);

    private static async Task<string> SourceAsync(TestDatabase store, params string[] names)
    {
        var source = Path.Combine(store.LibraryMountRoot.Path, "source");
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
