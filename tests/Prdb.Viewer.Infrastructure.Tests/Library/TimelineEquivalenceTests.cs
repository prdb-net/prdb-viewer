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
/// A resume position following the User to another encode of the same Video.
///
/// Only where the two timelines are established as equivalent. Without that the position belongs to
/// the file it was observed on, and the alternative — a proportional guess — would land in the
/// wrong scene, which is worse than the beginning.
/// </summary>
public sealed class TimelineEquivalenceTests
{
    private const string Original = "0f0f0f0f0f0f0f0f";

    private const string Reencode = "0f0f0f0f0f0f0f0c";

    /// <summary>A different picture entirely, which no longer looks like what was hashed before.</summary>
    private const string Unrelated = "f0f0f0f0f0f0f0f0";

    [Fact]
    public async Task A_position_follows_the_user_to_an_equivalent_encode()
    {
        await using var store = await CreateAsync();
        var viewer = await LibraryPipeline.AccountAsync(store);

        await using var scope = store.Scope();
        var (video, watched, other) = await FilesAsync(store);
        await ProgressAsync(store, viewer, video, watched, 4_000);

        var attempt = await scope.ServiceProvider
            .GetRequiredService<PersonalStateService>()
            .StartPlaybackAttemptAsync(viewer, video, other, TestContext.Current.CancellationToken);

        Assert.Equal(PlaybackAttemptVerdict.Started, attempt.Verdict);
        Assert.Equal(4_000, attempt.ResumePositionMilliseconds);
    }

    /// <summary>
    /// Two encodes that were merged by an Administrator rather than by the rule are still only
    /// equivalent where the measurement says so. Association with the same Video is not the same
    /// fact as carrying the same timeline.
    /// </summary>
    [Fact]
    public async Task Belonging_to_one_video_is_not_by_itself_an_equivalent_timeline()
    {
        await using var store = await CreateAsync(secondDuration: 11_000, associate: true);
        var viewer = await LibraryPipeline.AccountAsync(store);

        await using var scope = store.Scope();
        var (video, watched, other) = await FilesAsync(store);
        await ProgressAsync(store, viewer, video, watched, 4_000);

        var attempt = await scope.ServiceProvider
            .GetRequiredService<PersonalStateService>()
            .StartPlaybackAttemptAsync(viewer, video, other, TestContext.Current.CancellationToken);

        Assert.Equal(PlaybackAttemptVerdict.Started, attempt.Verdict);
        Assert.Null(attempt.ResumePositionMilliseconds);
    }

    /// <summary>
    /// The plan a client plays from says which of a Video's occurrences carry each other's
    /// timeline, so a fallback inside one Playback Attempt knows whether to keep the position.
    /// </summary>
    [Fact]
    public async Task The_playback_plan_says_which_occurrences_carry_each_others_timeline()
    {
        await using var store = await CreateAsync();
        var viewer = await LibraryPipeline.AccountAsync(store);

        await using var scope = store.Scope();
        var (videoId, first, second) = await FilesAsync(store);
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var video = await database.Videos
            .Include(row => row.VideoFiles)
            .SingleAsync(row => row.Id == videoId, TestContext.Current.CancellationToken);
        var plan = await scope.ServiceProvider
            .GetRequiredService<PlaybackPlanner>()
            .PlanAsync(
                viewer,
                LibraryPipeline.ClientContext,
                video,
                TestContext.Current.CancellationToken);

        Assert.All(plan.Variants, variant => Assert.Single(variant.TimelineEquivalentVideoFileIds));
        Assert.Equal(
            [second],
            plan.Variants.Single(variant => variant.VideoFileId == first)
                .TimelineEquivalentVideoFileIds);
    }

    /// <summary>
    /// A file hashed again to a different value loses its equivalence with the neighbourhood its
    /// old value produced, and a position already transferred is not retroactively rewritten: it
    /// was confirmed by playback where it stands.
    /// </summary>
    [Fact]
    public async Task Rehashing_a_file_takes_its_equivalence_away_without_rewriting_history()
    {
        var hashes = new Dictionary<string, string>
        {
            ["original.mp4"] = Original,
            ["re-encode.mp4"] = Reencode,
        };
        await using var store = await CreateAsync(hashes: hashes);
        var viewer = await LibraryPipeline.AccountAsync(store);
        var (video, watched, other) = await FilesAsync(store);
        await ProgressAsync(store, viewer, video, watched, 4_000);

        hashes["re-encode.mp4"] = Unrelated;
        await RehashAsync(store, "re-encode.mp4");

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();

        // The position stands where playback confirmed it, on the file it was confirmed on.
        var state = await database.PersonalVideoStates.SingleAsync(
            row => row.AccountId == viewer,
            TestContext.Current.CancellationToken);
        Assert.Equal(4_000, state.PlaybackProgressMilliseconds);
        Assert.Equal(watched, state.ProgressVideoFileId);

        // But it no longer follows to the file whose content has changed.
        var attempt = await scope.ServiceProvider
            .GetRequiredService<PersonalStateService>()
            .StartPlaybackAttemptAsync(viewer, video, other, TestContext.Current.CancellationToken);
        Assert.Null(attempt.ResumePositionMilliseconds);
    }

    /// <summary>The Video, the file a position was observed on, and the other occurrence.</summary>
    private static async Task<(Guid Video, Guid Watched, Guid Other)> FilesAsync(
        TestDatabase store)
    {
        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var files = await database.VideoFiles
            .OrderBy(file => file.RelativePath)
            .Select(file => new { file.Id, file.VideoId })
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(files[0].VideoId, files[1].VideoId);

        return (files[0].VideoId, files[0].Id, files[1].Id);
    }

    private static async Task ProgressAsync(
        TestDatabase store,
        Guid accountId,
        Guid videoId,
        Guid videoFileId,
        long positionMilliseconds)
    {
        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        database.PersonalVideoStates.Add(new PersonalVideoStateRow
        {
            AccountId = accountId,
            VideoId = videoId,
            PlaybackProgressMilliseconds = positionMilliseconds,
            ProgressVideoFileId = videoFileId,
            PlayState = PersonalPlayState.InProgress,
            PlayStateChangedAt = DateTime.UtcNow,
            LastQualifiedActivityAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

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
        long secondDuration = 12_345,
        bool associate = false,
        Dictionary<string, string>? hashes = null)
    {
        var values = hashes ?? new Dictionary<string, string>
        {
            ["original.mp4"] = Original,
            ["re-encode.mp4"] = Reencode,
        };
        var store = await TestDatabase.CreateAsync(
            mediaProbe: new FixtureProbe(duration: path =>
                Path.GetFileName(path) == "re-encode.mp4" ? secondDuration : 12_345),
            hasher: new FixtureHasher(path => new VideoFileHashes(
                FixtureHasher.OsHashOf(path),
                values[Path.GetFileName(path)],
                null)),
            previewGenerator: new FixturePreviewGenerator(),
            identificationClient: new FixtureIdentificationClient()
                .Unmatched("original.mp4")
                .Unmatched("re-encode.mp4"));
        var source = Path.Combine(store.LibraryMountRoot.Path, "source");
        Directory.CreateDirectory(source);

        foreach (var (name, index) in values.Keys.Select((name, index) => (name, index)))
        {
            await File.WriteAllBytesAsync(
                Path.Combine(source, name),
                [1, 2, 3, (byte)index],
                TestContext.Current.CancellationToken);
        }

        await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");
        await LibraryPipeline.DrainAsync(store);

        if (associate)
        {
            await AssociateAsync(store);
        }

        return store;
    }

    /// <summary>
    /// An Administrator answering the proposal the rule would not answer itself, so that two files
    /// belong to one Video without their timelines being equivalent.
    /// </summary>
    private static async Task AssociateAsync(TestDatabase store)
    {
        var administrator = await LibraryPipeline.AccountAsync(store, "administrator");
        await using var scope = store.Scope();
        var review = scope.ServiceProvider.GetRequiredService<IdentificationReviewService>();
        var item = Assert.Single(await review.GetQueueAsync(TestContext.Current.CancellationToken));
        var result = await review.DecideAsync(
            administrator,
            item.VideoId,
            new IdentificationDecisionRequest(
                IdentificationDecisionAction.AssociateVideos,
                IdentificationDimension.WorkIdentification,
                item.CaseVersion,
                Confirm: true,
                Note: "One work, two lengths.",
                AssociationId: item.Association!.Id),
            TestContext.Current.CancellationToken);
        Assert.Equal(IdentificationDecisionVerdict.Applied, result.Verdict);
    }
}
