using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.FakeCatalogue;

using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

/// <summary>
/// The library lanes, driven through the prdb clients that actually ship.
///
/// Everywhere else the lanes are given <see cref="FixtureIdentificationClient"/>, which implements
/// the interface by hand. That tests what a lane does with an answer and nothing about where the
/// answer comes from, and it lets the fixture invent answers prdb never sends. It already does:
/// the fixture returns no result at all for a file it was not told about, while prdb answers every
/// file it was asked about — at no confidence for the ones it does not hold. The lane has separate
/// code for each, and until now only the branch reality never takes was covered. The two turn out
/// to converge, which was worth finding out rather than assuming; it is pinned here now.
///
/// So these tests keep both clients and replace the socket underneath them. What runs is the
/// SDK's serialisation, the real mapping from reply to record, and the lane on top of it.
/// </summary>
public sealed class LibraryThroughPrdbTests
{
    [Fact]
    public async Task A_content_match_reaches_the_browsing_screen_as_an_established_Work()
    {
        var prdb = new FakePrdb().Recognises(
            "first.mp4",
            "A Known Work",
            "Known Site",
            actors: ["Alex Doe", "Sam Roe"]);
        await using var store = await CreateAsync(prdb);
        await ScanAsync(store, "first.mp4");

        var summary = Assert.Single(await BrowsingAsync(store));
        Assert.Equal("A Known Work", summary.DisplayTitle);
        Assert.Equal(IdentificationResolution.Established, summary.Identification.Work.Resolution);
        Assert.Equal(IdentificationReviewStatus.Clear, summary.Identification.Work.ReviewStatus);
        Assert.Equal("Known Site", summary.Identification.Site.TargetTitle);
        Assert.Equal(["Alex Doe", "Sam Roe"], summary.Identification.Actors);
    }

    /// <summary>
    /// A match by file name is not evidence enough to file a Work without a person agreeing to it,
    /// however sure the catalogue sounds. prdb sends the Site and cast alongside such a match; the
    /// hand-written fixture sends neither, so what a lane does with a confident name match that
    /// arrives fully populated was never exercised.
    /// </summary>
    [Fact]
    public async Task A_name_match_waits_for_review_rather_than_establishing_a_Work()
    {
        var prdb = new FakePrdb().Recognises(
            "first.mp4",
            "A Guessed Work",
            "Known Site",
            matchedBy: 2,
            actors: ["Alex Doe"]);
        await using var store = await CreateAsync(prdb);
        await ScanAsync(store, "first.mp4");

        var summary = Assert.Single(await BrowsingAsync(store));
        Assert.Equal(IdentificationResolution.Unknown, summary.Identification.Work.Resolution);
        Assert.Equal(
            IdentificationReviewStatus.ReviewNeeded,
            summary.Identification.Work.ReviewStatus);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var candidate = Assert.Single(await database.IdentificationCandidates
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal("A Guessed Work", candidate.TargetTitle);
        Assert.Empty(await database.IdentificationClaims
            .Where(claim => claim.Dimension == IdentificationDimension.WorkIdentification &&
                            claim.Status == IdentificationClaimStatus.Current)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// prdb answers an unheld file at no confidence rather than leaving it out, which takes the
    /// lane down a different branch from the one the fixture reaches. Both branches have to settle
    /// the file; a branch that did not would offer it again on every scan, forever. Making the
    /// fake answer the fixture's way leaves this test green, so the two agree — which is the
    /// answer to a question that had never been asked, not a defect this found.
    /// </summary>
    [Fact]
    public async Task A_file_the_catalogue_does_not_hold_is_settled_rather_than_offered_again()
    {
        var prdb = new FakePrdb();
        await using var store = await CreateAsync(prdb);
        var directoryId = await ScanAsync(store, "unknown.mp4");

        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            var file = await database.VideoFiles.SingleAsync(TestContext.Current.CancellationToken);
            Assert.NotNull(file.IdentifiedAt);
            Assert.Equal(file.Sha256, file.IdentifiedSha256);
        }

        var asked = prdb.IdentifyRequests.Count;
        await LibraryPipeline.RescanAsync(store, directoryId);

        // A rescan asks again about Videos that are still Unknown, because the catalogue may have
        // learned about them. Once, though — not once per pass of the lanes.
        Assert.Equal(asked + 1, prdb.IdentifyRequests.Count);
        var summary = Assert.Single(await BrowsingAsync(store));
        Assert.Equal(IdentificationResolution.Unknown, summary.Identification.Work.Resolution);
        Assert.Equal(IdentificationReviewStatus.Clear, summary.Identification.Work.ReviewStatus);
    }

    /// <summary>
    /// Which status code means the credential and which means the service is decided in the
    /// client, and what the installation is told about it is decided in the lane. Between the two
    /// nothing had ever run.
    /// </summary>
    [Fact]
    public async Task A_refused_credential_marks_the_installation_and_keeps_what_is_known()
    {
        var prdb = new FakePrdb().Recognises("first.mp4", "A Known Work", "Known Site");
        await using var store = await CreateAsync(prdb);
        var directoryId = await ScanAsync(store, "first.mp4");

        prdb.Failure = System.Net.HttpStatusCode.Unauthorized;
        await AddFileAsync(store, "second.mp4");
        await LibraryPipeline.RescanAsync(store, directoryId);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var configuration = await database.InstallationConfigurations
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(PrdbConnectionStatus.Rejected, configuration.PrdbConnectionStatus);
        Assert.Equal(PrdbConnectionIssue.ExternalAuthority, configuration.LastConnectionIssue);
        Assert.Contains(
            await database.WorkIssues.ToListAsync(TestContext.Current.CancellationToken),
            issue => issue.Cause == WorkIssueCause.ExternalAuthority &&
                     issue.Severity == WorkIssueSeverity.OperationalBlocker &&
                     issue.RetryDisposition == WorkIssueRetryDisposition.NoAutomaticRetry &&
                     issue.ResolvedAt is null);

        // The refusal is about new requests. What the installation already established stays.
        Assert.Single(await database.IdentificationClaims
            .Where(claim => claim.Dimension == IdentificationDimension.WorkIdentification &&
                            claim.Status == IdentificationClaimStatus.Current)
            .ToListAsync(TestContext.Current.CancellationToken));
        await StillEstablishedAsync(store, scope);
    }

    /// <summary>
    /// An outage is nobody's to correct, so it has to read as one: a scoped issue with a retry
    /// already scheduled, and no doubt cast on the credential.
    /// </summary>
    [Theory]
    [InlineData(System.Net.HttpStatusCode.ServiceUnavailable)]
    [InlineData(System.Net.HttpStatusCode.TooManyRequests)]
    public async Task An_outage_schedules_a_retry_without_blaming_the_credential(
        System.Net.HttpStatusCode status)
    {
        var prdb = new FakePrdb().Recognises("first.mp4", "A Known Work", "Known Site");
        await using var store = await CreateAsync(prdb);
        var directoryId = await ScanAsync(store, "first.mp4");

        prdb.Failure = status;
        await AddFileAsync(store, "second.mp4");
        await LibraryPipeline.RescanAsync(store, directoryId);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var configuration = await database.InstallationConfigurations
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(PrdbConnectionStatus.Degraded, configuration.PrdbConnectionStatus);
        Assert.Equal(PrdbConnectionIssue.ExternalAvailability, configuration.LastConnectionIssue);
        var issue = Assert.Single(
            await database.WorkIssues.ToListAsync(TestContext.Current.CancellationToken),
            issue => issue.Cause == WorkIssueCause.ExternalAvailability);
        Assert.Equal(WorkIssueSeverity.ScopedIssue, issue.Severity);
        Assert.Equal(WorkIssueRetryDisposition.AutomaticRetryScheduled, issue.RetryDisposition);
        Assert.NotNull(issue.NextAttemptAt);
        await StillEstablishedAsync(store, scope);
    }

    /// <summary>
    /// That the first scan really established a Work, and that the failure which followed left it
    /// alone. Counting the Videos on the screen does not say this: both files are on it either
    /// way, so the count stayed right through a run where nothing was ever identified.
    /// </summary>
    private static async Task StillEstablishedAsync(TestDatabase store, AsyncServiceScope scope)
    {
        var browsing = await BrowsingAsync(store, scope);
        Assert.Equal(2, browsing.Count);
        var known = Assert.Single(
            browsing,
            summary => summary.Identification.Work.Resolution == IdentificationResolution.Established);
        Assert.Equal("A Known Work", known.DisplayTitle);
        Assert.Equal("Known Site", known.Identification.Site.TargetTitle);
    }

    /// <summary>
    /// The one lane that keeps working while prdb is unreachable, reading paths against the
    /// vocabulary the Site Directory client fetched. Both halves ship; neither had been run
    /// against the other.
    /// </summary>
    [Fact]
    public async Task A_Site_the_directory_published_is_recognised_from_the_path()
    {
        var prdb = new FakePrdb();
        prdb.PublishedSites.Add("Known Site");
        await using var store = await CreateAsync(prdb);
        await ScanAsync(store, Path.Combine("Known Site", "unknown.mp4"));

        var summary = Assert.Single(await BrowsingAsync(store));
        // The catalogue holds nothing under this name, so the Work stays Unknown. The path still
        // says which Site it came from.
        Assert.Equal(IdentificationResolution.Unknown, summary.Identification.Work.Resolution);
        Assert.Equal(IdentificationResolution.Established, summary.Identification.Site.Resolution);
        Assert.Equal("Known Site", summary.Identification.Site.TargetTitle);
    }

    private static Task<TestDatabase> CreateAsync(FakePrdb prdb) =>
        TestDatabase.CreateAsync(
            mediaProbe: new FixtureProbe(),
            hasher: new FixtureHasher(),
            previewGenerator: new FixturePreviewGenerator(),
            prdb: prdb);

    /// <summary>Writes the named files, activates the library, and drains every lane.</summary>
    private static async Task<Guid> ScanAsync(TestDatabase store, params string[] names)
    {
        foreach (var name in names)
        {
            await AddFileAsync(store, name);
        }

        var directoryId = await LibraryPipeline.ActivateAsync(store, SourceOf(store));
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");
        await LibraryPipeline.DrainAsync(store);
        return directoryId;
    }

    private static async Task AddFileAsync(TestDatabase store, string name)
    {
        var path = Path.Combine(SourceOf(store), name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(
            path,
            System.Text.Encoding.UTF8.GetBytes(name),
            TestContext.Current.CancellationToken);
    }

    private static string SourceOf(TestDatabase store)
    {
        var source = Path.Combine(store.LibraryMountRoot.Path, "source");
        Directory.CreateDirectory(source);
        return source;
    }

    private static async Task<IReadOnlyList<VideoSummary>> BrowsingAsync(TestDatabase store)
    {
        await using var scope = store.Scope();
        return await BrowsingAsync(store, scope);
    }

    private static Task<IReadOnlyList<VideoSummary>> BrowsingAsync(
        TestDatabase store,
        AsyncServiceScope scope) =>
        scope.ServiceProvider
            .GetRequiredService<VideoCatalog>()
            .GetAsync(
                Guid.CreateVersion7(),
                LibraryPipeline.ClientContext,
                TestContext.Current.CancellationToken);
}
