using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

/// <summary>
/// One decision over a whole Identification Review Group, applied case by case.
///
/// What a group settles has to be exactly what settling each of its cases would have been, or the
/// count on the button is not a promise about anything — so a group reaches the same decision path
/// once per case rather than a second path of its own.
/// </summary>
public sealed class GroupDecisionTests
{
    private const string OneSite = "5b1a2c34-0000-4000-8000-0000000006aa";

    [Fact]
    public async Task A_group_is_settled_with_one_decision_and_one_note()
    {
        await using var store = await CreateAsync(cases: 4);
        var administrator = await LibraryPipeline.AccountAsync(store, "administrator");
        var plan = await PlanAsync(store);

        var result = await DecideAsync(
            store,
            administrator,
            plan,
            IdentificationDecisionAction.AcceptCandidate,
            "All of these are that site.");

        Assert.Equal(IdentificationGroupDecisionVerdict.Applied, result.Verdict);
        Assert.Equal(4, result.Applied);
        Assert.Empty(result.Skipped);
        Assert.Empty(result.Refused);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        Assert.Equal(
            4,
            await database.IdentificationClaims.CountAsync(
                claim => claim.Dimension == IdentificationDimension.SiteRecognition &&
                         claim.Status == IdentificationClaimStatus.Current &&
                         claim.IsAdministrativeOverride,
                TestContext.Current.CancellationToken));

        // One record per case, one act in history: each Video keeps its own decision with its own
        // prior and resulting state, and all of them name the same act.
        var decisions = await database.IdentificationDecisions
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(4, decisions.Count);
        Assert.Single(decisions.Select(decision => decision.GroupDecisionId).Distinct());
        Assert.All(decisions, decision => Assert.NotNull(decision.GroupDecisionId));
        Assert.All(decisions, decision => Assert.Equal("All of these are that site.", decision.Note));
        Assert.All(decisions, decision => Assert.Equal("Unknown", decision.PriorState));
    }

    /// <summary>
    /// A backlog moves while it is being read. All-or-nothing over four hundred cases would mean
    /// one moved case throws the reviewer's work away.
    /// </summary>
    [Fact]
    public async Task A_case_that_changed_underneath_is_skipped_and_the_rest_still_applies()
    {
        await using var store = await CreateAsync(cases: 3);
        var administrator = await LibraryPipeline.AccountAsync(store, "administrator");
        var plan = await PlanAsync(store);
        var moved = plan.Cases[1];

        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            await database.Videos
                .Where(video => video.Id == moved.VideoId)
                .ExecuteUpdateAsync(
                    update => update.SetProperty(video => video.CaseVersion, video => video.CaseVersion + 1),
                    TestContext.Current.CancellationToken);
        }

        var result = await DecideAsync(
            store,
            administrator,
            plan,
            IdentificationDecisionAction.RejectCandidate);

        Assert.Equal(2, result.Applied);
        Assert.Equal(moved.VideoId, Assert.Single(result.Skipped).VideoId);
        Assert.Contains("changed while the group was being read", Assert.Single(result.Skipped).Reason);
        Assert.Contains("2 cases settled", result.Summary);
        Assert.Contains("One case changed", result.Summary);
    }

    /// <summary>
    /// Accept and reject are the two a group can carry at all. The others are per-Video judgements,
    /// and the refusal says why rather than leaving a disabled button to explain itself.
    /// </summary>
    [Theory]
    [InlineData(IdentificationDecisionAction.SplitVideo)]
    [InlineData(IdentificationDecisionAction.RevokeClaim)]
    [InlineData(IdentificationDecisionAction.ReplaceClaim)]
    [InlineData(IdentificationDecisionAction.AssignDirectly)]
    public async Task A_per_video_judgement_is_refused_for_a_group_and_says_why(
        IdentificationDecisionAction action)
    {
        await using var store = await CreateAsync(cases: 2);
        var administrator = await LibraryPipeline.AccountAsync(store, "administrator");
        var plan = await PlanAsync(store);

        var result = await DecideAsync(store, administrator, plan, action);

        Assert.Equal(IdentificationGroupDecisionVerdict.ActionUnavailable, result.Verdict);
        Assert.Equal(0, result.Applied);
        Assert.NotEmpty(result.Summary);

        // And the plan said so before the button, in the same words.
        var refused = plan.Decisions.Single(decision => decision.Action == action);
        Assert.NotNull(refused.Refusal);
        Assert.Equal(refused.Refusal, result.Summary);
    }

    /// <summary>
    /// What answering a group does is said before it is taken: how many Videos change, how many
    /// merge into others, and how many cases are refused and stay open.
    /// </summary>
    [Fact]
    public async Task The_plan_states_the_consequence_for_the_whole_group()
    {
        await using var store = await CreateAsync(cases: 5);

        var plan = await PlanAsync(store);

        Assert.Equal(5, plan.CaseCount);
        Assert.Equal(5, plan.Cases.Count);

        var accept = plan.Decisions.Single(decision =>
            decision.Action == IdentificationDecisionAction.AcceptCandidate);
        Assert.Null(accept.Refusal);
        Assert.Equal(5, accept.VideosChanged);

        // A Site Recognition group merges nothing: only a work identity two Videos share does that.
        Assert.Equal(0, accept.VideosMerged);
        Assert.False(accept.RequiresNote);
        Assert.Contains("5 Videos become Established", accept.Outcome);

        var reject = plan.Decisions.Single(decision =>
            decision.Action == IdentificationDecisionAction.RejectCandidate);
        Assert.Equal(0, reject.VideosChanged);
        Assert.Contains("stays suppressed", reject.Outcome);
    }

    /// <summary>
    /// Every case of a work group proposes the same work, so the library ends with one Video
    /// carrying it and the rest merged into that one. That is the whole consequence, and a count on
    /// a button does not say it.
    /// </summary>
    [Fact]
    public async Task A_work_group_says_how_many_videos_would_merge_into_one()
    {
        await using var store = await CreateAsync(
            cases: 4,
            dimension: IdentificationDimension.WorkIdentification);

        var accept = (await PlanAsync(store)).Decisions.Single(decision =>
            decision.Action == IdentificationDecisionAction.AcceptCandidate);

        Assert.Equal(4, accept.VideosChanged);
        Assert.Equal(3, accept.VideosMerged);
        Assert.True(accept.RequiresNote);
        Assert.Contains("3 Videos merge into the Video that carries the work", accept.Outcome);
        Assert.Contains("private viewing state", accept.Outcome);
    }

    /// <summary>
    /// Four hundred merges with their Personal State reconciliation is not something a person
    /// should watch a browser hang for, so one call settles a bounded batch and says what is left.
    /// </summary>
    [Fact]
    public async Task One_call_settles_a_bounded_batch()
    {
        await using var store = await CreateAsync(cases: IdentificationReviewService.GroupBatchLimit + 3);
        var administrator = await LibraryPipeline.AccountAsync(store, "administrator");
        var plan = await PlanAsync(store);

        var first = await DecideAsync(
            store,
            administrator,
            plan,
            IdentificationDecisionAction.RejectCandidate);
        Assert.Equal(IdentificationReviewService.GroupBatchLimit, first.Applied);

        var remaining = plan with
        {
            Cases = plan.Cases.Skip(IdentificationReviewService.GroupBatchLimit).ToArray(),
        };
        var second = await DecideAsync(
            store,
            administrator,
            remaining,
            IdentificationDecisionAction.RejectCandidate);
        Assert.Equal(3, second.Applied);

        await using var scope = store.Scope();
        Assert.Empty(await scope.ServiceProvider
            .GetRequiredService<ViewerDbContext>()
            .IdentificationCandidates
            .Where(candidate => candidate.Status == IdentificationCandidateStatus.Pending)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<IdentificationGroupPlan> PlanAsync(TestDatabase store)
    {
        await using var scope = store.Scope();
        var review = scope.ServiceProvider.GetRequiredService<IdentificationReviewService>();
        var queue = await review.GetQueueAsync(null, TestContext.Current.CancellationToken);
        var group = Assert.Single(queue.Groups);
        var plan = await review.GetGroupPlanAsync(group.Key, TestContext.Current.CancellationToken);
        Assert.NotNull(plan);
        return plan;
    }

    private static async Task<IdentificationGroupDecisionResult> DecideAsync(
        TestDatabase store,
        Guid administrator,
        IdentificationGroupPlan plan,
        IdentificationDecisionAction action,
        string? note = "Decided as a group.")
    {
        await using var scope = store.Scope();
        return await scope.ServiceProvider
            .GetRequiredService<IdentificationReviewService>()
            .DecideGroupAsync(
                administrator,
                new IdentificationGroupDecisionRequest(
                    Guid.CreateVersion7(),
                    plan.GroupKey,
                    action,
                    plan.Cases,
                    note),
                TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A library whose open cases all ask one question, written directly: what is under test is
    /// what one answer does to all of them.
    /// </summary>
    private static async Task<TestDatabase> CreateAsync(
        int cases,
        IdentificationDimension dimension = IdentificationDimension.SiteRecognition)
    {
        var store = await TestDatabase.CreateAsync();
        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var directory = new LibraryDirectoryRow
        {
            Id = Guid.CreateVersion7(),
            Name = "Fixture Library",
            ContainerPath = Path.Combine(store.LibraryMountRoot.Path, "source"),
            State = LibraryDirectoryState.Active,
            Health = LibraryDirectoryHealth.Healthy,
            ConfigurationGeneration = 1,
            CreatedAt = At(0),
            ActivatedAt = At(0),
        };
        database.LibraryDirectories.Add(directory);

        for (var index = 0; index < cases; index++)
        {
            var video = new VideoRow
            {
                Id = Guid.CreateVersion7(),
                DiscoveryDate = At(index),
                DisplayLabel = $"video-{index:0000}",
            };
            database.Videos.Add(video);
            database.VideoFiles.Add(new VideoFileRow
            {
                Id = Guid.CreateVersion7(),
                VideoId = video.Id,
                LibraryDirectoryId = directory.Id,
                RelativePath = $"video-{index:0000}.mp4",
                Size = 1_000 + index,
                LastWriteTimeUtc = At(index),
                Sha256 = new string('a', 64),
                PublicDeliveryId = Guid.NewGuid(),
                ContainerFormat = "matroska,webm",
                VideoCodec = "vp8",
                AudioCodec = "vorbis",
                DurationMilliseconds = 12_345,
                Availability = VideoFileAvailability.Available,
                LastObservedScanId = Guid.CreateVersion7(),
                InspectedAt = At(index),
            });
            database.IdentificationCandidates.Add(new IdentificationCandidateRow
            {
                Id = Guid.CreateVersion7(),
                VideoId = video.Id,
                Dimension = dimension,
                Status = IdentificationCandidateStatus.Pending,
                TargetKey = OneSite,
                TargetTitle = dimension == IdentificationDimension.SiteRecognition
                    ? "One Site"
                    : "One Work",
                EvidenceClass = IdentificationEvidenceClass.Suggestive,
                Reason = IdentificationReviewReason.SuggestiveEvidence,
                Source = IdentificationSource.LocalInference,
                EvidenceKey = "LocalSiteName:one site",
                CreatedAt = At(index),
            });
        }

        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        return store;
    }

    private static DateTime At(int index) =>
        DateTime.SpecifyKind(new DateTime(2026, 9, 1), DateTimeKind.Utc).AddMinutes(index);
}
