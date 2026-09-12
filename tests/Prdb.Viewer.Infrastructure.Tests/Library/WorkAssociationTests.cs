using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Core.Personal;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;
using Prdb.Viewer.Infrastructure.Personal;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

/// <summary>
/// Several encodes of one unidentified work stopping being several Videos.
///
/// `VISION.md` states the rule plainly: an unidentified file initially represents its own local
/// Video because there is no evidence that it belongs with another one. This is where that evidence
/// exists — and where it stops short, because an association merges Videos and a merge is not a
/// label that can be peeled off.
/// </summary>
public sealed class WorkAssociationTests
{
    /// <summary>Two encodes of one work: two bits apart.</summary>
    private const string Original = "0f0f0f0f0f0f0f0f";

    private const string Reencode = "0f0f0f0f0f0f0f0c";

    private const string Unrelated = "f0f0f0f0f0f0f0f0";

    [Fact]
    public async Task Two_unknown_videos_whose_files_agree_become_one_video_that_is_still_unknown()
    {
        await using var store = await CreateAsync();

        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var video = Assert.Single(await database.Videos
            .Where(row => row.SurvivingVideoId == null)
            .ToListAsync(TestContext.Current.CancellationToken));

        // Both files' facts survive, because that is what a later Split and any later
        // identification have to work from.
        var files = await database.VideoFiles
            .Where(file => file.VideoId == video.Id)
            .OrderBy(file => file.RelativePath)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["original.mp4", "re-encode.mp4"], files.Select(file => file.RelativePath));
        Assert.Equal(new[] { Original, Reencode }.Order(), files.Select(file => file.PerceptualHash).Order());

        // It names no work. The surviving Video is still an Unknown Video.
        Assert.False(video.HasEstablishedWork);
        Assert.Empty(await database.IdentificationClaims
            .Where(claim => claim.Dimension == IdentificationDimension.WorkIdentification)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// An association nobody can account for is exactly what the evidence principle forbids, so it
    /// carries what it was concluded from and by what.
    /// </summary>
    [Fact]
    public async Task An_established_association_keeps_the_reading_it_was_concluded_from()
    {
        await using var store = await CreateAsync();
        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var association = Assert.Single(await database.WorkAssociations
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(WorkAssociationStatus.Established, association.Status);
        Assert.Equal(IdentificationSource.LocalInference, association.Source);
        Assert.Equal(2, association.Distance);
        Assert.True(association.DurationsAgree);
        Assert.Null(association.DecidedByAccountId);
        Assert.NotNull(association.EstablishedAt);

        // And it is readable on the Video, the way an Identification Claim's provenance is.
        var account = await LibraryPipeline.AccountAsync(store);
        var video = await database.Videos
            .Where(row => row.SurvivingVideoId == null)
            .SingleAsync(TestContext.Current.CancellationToken);
        var detail = await scope.ServiceProvider
            .GetRequiredService<LibraryDiscovery>()
            .GetVideoAsync(
                account,
                LibraryPipeline.ClientContext,
                video.Id,
                TestContext.Current.CancellationToken);
        var shown = Assert.Single(detail!.Associations!);
        Assert.Equal(WorkAssociationStatus.Established, shown.Status);
        Assert.Contains("names no work", shown.Summary);
        Assert.Contains("still Unknown", shown.Summary);
    }

    /// <summary>
    /// The narrow path is narrow. A pair whose running times disagree does not merge; it waits for
    /// a person, and the wait is a case in the ordinary queue rather than a silence.
    /// </summary>
    [Fact]
    public async Task A_pair_that_does_not_clear_the_narrow_path_waits_for_an_administrator()
    {
        await using var store = await CreateAsync(reencodeDuration: 11_000);
        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        Assert.Equal(
            2,
            await database.Videos.CountAsync(
                row => row.SurvivingVideoId == null,
                TestContext.Current.CancellationToken));

        var association = Assert.Single(await database.WorkAssociations
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(WorkAssociationStatus.Proposed, association.Status);

        var item = Assert.Single(await scope.ServiceProvider
            .GetRequiredService<IdentificationReviewService>()
            .QueueAsync(TestContext.Current.CancellationToken));
        Assert.Null(item.Candidate);
        Assert.NotNull(item.Association);
        Assert.Equal(WorkAssociationStatus.Proposed, item.Association.Status);
        Assert.Contains("may carry the same content", item.Association.Summary);
        Assert.Equal("re-encode.mp4", item.Association.OtherRelativePath);
    }

    [Fact]
    public async Task Files_that_do_not_look_alike_are_never_associated()
    {
        await using var store = await CreateAsync(reencodeHash: Unrelated);
        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        Assert.Empty(await scope.ServiceProvider
            .GetRequiredService<ViewerDbContext>()
            .WorkAssociations
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// An Administrator answering a proposal is answering whether two files carry the same content
    /// — not what that content is. The Video that comes out is still Unknown.
    /// </summary>
    [Fact]
    public async Task An_administrator_can_associate_two_videos_without_naming_anything()
    {
        await using var store = await CreateAsync(reencodeDuration: 11_000);
        await LibraryPipeline.DrainAsync(store);
        var administrator = await LibraryPipeline.AccountAsync(store, "administrator");

        await using var scope = store.Scope();
        var review = scope.ServiceProvider.GetRequiredService<IdentificationReviewService>();
        var item = Assert.Single(await review.QueueAsync(TestContext.Current.CancellationToken));

        // The consequence is said before it is taken, and a merge always asks for a note.
        var preview = await review.DecideAsync(
            administrator,
            item.VideoId,
            Request(item, IdentificationDecisionAction.AssociateVideos, confirm: false),
            TestContext.Current.CancellationToken);
        Assert.Equal(IdentificationDecisionVerdict.Preview, preview.Verdict);
        Assert.True(preview.Consequence!.RequiresNote);
        Assert.Contains("become one", preview.Consequence.ClaimTransition);

        var result = await review.DecideAsync(
            administrator,
            item.VideoId,
            Request(item, IdentificationDecisionAction.AssociateVideos, confirm: true, note: "One work."),
            TestContext.Current.CancellationToken);
        Assert.Equal(IdentificationDecisionVerdict.Applied, result.Verdict);

        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var video = Assert.Single(await database.Videos
            .Where(row => row.SurvivingVideoId == null)
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.False(video.HasEstablishedWork);
        Assert.Equal(
            2,
            await database.VideoFiles.CountAsync(
                file => file.VideoId == video.Id,
                TestContext.Current.CancellationToken));

        var association = Assert.Single(await database.WorkAssociations
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(WorkAssociationStatus.Established, association.Status);
        Assert.Equal(IdentificationSource.AdministratorDecision, association.Source);
        Assert.Equal(administrator, association.DecidedByAccountId);
        Assert.Equal("One work.", association.Note);

        // The act is in the Video's own history, as every other decision is.
        var decision = Assert.Single(await database.IdentificationDecisions
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(IdentificationDecisionAction.AssociateVideos, decision.Action);
        Assert.True(decision.MergedAnotherVideo);
    }

    /// <summary>
    /// Rejecting says the two are not the same content, and the rule does not conclude it again
    /// from the same reading afterwards.
    /// </summary>
    [Fact]
    public async Task A_rejected_association_is_not_concluded_again_from_the_same_reading()
    {
        await using var store = await CreateAsync(reencodeDuration: 11_000);
        await LibraryPipeline.DrainAsync(store);
        var administrator = await LibraryPipeline.AccountAsync(store, "administrator");

        await using (var scope = store.Scope())
        {
            var review = scope.ServiceProvider.GetRequiredService<IdentificationReviewService>();
            var item = Assert.Single(await review.QueueAsync(TestContext.Current.CancellationToken));
            var result = await review.DecideAsync(
                administrator,
                item.VideoId,
                Request(item, IdentificationDecisionAction.RejectAssociation, confirm: true),
                TestContext.Current.CancellationToken);
            Assert.Equal(IdentificationDecisionVerdict.Applied, result.Verdict);
        }

        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            var files = await database.VideoFiles
                .Select(file => file.Id)
                .ToListAsync(TestContext.Current.CancellationToken);
            await scope.ServiceProvider
                .GetRequiredService<LocalSimilarityService>()
                .OfferToNeighboursAsync(files, TestContext.Current.CancellationToken);
        }

        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            var association = Assert.Single(await database.WorkAssociations
                .ToListAsync(TestContext.Current.CancellationToken));
            Assert.Equal(WorkAssociationStatus.Rejected, association.Status);
            Assert.Equal(
                2,
                await database.Videos.CountAsync(
                    row => row.SurvivingVideoId == null,
                    TestContext.Current.CancellationToken));
        }
    }

    /// <summary>
    /// A wrongly associated pair is separated with the mechanism that already separates Video
    /// Files, not with a new one — and the record of the association stays, because it is what
    /// stops the rule concluding it again from the same reading.
    /// </summary>
    [Fact]
    public async Task A_split_undoes_an_association_and_the_rule_does_not_redo_it()
    {
        await using var store = await CreateAsync();
        await LibraryPipeline.DrainAsync(store);
        var administrator = await LibraryPipeline.AccountAsync(store, "administrator");
        Guid separated;

        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            var video = await database.Videos
                .Where(row => row.SurvivingVideoId == null)
                .SingleAsync(TestContext.Current.CancellationToken);
            separated = await database.VideoFiles
                .Where(file => file.RelativePath == "re-encode.mp4")
                .Select(file => file.Id)
                .SingleAsync(TestContext.Current.CancellationToken);
            var result = await scope.ServiceProvider
                .GetRequiredService<IdentificationReviewService>()
                .DecideAsync(
                    administrator,
                    video.Id,
                    new IdentificationDecisionRequest(
                        IdentificationDecisionAction.SplitVideo,
                        IdentificationDimension.WorkIdentification,
                        video.CaseVersion,
                        Confirm: true,
                        Note: "Not the same work after all.",
                        SeparatedVideoFileIds: [separated]),
                    TestContext.Current.CancellationToken);
            Assert.Equal(IdentificationDecisionVerdict.Applied, result.Verdict);
        }

        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            Assert.Equal(
                WorkAssociationStatus.Separated,
                (await database.WorkAssociations.SingleAsync(TestContext.Current.CancellationToken))
                    .Status);

            var files = await database.VideoFiles
                .Select(file => file.Id)
                .ToListAsync(TestContext.Current.CancellationToken);
            await scope.ServiceProvider
                .GetRequiredService<LocalSimilarityService>()
                .OfferToNeighboursAsync(files, TestContext.Current.CancellationToken);
        }

        await using (var last = store.Scope())
        {
            Assert.Equal(
                2,
                await last.ServiceProvider
                    .GetRequiredService<ViewerDbContext>()
                    .Videos
                    .CountAsync(
                        row => row.SurvivingVideoId == null,
                        TestContext.Current.CancellationToken));
        }
    }

    /// <summary>
    /// What the merge does to two Users' private viewing state, stated here rather than discovered
    /// later. Play Counts sum, completion history stays true, the more recent activity supplies the
    /// resume position, and the earlier Favourite wins.
    /// </summary>
    [Fact]
    public async Task Personal_state_watched_separately_survives_the_merge_honestly()
    {
        await using var store = await CreateAsync(reencodeDuration: 11_000);
        await LibraryPipeline.DrainAsync(store);
        var viewer = await LibraryPipeline.AccountAsync(store);
        var administrator = await LibraryPipeline.AccountAsync(store, "administrator");
        Guid firstVideo;
        Guid secondVideo;

        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            var videos = await database.Videos
                .Where(row => row.SurvivingVideoId == null)
                .OrderBy(row => row.DiscoveryDate)
                .ToListAsync(TestContext.Current.CancellationToken);
            firstVideo = videos[0].Id;
            secondVideo = videos[1].Id;

            // Both sides were watched separately, by one User, before anybody knew they were one
            // work: two Play Counts, a completion on one side, a resume position on the other.
            database.PersonalVideoStates.AddRange(
                State(viewer, firstVideo, playCount: 2, completed: true, progress: null,
                    at: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)),
                State(viewer, secondVideo, playCount: 3, completed: false, progress: 4_000,
                    at: new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc)));
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var scope = store.Scope())
        {
            var review = scope.ServiceProvider.GetRequiredService<IdentificationReviewService>();
            var item = Assert.Single(await review.QueueAsync(TestContext.Current.CancellationToken));
            await review.DecideAsync(
                administrator,
                item.VideoId,
                Request(item, IdentificationDecisionAction.AssociateVideos, confirm: true, note: "One work."),
                TestContext.Current.CancellationToken);
        }

        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            var state = Assert.Single(await database.PersonalVideoStates
                .Where(row => row.AccountId == viewer)
                .ToListAsync(TestContext.Current.CancellationToken));

            // Play Counts sum: both viewing cycles happened, and neither is denied.
            Assert.Equal(5, state.PlayCount);

            // Viewing Completion is history and cannot be untrue: watching one encode to the end
            // is watching the work to the end.
            Assert.True(state.HasViewingCompletion);

            // The resume position is the one the more recent activity established. A position
            // observed on one file is not proportionally guessed onto the other.
            Assert.Equal(4_000, state.PlaybackProgressMilliseconds);
            Assert.Equal(PersonalPlayState.InProgress, state.PlayState);

            // Nothing about it reaches the Administrator who decided the merge.
            var decision = await database.IdentificationDecisions
                .SingleAsync(TestContext.Current.CancellationToken);
            Assert.DoesNotContain("4000", decision.ResultingState);
        }
    }

    private static PersonalVideoStateRow State(
        Guid accountId,
        Guid videoId,
        int playCount,
        bool completed,
        long? progress,
        DateTime at) =>
        new()
        {
            AccountId = accountId,
            VideoId = videoId,
            PlayCount = playCount,
            HasViewingCompletion = completed,
            LastCompletedAt = completed ? at : null,
            PlaybackProgressMilliseconds = progress,
            PlayState = completed ? PersonalPlayState.Completed : PersonalPlayState.InProgress,
            PlayStateChangedAt = at,
            LastQualifiedActivityAt = at,
            UpdatedAt = at,
        };

    private static IdentificationDecisionRequest Request(
        IdentificationQueueItem item,
        IdentificationDecisionAction action,
        bool confirm,
        string? note = null) =>
        new(
            action,
            IdentificationDimension.WorkIdentification,
            item.CaseVersion,
            confirm,
            Note: note,
            AssociationId: item.Association!.Id);

    /// <summary>Two files prdb has never heard of, which look like each other.</summary>
    private static async Task<TestDatabase> CreateAsync(
        long reencodeDuration = 12_345,
        string reencodeHash = Reencode)
    {
        var hashes = new Dictionary<string, string>
        {
            ["original.mp4"] = Original,
            ["re-encode.mp4"] = reencodeHash,
        };
        var store = await TestDatabase.CreateAsync(
            mediaProbe: new FixtureProbe(duration: path =>
                Path.GetFileName(path) == "re-encode.mp4" ? reencodeDuration : 12_345),
            hasher: new FixtureHasher(path => new VideoFileHashes(
                FixtureHasher.OsHashOf(path),
                hashes[Path.GetFileName(path)],
                null)),
            previewGenerator: new FixturePreviewGenerator(),
            identificationClient: new FixtureIdentificationClient()
                .Unmatched("original.mp4")
                .Unmatched("re-encode.mp4"));
        var source = Path.Combine(store.LibraryMountRoot.Path, "source");
        Directory.CreateDirectory(source);

        foreach (var (name, index) in hashes.Keys.Select((name, index) => (name, index)))
        {
            await File.WriteAllBytesAsync(
                Path.Combine(source, name),
                [1, 2, 3, (byte)index],
                TestContext.Current.CancellationToken);
        }

        await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");
        return store;
    }
}
