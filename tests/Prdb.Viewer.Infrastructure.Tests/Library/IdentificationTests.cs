using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Configuration;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

public sealed class IdentificationTests
{
    private const string WorkId = "6f1a2c34-0000-4000-8000-000000000001";
    private const string OtherWorkId = "6f1a2c34-0000-4000-8000-000000000002";

    [Fact]
    public async Task A_conclusive_prdb_match_establishes_the_work_and_site_with_provenance()
    {
        var previews = new FixturePreviewGenerator();
        var prdb = new FixtureIdentificationClient().Conclusive(
            "first.mp4",
            WorkId,
            "A Known Work",
            new RemoteSite("5b1a2c34-0000-4000-8000-0000000000aa", "Known Site", "https://example.test"));
        await using var store = await CreateAsync(prdb, previews);
        var source = await SourceAsync(store, ("first.mp4", [1, 2, 3, 4]));
        await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");

        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var claims = await database.IdentificationClaims
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, claims.Count);
        Assert.All(claims, claim =>
        {
            Assert.Equal(IdentificationClaimStatus.Current, claim.Status);
            Assert.Equal(IdentificationSource.PrdbIdentification, claim.Source);
            Assert.Equal(IdentificationEvidenceClass.Conclusive, claim.EvidenceClass);
            Assert.False(claim.IsAdministrativeOverride);
        });
        Assert.Equal(
            WorkId,
            Assert.Single(
                claims,
                claim => claim.Dimension == IdentificationDimension.WorkIdentification).TargetKey);
        Assert.Equal(
            "Known Site",
            Assert.Single(
                claims,
                claim => claim.Dimension == IdentificationDimension.SiteRecognition).TargetTitle);
        Assert.Empty(await database.IdentificationCandidates
            .ToListAsync(TestContext.Current.CancellationToken));

        var metadata = await database.VideoMetadata
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("A Known Work", metadata.Title);
        Assert.Equal("Known Site", metadata.SiteTitle);

        var file = await database.VideoFiles.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(VideoFileHashState.Computed, file.HashState);
        Assert.Equal(file.Sha256, file.HashedSha256);
        Assert.Equal(VideoFilePreviewState.Generated, file.PreviewState);
        Assert.NotNull(file.PublicPreviewId);
        Assert.Equal(1, previews.Generated);

        var summary = (await scope.ServiceProvider
            .GetRequiredService<VideoCatalog>()
            .GetAsync(Guid.CreateVersion7(), LibraryPipeline.ClientContext, TestContext.Current.CancellationToken))
            .Single();
        Assert.Equal("A Known Work", summary.DisplayTitle);
        Assert.Equal($"/media/previews/{file.PublicPreviewId}", summary.PreviewUrl);
        Assert.Equal(
            IdentificationResolution.Established,
            summary.Identification.Work.Resolution);
        Assert.Equal(
            IdentificationReviewStatus.Clear,
            summary.Identification.Work.ReviewStatus);
        Assert.Equal(["Alex Doe"], summary.Identification.Actors.Select(actor => actor.Name));
        Assert.Equal("Known Site", summary.Identification.Site.TargetTitle);
    }

    [Fact]
    public async Task Files_with_the_same_conclusive_identity_become_one_video()
    {
        var prdb = new FixtureIdentificationClient()
            .Conclusive("first.mp4", WorkId, "A Known Work")
            .Conclusive("second.mp4", WorkId, "A Known Work");
        await using var store = await CreateAsync(prdb);
        var source = await SourceAsync(store, ("first.mp4", [1, 2, 3, 4]), ("second.mp4", [5, 6, 7, 8]));
        await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");

        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var videos = await database.Videos
            .OrderBy(video => video.DiscoveryDate)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, videos.Count);
        var survivor = Assert.Single(videos, video => video.SurvivingVideoId is null);
        var alias = Assert.Single(videos, video => video.SurvivingVideoId is not null);
        Assert.Equal(survivor.Id, alias.SurvivingVideoId);
        Assert.True(survivor.DiscoveryDate <= alias.DiscoveryDate);
        Assert.Equal(
            2,
            await database.VideoFiles.CountAsync(
                file => file.VideoId == survivor.Id,
                TestContext.Current.CancellationToken));
        Assert.Single(await database.IdentificationClaims
            .Where(claim => claim.Status == IdentificationClaimStatus.Current)
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Single(await scope.ServiceProvider
            .GetRequiredService<VideoCatalog>()
            .GetAsync(Guid.CreateVersion7(), LibraryPipeline.ClientContext, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Suggestive_evidence_only_proposes_a_candidate_and_leaves_the_video_unknown()
    {
        var prdb = new FixtureIdentificationClient().Suggestive("first.mp4", WorkId, "A Guessed Work");
        await using var store = await CreateAsync(prdb);
        var source = await SourceAsync(store, ("first.mp4", [1, 2, 3, 4]));
        await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");

        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        Assert.Empty(await database.IdentificationClaims
            .ToListAsync(TestContext.Current.CancellationToken));
        var candidate = await database.IdentificationCandidates
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(IdentificationCandidateStatus.Pending, candidate.Status);
        Assert.Equal(IdentificationEvidenceClass.Suggestive, candidate.EvidenceClass);
        Assert.Equal(IdentificationReviewReason.SuggestiveEvidence, candidate.Reason);
        Assert.Empty(await database.VideoMetadata.ToListAsync(TestContext.Current.CancellationToken));

        var summary = (await scope.ServiceProvider
            .GetRequiredService<VideoCatalog>()
            .GetAsync(Guid.CreateVersion7(), LibraryPipeline.ClientContext, TestContext.Current.CancellationToken))
            .Single();
        Assert.Equal("first", summary.DisplayTitle);
        Assert.Equal(IdentificationResolution.Unknown, summary.Identification.Work.Resolution);
        Assert.Equal(
            IdentificationReviewStatus.ReviewNeeded,
            summary.Identification.Work.ReviewStatus);
        Assert.DoesNotContain("A Guessed Work", summary.DisplayTitle);
    }

    [Fact]
    public async Task An_unchanged_rescan_does_not_repeat_a_candidate()
    {
        var prdb = new FixtureIdentificationClient().Suggestive("first.mp4", WorkId, "A Guessed Work");
        await using var store = await CreateAsync(prdb);
        var source = await SourceAsync(store, ("first.mp4", [1, 2, 3, 4]));
        var directoryId = await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");
        await LibraryPipeline.DrainAsync(store);

        await LibraryPipeline.RescanAsync(store, directoryId);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        Assert.Single(await database.IdentificationCandidates
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.True(prdb.Calls >= 2);
    }

    [Fact]
    public async Task Evidence_agreeing_with_an_established_claim_confirms_it_rather_than_proposing_it()
    {
        var prdb = new FixtureIdentificationClient().Conclusive("first.mp4", WorkId, "A Known Work");
        await using var store = await CreateAsync(prdb);
        var source = await SourceAsync(store, ("first.mp4", [1, 2, 3, 4]));
        await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");
        await LibraryPipeline.DrainAsync(store);

        // The same work, offered again on weaker evidence: a name match where the content match
        // was. It says what the library already knows, so there is nothing to decide — and a
        // candidate made of it would have put the Video into a review whose only honest answer is
        // that both columns say the same thing.
        prdb.Suggestive("first.mp4", WorkId, "A Known Work");
        await LibraryPipeline.ReofferAsync(store, "first.mp4");

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        Assert.Empty(await database.IdentificationCandidates
            .ToListAsync(TestContext.Current.CancellationToken));
        var claim = Assert.Single(await database.IdentificationClaims
            .Where(row => row.Dimension == IdentificationDimension.WorkIdentification)
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(IdentificationClaimStatus.Current, claim.Status);
        Assert.Equal(WorkId, claim.TargetKey);
        Assert.Equal(IdentificationEvidenceClass.Conclusive, claim.EvidenceClass);

        // Agreement is recorded where agreement belongs: the claim was confirmed again, later
        // than it was established.
        Assert.NotNull(claim.LastConfirmedAt);
        Assert.True(claim.LastConfirmedAt > claim.EstablishedAt);

        var video = await database.Videos.SingleAsync(TestContext.Current.CancellationToken);
        Assert.False(video.ReviewNeeded);
    }

    [Fact]
    public async Task Conflicting_conclusive_evidence_keeps_the_claim_and_asks_for_review()
    {
        var prdb = new FixtureIdentificationClient()
            .Conclusive("first.mp4", WorkId, "A Known Work")
            .Conclusive("second.mp4", WorkId, "A Known Work");
        await using var store = await CreateAsync(prdb);
        var source = await SourceAsync(store, ("first.mp4", [1, 2, 3, 4]), ("second.mp4", [5, 6, 7, 8]));
        await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");
        await LibraryPipeline.DrainAsync(store);

        prdb.Conclusive("second.mp4", OtherWorkId, "A Different Work");
        await LibraryPipeline.ReofferAsync(store, "second.mp4");

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var current = await database.IdentificationClaims
            .Where(claim => claim.Status == IdentificationClaimStatus.Current &&
                            claim.Dimension == IdentificationDimension.WorkIdentification)
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(WorkId, current.TargetKey);
        var candidate = await database.IdentificationCandidates
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(OtherWorkId, candidate.TargetKey);
        Assert.Equal(IdentificationEvidenceClass.Conclusive, candidate.EvidenceClass);
        Assert.Equal(
            IdentificationReviewReason.ConflictingConclusiveEvidence,
            candidate.Reason);
    }

    [Fact]
    public async Task Identification_waits_visibly_without_a_credential_and_resumes_after_verification()
    {
        var prdb = new FixtureIdentificationClient().Conclusive("first.mp4", WorkId, "A Known Work");
        await using var store = await CreateAsync(prdb);
        var source = await SourceAsync(store, ("first.mp4", [1, 2, 3, 4]));
        await LibraryPipeline.ActivateAsync(store, source);

        await LibraryPipeline.DrainAsync(store);

        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            var work = await database.BackgroundWork.SingleAsync(
                row => row.Category == BackgroundWorkCategory.Identification,
                TestContext.Current.CancellationToken);
            Assert.Equal(BackgroundWorkState.Waiting, work.State);
            Assert.NotNull(work.WaitingReason);

            // Nothing is retried against unchanged configuration, so the lane waits without a
            // scheduled attempt until an Administrator supplies a key.
            Assert.Null(work.NextAttemptAt);
            var issue = await database.WorkIssues.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(WorkIssueCause.Configuration, issue.Cause);
            Assert.Equal(WorkIssueSeverity.OperationalBlocker, issue.Severity);
            Assert.Equal(RemediationOwner.Administrator, issue.RemediationOwner);
            Assert.Equal(WorkIssueRetryDisposition.NoAutomaticRetry, issue.RetryDisposition);
            Assert.Equal(0, prdb.Calls);
        }

        await using (var scope = store.Scope())
        {
            await scope.ServiceProvider
                .GetRequiredService<InstallationConfigurationService>()
                .VerifyCredentialAsync("installation-key", TestContext.Current.CancellationToken);
        }

        await LibraryPipeline.DrainAsync(store);

        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            Assert.Equal(1, prdb.Calls);
            Assert.Equal("installation-key", prdb.Credentials.Single());
            Assert.Single(await database.IdentificationClaims
                .Where(claim => claim.Dimension == IdentificationDimension.WorkIdentification)
                .ToListAsync(TestContext.Current.CancellationToken));
            Assert.All(
                await database.WorkIssues.ToListAsync(TestContext.Current.CancellationToken),
                issue =>
                {
                    Assert.NotNull(issue.ResolvedAt);
                    Assert.NotNull(issue.ResolutionEvidence);
                });
        }
    }

    [Fact]
    public async Task An_outage_degrades_the_connection_and_recovers_without_losing_knowledge()
    {
        var prdb = new FixtureIdentificationClient().Conclusive("first.mp4", WorkId, "A Known Work");
        prdb.Status = IdentificationBatchStatus.Unavailable;
        await using var store = await CreateAsync(prdb);
        var source = await SourceAsync(store, ("first.mp4", [1, 2, 3, 4]));
        await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");

        await LibraryPipeline.DrainAsync(store);

        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            Assert.Equal(
                PrdbConnectionStatus.Degraded,
                (await database.InstallationConfigurations.SingleAsync(
                    TestContext.Current.CancellationToken)).PrdbConnectionStatus);
            var issue = await database.WorkIssues.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(WorkIssueCause.ExternalAvailability, issue.Cause);
            Assert.Equal(RemediationOwner.AutomaticRecovery, issue.RemediationOwner);
            Assert.Equal(WorkIssueSeverity.ScopedIssue, issue.Severity);
            Assert.Single(await scope.ServiceProvider
                .GetRequiredService<VideoCatalog>()
                .GetAsync(Guid.CreateVersion7(), LibraryPipeline.ClientContext, TestContext.Current.CancellationToken));
        }

        prdb.Status = IdentificationBatchStatus.Identified;
        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            await database.BackgroundWork
                .Where(work => work.Category == BackgroundWorkCategory.Identification)
                .ExecuteUpdateAsync(
                    update => update.SetProperty(
                        work => work.NextAttemptAt,
                        DateTime.SpecifyKind(new DateTime(2026, 8, 27), DateTimeKind.Utc)),
                    TestContext.Current.CancellationToken);
        }

        await LibraryPipeline.DrainAsync(store);

        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            Assert.Equal(
                PrdbConnectionStatus.Verified,
                (await database.InstallationConfigurations.SingleAsync(
                    TestContext.Current.CancellationToken)).PrdbConnectionStatus);
            Assert.All(
                await database.WorkIssues.ToListAsync(TestContext.Current.CancellationToken),
                issue => Assert.NotNull(issue.ResolvedAt));
            Assert.Single(await database.IdentificationClaims
                .Where(claim => claim.Dimension == IdentificationDimension.WorkIdentification)
                .ToListAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task A_refused_credential_blocks_new_identifications_but_keeps_what_is_known()
    {
        var prdb = new FixtureIdentificationClient().Conclusive("first.mp4", WorkId, "A Known Work");
        await using var store = await CreateAsync(prdb);
        var source = await SourceAsync(store, ("first.mp4", [1, 2, 3, 4]));
        var directoryId = await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");
        await LibraryPipeline.DrainAsync(store);

        prdb.Status = IdentificationBatchStatus.Rejected;
        await AddFileAsync(store, source, "second.mp4", [9, 9, 9, 9]);
        await LibraryPipeline.RescanAsync(store, directoryId);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var configuration = await database.InstallationConfigurations
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(PrdbConnectionStatus.Rejected, configuration.PrdbConnectionStatus);
        Assert.Equal(PrdbConnectionIssue.ExternalAuthority, configuration.LastConnectionIssue);
        Assert.Equal("installation-key", configuration.ActivePrdbCredential);
        Assert.Contains(
            await database.WorkIssues.ToListAsync(TestContext.Current.CancellationToken),
            issue => issue.Cause == WorkIssueCause.ExternalAuthority &&
                     issue.Severity == WorkIssueSeverity.OperationalBlocker &&
                     issue.ResolvedAt is null);
        Assert.Single(await database.IdentificationClaims
            .Where(claim => claim.Dimension == IdentificationDimension.WorkIdentification &&
                            claim.Status == IdentificationClaimStatus.Current)
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            2,
            (await scope.ServiceProvider
                .GetRequiredService<VideoCatalog>()
                .GetAsync(Guid.CreateVersion7(), LibraryPipeline.ClientContext, TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task A_file_without_hashes_is_still_offered_for_identification_by_name()
    {
        var prdb = new FixtureIdentificationClient().Suggestive("first.mp4", WorkId, "A Guessed Work");
        var hasher = new FixtureHasher(_ => new VideoFileHashes(null, null, "fixture refused"));
        await using var store = await CreateAsync(prdb, hasher: hasher);
        var source = await SourceAsync(store, ("first.mp4", [1, 2, 3, 4]));
        await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");

        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var file = await database.VideoFiles.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(VideoFileHashState.Failed, file.HashState);
        Assert.NotNull(file.IdentifiedAt);
        Assert.Single(await database.IdentificationCandidates
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Contains(
            await database.WorkIssues.ToListAsync(TestContext.Current.CancellationToken),
            issue => issue.AffectedScope == "first.mp4" &&
                     issue.RemediationOwner == RemediationOwner.Administrator);
    }

    [Fact]
    public async Task A_failed_preview_is_a_scoped_issue_and_is_retried_on_the_next_scan()
    {
        var refuse = true;
        var previews = new FixturePreviewGenerator(_ => !refuse);
        await using var store = await CreateAsync(new FixtureIdentificationClient(), previews);
        var source = await SourceAsync(store, ("first.mp4", [1, 2, 3, 4]));
        var directoryId = await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");
        await LibraryPipeline.DrainAsync(store);

        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            var file = await database.VideoFiles.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(VideoFilePreviewState.Failed, file.PreviewState);
            Assert.Null(file.PublicPreviewId);
            Assert.Contains(
                await database.WorkIssues.ToListAsync(TestContext.Current.CancellationToken),
                issue => issue.Severity == WorkIssueSeverity.ScopedIssue &&
                         issue.AffectedScope == "first.mp4");
        }

        refuse = false;
        await LibraryPipeline.RescanAsync(store, directoryId);

        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            var file = await database.VideoFiles.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(VideoFilePreviewState.Generated, file.PreviewState);
            var preview = await scope.ServiceProvider
                .GetRequiredService<PreviewDeliveryService>()
                .OpenAsync(file.PublicPreviewId!.Value, TestContext.Current.CancellationToken);
            Assert.NotNull(preview);
            await using var content = preview.Content;
            Assert.Equal("image/jpeg", preview.ContentType);
            Assert.Equal(3, content.Length);
        }
    }

    [Fact]
    public async Task Source_media_is_never_written_to()
    {
        await using var store = await CreateAsync(
            new FixtureIdentificationClient().Conclusive("first.mp4", WorkId, "A Known Work"));
        var source = await SourceAsync(store, ("first.mp4", [1, 2, 3, 4]));
        await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");
        var before = new DirectoryInfo(source).GetFiles("*", SearchOption.AllDirectories)
            .Select(file => (file.FullName, file.Length, file.LastWriteTimeUtc))
            .ToArray();

        await LibraryPipeline.DrainAsync(store);

        Assert.Equal(
            before,
            new DirectoryInfo(source).GetFiles("*", SearchOption.AllDirectories)
                .Select(file => (file.FullName, file.Length, file.LastWriteTimeUtc))
                .ToArray());
    }

    private static Task<TestDatabase> CreateAsync(
        FixtureIdentificationClient prdb,
        FixturePreviewGenerator? previews = null,
        FixtureHasher? hasher = null) =>
        TestDatabase.CreateAsync(
            prdbConnectionVerifier: new FixtureConnectionVerifier(),
            mediaProbe: new FixtureProbe(),
            hasher: hasher ?? new FixtureHasher(),
            previewGenerator: previews ?? new FixturePreviewGenerator(),
            identificationClient: prdb);

    private static async Task<string> SourceAsync(
        TestDatabase store,
        params (string Name, byte[] Content)[] files)
    {
        var source = Path.Combine(store.LibraryMountRoot.Path, "source");
        Directory.CreateDirectory(source);

        foreach (var (name, content) in files)
        {
            await AddFileAsync(store, source, name, content);
        }

        return source;
    }

    private static Task AddFileAsync(
        TestDatabase store,
        string source,
        string name,
        byte[] content) =>
        File.WriteAllBytesAsync(
            Path.Combine(source, name),
            content,
            TestContext.Current.CancellationToken);
}
