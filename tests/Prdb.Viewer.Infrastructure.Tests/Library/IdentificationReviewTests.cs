using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Viewer.Core.Access;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;
using Prdb.Viewer.Infrastructure.Personal;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

public sealed class IdentificationReviewTests
{
    private const string WorkId = "6f1a2c34-0000-4000-8000-000000000001";
    private const string OtherWorkId = "6f1a2c34-0000-4000-8000-000000000002";
    private static readonly Guid Administrator = Guid.CreateVersion7();

    [Fact]
    public async Task Conclusive_conflicts_are_queued_before_suggestive_candidates()
    {
        var prdb = new FixtureIdentificationClient()
            .Conclusive("first.mp4", WorkId, "A Known Work")
            .Suggestive("second.mp4", OtherWorkId, "A Guessed Work");
        await using var store = await CreateAsync(prdb, ("first.mp4", [1, 2, 3, 4]), ("second.mp4", [5, 6, 7, 8]));
        prdb.Conclusive("first.mp4", OtherWorkId, "A Different Work");
        await LibraryPipeline.ReofferAsync(store, "first.mp4");

        await using var scope = store.Scope();
        var queue = await scope.ServiceProvider
            .GetRequiredService<IdentificationReviewService>()
            .GetQueueAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, queue.Count);
        Assert.Equal(IdentificationEvidenceClass.Conclusive, queue[0].Candidate.EvidenceClass);
        Assert.Equal(IdentificationResolution.Established, queue[0].CurrentResolution);
        Assert.Equal("A Known Work", queue[0].CurrentTargetTitle);
        Assert.Contains("conclusive results disagree", queue[0].Reason);
        Assert.Equal(IdentificationEvidenceClass.Suggestive, queue[1].Candidate.EvidenceClass);
        Assert.Equal(IdentificationResolution.Unknown, queue[1].CurrentResolution);
        Assert.NotNull(queue[1].PreviewUrl);
    }

    [Fact]
    public async Task Accepting_a_candidate_is_previewed_and_then_establishes_an_override()
    {
        var prdb = new FixtureIdentificationClient().Suggestive("first.mp4", WorkId, "A Guessed Work");
        await using var store = await CreateAsync(prdb, ("first.mp4", [1, 2, 3, 4]));

        await using var scope = store.Scope();
        var review = scope.ServiceProvider.GetRequiredService<IdentificationReviewService>();
        var open = (await review.GetQueueAsync(TestContext.Current.CancellationToken)).Single();
        var request = new IdentificationDecisionRequest(
            IdentificationDecisionAction.AcceptCandidate,
            IdentificationDimension.WorkIdentification,
            open.CaseVersion,
            Confirm: false,
            CandidateId: open.Candidate.Id);

        var preview = await review.DecideAsync(
            Administrator,
            open.VideoId,
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(IdentificationDecisionVerdict.Preview, preview.Verdict);
        Assert.Contains("Administrative Override", preview.Consequence!.ClaimTransition);
        Assert.False(preview.Consequence.MergesAnotherVideo);
        Assert.False(preview.Consequence.RequiresNote);
        Assert.Equal(IdentificationReviewStatus.Clear, preview.Consequence.ResultingReviewStatus);
        Assert.Equal(
            IdentificationResolution.Unknown,
            preview.Case!.Identification.Work.Resolution);

        var applied = await review.DecideAsync(
            Administrator,
            open.VideoId,
            request with { Confirm = true },
            TestContext.Current.CancellationToken);

        Assert.Equal(IdentificationDecisionVerdict.Applied, applied.Verdict);
        Assert.Equal(
            IdentificationResolution.Established,
            applied.Case!.Identification.Work.Resolution);
        Assert.True(applied.Case.Identification.Work.AdministrativeOverride);
        Assert.Equal(
            IdentificationSource.AdministratorDecision,
            applied.Case.Identification.Work.Source);
        Assert.Equal(
            IdentificationReviewStatus.Clear,
            applied.Case.Identification.Work.ReviewStatus);
        Assert.Empty(applied.Case.OpenCandidates);
        var decision = Assert.Single(applied.Case.Decisions);
        Assert.Equal(IdentificationDecisionAction.AcceptCandidate, decision.Action);
        Assert.Equal("Unknown", decision.PriorState);
        Assert.Contains("Administrative Override", decision.ResultingState);
    }

    [Fact]
    public async Task Automation_reports_against_an_override_without_replacing_it()
    {
        var prdb = new FixtureIdentificationClient().Suggestive("first.mp4", WorkId, "A Guessed Work");
        await using var store = await CreateAsync(prdb, ("first.mp4", [1, 2, 3, 4]));
        await AcceptFirstAsync(store);

        prdb.Conclusive("first.mp4", OtherWorkId, "A Different Work");
        await LibraryPipeline.ReofferAsync(store, "first.mp4");

        await using var scope = store.Scope();
        var identificationCase = await scope.ServiceProvider
            .GetRequiredService<IdentificationReviewService>()
            .GetCaseAsync(await OnlyVideoAsync(store), TestContext.Current.CancellationToken);
        Assert.Equal(
            "A Guessed Work",
            identificationCase!.Identification.Work.TargetTitle);
        Assert.True(identificationCase.Identification.Work.AdministrativeOverride);
        Assert.Equal(
            IdentificationReviewStatus.ReviewNeeded,
            identificationCase.Identification.Work.ReviewStatus);
        var candidate = Assert.Single(identificationCase.OpenCandidates);
        Assert.Equal(
            IdentificationReviewReason.ConflictsWithAdministrativeOverride,
            candidate.Reason);
        Assert.Contains("Administrative Override", identificationCase.Explanation);
    }

    [Fact]
    public async Task Rejecting_suppresses_the_same_evidence_and_stronger_evidence_returns_it()
    {
        var prdb = new FixtureIdentificationClient().Suggestive("first.mp4", WorkId, "A Guessed Work");
        await using var store = await CreateAsync(prdb, ("first.mp4", [1, 2, 3, 4]));

        await using (var scope = store.Scope())
        {
            var review = scope.ServiceProvider.GetRequiredService<IdentificationReviewService>();
            var open = (await review.GetQueueAsync(TestContext.Current.CancellationToken)).Single();
            var rejected = await review.DecideAsync(
                Administrator,
                open.VideoId,
                new IdentificationDecisionRequest(
                    IdentificationDecisionAction.RejectCandidate,
                    IdentificationDimension.WorkIdentification,
                    open.CaseVersion,
                    Confirm: true,
                    CandidateId: open.Candidate.Id),
                TestContext.Current.CancellationToken);
            Assert.Equal(IdentificationDecisionVerdict.Applied, rejected.Verdict);
            Assert.Equal(
                IdentificationResolution.Unknown,
                rejected.Case!.Identification.Work.Resolution);
            Assert.Empty(rejected.Case.OpenCandidates);
        }

        await LibraryPipeline.ReofferAsync(store, "first.mp4");

        await using (var scope = store.Scope())
        {
            Assert.Empty(await scope.ServiceProvider
                .GetRequiredService<IdentificationReviewService>()
                .GetQueueAsync(TestContext.Current.CancellationToken));
        }

        prdb.Conclusive("first.mp4", WorkId, "A Guessed Work");
        await LibraryPipeline.ReofferAsync(store, "first.mp4");

        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            var claim = await database.IdentificationClaims
                .SingleAsync(
                    row => row.Dimension == IdentificationDimension.WorkIdentification,
                    TestContext.Current.CancellationToken);
            Assert.Equal(IdentificationClaimStatus.Current, claim.Status);
            Assert.Equal(WorkId, claim.TargetKey);
        }
    }

    [Fact]
    public async Task A_stale_confirmation_is_refused_and_shows_the_refreshed_case()
    {
        var prdb = new FixtureIdentificationClient().Suggestive("first.mp4", WorkId, "A Guessed Work");
        await using var store = await CreateAsync(prdb, ("first.mp4", [1, 2, 3, 4]));

        await using var scope = store.Scope();
        var review = scope.ServiceProvider.GetRequiredService<IdentificationReviewService>();
        var open = (await review.GetQueueAsync(TestContext.Current.CancellationToken)).Single();

        var stale = await review.DecideAsync(
            Administrator,
            open.VideoId,
            new IdentificationDecisionRequest(
                IdentificationDecisionAction.AcceptCandidate,
                IdentificationDimension.WorkIdentification,
                open.CaseVersion - 1,
                Confirm: true,
                CandidateId: open.Candidate.Id),
            TestContext.Current.CancellationToken);

        Assert.Equal(IdentificationDecisionVerdict.Stale, stale.Verdict);
        Assert.Equal(open.CaseVersion, stale.Case!.CaseVersion);
        Assert.Equal(IdentificationResolution.Unknown, stale.Case.Identification.Work.Resolution);
        Assert.Single(stale.Case.OpenCandidates);
    }

    [Fact]
    public async Task Replacing_a_claim_requires_a_note_and_keeps_the_earlier_claim_in_history()
    {
        var prdb = new FixtureIdentificationClient().Conclusive("first.mp4", WorkId, "A Known Work");
        await using var store = await CreateAsync(prdb, ("first.mp4", [1, 2, 3, 4]));
        var videoId = await OnlyVideoAsync(store);

        await using var scope = store.Scope();
        var review = scope.ServiceProvider.GetRequiredService<IdentificationReviewService>();
        var identificationCase = await review.GetCaseAsync(
            videoId,
            TestContext.Current.CancellationToken);
        var request = new IdentificationDecisionRequest(
            IdentificationDecisionAction.ReplaceClaim,
            IdentificationDimension.WorkIdentification,
            identificationCase!.CaseVersion,
            Confirm: true,
            TargetKey: OtherWorkId,
            TargetTitle: "A Corrected Work");

        var refused = await review.DecideAsync(
            Administrator,
            videoId,
            request,
            TestContext.Current.CancellationToken);
        Assert.Equal(IdentificationDecisionVerdict.NoteRequired, refused.Verdict);
        Assert.True(refused.Consequence!.RequiresNote);

        var applied = await review.DecideAsync(
            Administrator,
            videoId,
            request with { Note = "The remote catalogue matched the wrong cut." },
            TestContext.Current.CancellationToken);

        Assert.Equal(IdentificationDecisionVerdict.Applied, applied.Verdict);
        Assert.Equal("A Corrected Work", applied.Case!.Identification.Work.TargetTitle);
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var claims = await database.IdentificationClaims
            .Where(claim => claim.Dimension == IdentificationDimension.WorkIdentification)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, claims.Count);
        Assert.Single(claims, claim => claim.Status == IdentificationClaimStatus.Superseded &&
                                       claim.TargetKey == WorkId);
    }

    [Fact]
    public async Task Revoking_leaves_the_video_unknown_and_offers_its_evidence_again()
    {
        var prdb = new FixtureIdentificationClient().Suggestive("first.mp4", WorkId, "A Guessed Work");
        await using var store = await CreateAsync(prdb, ("first.mp4", [1, 2, 3, 4]));
        await AcceptFirstAsync(store);
        var videoId = await OnlyVideoAsync(store);

        await using (var scope = store.Scope())
        {
            var review = scope.ServiceProvider.GetRequiredService<IdentificationReviewService>();
            var identificationCase = await review.GetCaseAsync(
                videoId,
                TestContext.Current.CancellationToken);
            var revoked = await review.DecideAsync(
                Administrator,
                videoId,
                new IdentificationDecisionRequest(
                    IdentificationDecisionAction.RevokeClaim,
                    IdentificationDimension.WorkIdentification,
                    identificationCase!.CaseVersion,
                    Confirm: true,
                    Note: "The accepted match was wrong."),
                TestContext.Current.CancellationToken);

            Assert.Equal(IdentificationDecisionVerdict.Applied, revoked.Verdict);
            Assert.Equal(
                IdentificationResolution.Unknown,
                revoked.Case!.Identification.Work.Resolution);
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            Assert.Single(await database.IdentificationClaims
                .Where(claim => claim.Status == IdentificationClaimStatus.Revoked)
                .ToListAsync(TestContext.Current.CancellationToken));
            Assert.Null((await database.VideoFiles.SingleAsync(
                TestContext.Current.CancellationToken)).IdentifiedSha256);
        }

        prdb.Conclusive("first.mp4", OtherWorkId, "A Conclusive Work");
        await LibraryPipeline.DrainAsync(store);

        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            var current = await database.IdentificationClaims.SingleAsync(
                claim => claim.Status == IdentificationClaimStatus.Current &&
                         claim.Dimension == IdentificationDimension.WorkIdentification,
                TestContext.Current.CancellationToken);
            Assert.Equal(OtherWorkId, current.TargetKey);
        }
    }

    [Fact]
    public async Task Assigning_a_target_established_elsewhere_previews_and_performs_the_merge()
    {
        var prdb = new FixtureIdentificationClient()
            .Conclusive("first.mp4", WorkId, "A Known Work")
            .Unmatched("second.mp4");
        await using var store = await CreateAsync(
            prdb,
            ("first.mp4", [1, 2, 3, 4]),
            ("second.mp4", [5, 6, 7, 8]));

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var unknown = await database.VideoFiles
            .Where(file => file.RelativePath == "second.mp4")
            .Select(file => file.VideoId)
            .SingleAsync(TestContext.Current.CancellationToken);
        var review = scope.ServiceProvider.GetRequiredService<IdentificationReviewService>();
        var identificationCase = await review.GetCaseAsync(
            unknown,
            TestContext.Current.CancellationToken);
        var request = new IdentificationDecisionRequest(
            IdentificationDecisionAction.AssignDirectly,
            IdentificationDimension.WorkIdentification,
            identificationCase!.CaseVersion,
            Confirm: false,
            TargetKey: WorkId,
            TargetTitle: "A Known Work");

        var preview = await review.DecideAsync(
            Administrator,
            unknown,
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(IdentificationDecisionVerdict.Preview, preview.Verdict);
        Assert.True(preview.Consequence!.MergesAnotherVideo);
        Assert.Contains("already carries this work identity", preview.Consequence.MergeSummary);
        Assert.True(preview.Consequence.RequiresNote);
        Assert.Equal(2, preview.Consequence.AffectedVideoFileCount);

        var applied = await review.DecideAsync(
            Administrator,
            unknown,
            request with { Confirm = true, Note = "Both files carry the same work." },
            TestContext.Current.CancellationToken);

        Assert.Equal(IdentificationDecisionVerdict.Applied, applied.Verdict);
        Assert.Equal(2, applied.Case!.VideoFiles.Count);
        Assert.True(applied.Case.Identification.Work.AdministrativeOverride);
        Assert.Single(await database.Videos
            .Where(video => video.SurvivingVideoId == null)
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Single(await database.IdentificationDecisions
            .Where(decision => decision.MergedAnotherVideo)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_site_decision_that_would_contradict_an_identified_work_is_unavailable()
    {
        var prdb = new FixtureIdentificationClient().Conclusive(
            "first.mp4",
            WorkId,
            "A Known Work",
            new RemoteSite("5b1a2c34-0000-4000-8000-0000000000aa", "Known Site", null));
        await using var store = await CreateAsync(prdb, ("first.mp4", [1, 2, 3, 4]));
        var videoId = await OnlyVideoAsync(store);

        await using var scope = store.Scope();
        var review = scope.ServiceProvider.GetRequiredService<IdentificationReviewService>();
        var identificationCase = await review.GetCaseAsync(
            videoId,
            TestContext.Current.CancellationToken);
        Assert.Contains(
            IdentificationDecisionAction.ReplaceClaim,
            identificationCase!.UnavailableSiteActions);

        var refused = await review.DecideAsync(
            Administrator,
            videoId,
            new IdentificationDecisionRequest(
                IdentificationDecisionAction.ReplaceClaim,
                IdentificationDimension.SiteRecognition,
                identificationCase.CaseVersion,
                Confirm: true,
                TargetKey: "5b1a2c34-0000-4000-8000-0000000000bb",
                TargetTitle: "Another Site",
                Note: "Looks like another site."),
            TestContext.Current.CancellationToken);

        Assert.Equal(IdentificationDecisionVerdict.ActionUnavailable, refused.Verdict);
        Assert.Equal("Known Site", refused.Case!.Identification.Site.TargetTitle);
    }

    [Fact]
    public async Task Splitting_a_merged_video_reactivates_the_previous_identity()
    {
        var prdb = new FixtureIdentificationClient()
            .Conclusive("first.mp4", WorkId, "A Known Work")
            .Conclusive("second.mp4", WorkId, "A Known Work");
        await using var store = await CreateAsync(
            prdb,
            ("first.mp4", [1, 2, 3, 4]),
            ("second.mp4", [5, 6, 7, 8]));
        var survivorId = await OnlyVideoAsync(store);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var separated = await database.VideoFiles
            .Where(file => file.RelativePath == "second.mp4")
            .Select(file => new { file.Id, file.PreviousVideoId })
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(separated.PreviousVideoId);
        var review = scope.ServiceProvider.GetRequiredService<IdentificationReviewService>();
        var identificationCase = await review.GetCaseAsync(
            survivorId,
            TestContext.Current.CancellationToken);

        var applied = await review.DecideAsync(
            Administrator,
            survivorId,
            new IdentificationDecisionRequest(
                IdentificationDecisionAction.SplitVideo,
                IdentificationDimension.WorkIdentification,
                identificationCase!.CaseVersion,
                Confirm: true,
                Note: "These files are different works.",
                SeparatedVideoFileIds: [separated.Id]),
            TestContext.Current.CancellationToken);

        Assert.Equal(IdentificationDecisionVerdict.Applied, applied.Verdict);
        Assert.Single(applied.Case!.VideoFiles);
        var reactivated = await database.Videos.SingleAsync(
            video => video.Id == separated.PreviousVideoId,
            TestContext.Current.CancellationToken);
        Assert.Null(reactivated.SurvivingVideoId);
        Assert.Equal(
            reactivated.Id,
            await database.VideoFiles
                .Where(file => file.Id == separated.Id)
                .Select(file => file.VideoId)
                .SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            2,
            (await scope.ServiceProvider
                .GetRequiredService<VideoCatalog>()
                .GetAsync(Guid.CreateVersion7(), LibraryPipeline.ClientContext, TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task A_merge_preserves_both_private_viewing_histories_without_exposing_them()
    {
        var prdb = new FixtureIdentificationClient()
            .Conclusive("first.mp4", WorkId, "A Known Work")
            .Unmatched("second.mp4");
        await using var store = await CreateAsync(
            prdb,
            ("first.mp4", [1, 2, 3, 4]),
            ("second.mp4", [5, 6, 7, 8]));
        var accountId = await AccountAsync(store);
        await WatchAsync(store, accountId, "first.mp4");
        await WatchAsync(store, accountId, "second.mp4");

        Guid unknown;
        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            unknown = await database.VideoFiles
                .Where(file => file.RelativePath == "second.mp4")
                .Select(file => file.VideoId)
                .SingleAsync(TestContext.Current.CancellationToken);
            var review = scope.ServiceProvider.GetRequiredService<IdentificationReviewService>();
            var identificationCase = await review.GetCaseAsync(
                unknown,
                TestContext.Current.CancellationToken);
            var applied = await review.DecideAsync(
                Administrator,
                unknown,
                new IdentificationDecisionRequest(
                    IdentificationDecisionAction.AssignDirectly,
                    IdentificationDimension.WorkIdentification,
                    identificationCase!.CaseVersion,
                    Confirm: true,
                    TargetKey: WorkId,
                    TargetTitle: "A Known Work",
                    Note: "Both files carry the same work."),
                TestContext.Current.CancellationToken);
            Assert.Equal(IdentificationDecisionVerdict.Applied, applied.Verdict);
        }

        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            var state = await database.PersonalVideoStates
                .SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(2, state.PlayCount);
            Assert.True(state.AccumulatedWatchDurationMilliseconds >= 10_000);
            Assert.Equal(
                2,
                await database.PlaybackAttempts.CountAsync(
                    attempt => attempt.VideoId == state.VideoId,
                    TestContext.Current.CancellationToken));
        }
    }

    private static async Task AcceptFirstAsync(TestDatabase store)
    {
        await using var scope = store.Scope();
        var review = scope.ServiceProvider.GetRequiredService<IdentificationReviewService>();
        var open = (await review.GetQueueAsync(TestContext.Current.CancellationToken)).Single();
        var applied = await review.DecideAsync(
            Administrator,
            open.VideoId,
            new IdentificationDecisionRequest(
                IdentificationDecisionAction.AcceptCandidate,
                open.Dimension,
                open.CaseVersion,
                Confirm: true,
                CandidateId: open.Candidate.Id),
            TestContext.Current.CancellationToken);
        Assert.Equal(IdentificationDecisionVerdict.Applied, applied.Verdict);
    }

    private static async Task<Guid> OnlyVideoAsync(TestDatabase store)
    {
        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        return await database.Videos
            .Where(video => video.SurvivingVideoId == null)
            .Select(video => video.Id)
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<Guid> AccountAsync(TestDatabase store)
    {
        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var account = new AccountRow
        {
            Id = Guid.CreateVersion7(),
            Username = "viewer",
            NormalizedUsername = "VIEWER",
            PasswordHash = "hash",
            Authority = AccountAuthority.User,
            State = AccountState.Approved,
            RegisteredAt = DateTime.SpecifyKind(new DateTime(2026, 8, 27), DateTimeKind.Utc),
        };
        database.Accounts.Add(account);
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        return account.Id;
    }

    private static async Task WatchAsync(TestDatabase store, Guid accountId, string relativePath)
    {
        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var file = await database.VideoFiles.SingleAsync(
            candidate => candidate.RelativePath == relativePath,
            TestContext.Current.CancellationToken);
        var personal = scope.ServiceProvider.GetRequiredService<PersonalStateService>();
        var attempt = await personal.StartPlaybackAttemptAsync(
            accountId,
            file.VideoId,
            file.Id,
            TestContext.Current.CancellationToken);
        var report = await personal.ReportPlaybackAsync(
            accountId,
            attempt.PlaybackAttemptId!.Value,
            Guid.CreateVersion7(),
            0,
            file.Id,
            11_000,
            11_000,
            false,
            true,
            TestContext.Current.CancellationToken);
        Assert.Equal(Core.Personal.PlaybackReportVerdict.Accepted, report.Verdict);
    }

    private static async Task<TestDatabase> CreateAsync(
        FixtureIdentificationClient prdb,
        params (string Name, byte[] Content)[] files)
    {
        var store = await TestDatabase.CreateAsync(
            prdbConnectionVerifier: new FixtureConnectionVerifier(),
            mediaProbe: new FixtureProbe(),
            hasher: new FixtureHasher(),
            previewGenerator: new FixturePreviewGenerator(),
            identificationClient: prdb);
        var source = Path.Combine(store.LibraryMountRoot.Path, "source");
        Directory.CreateDirectory(source);

        foreach (var (name, content) in files)
        {
            await File.WriteAllBytesAsync(
                Path.Combine(source, name),
                content,
                TestContext.Current.CancellationToken);
        }

        await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");
        await LibraryPipeline.DrainAsync(store);
        return store;
    }
}
