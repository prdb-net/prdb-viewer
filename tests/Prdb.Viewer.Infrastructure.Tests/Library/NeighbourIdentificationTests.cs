using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

/// <summary>
/// What one file of a perceptual pair being identified is worth to the other one. prdb answered
/// for the 1080p copy and had nothing for the re-encode; the installation can see that the two
/// carry the same picture, and it says so to an Administrator rather than deciding it.
///
/// The pairs here deliberately disagree about their running times, because that is where this rung
/// lives: a pair that agrees clears ADR 0021's narrow path and is associated without review, which
/// is <see cref="WorkAssociationTests"/>. What is left for an Administrator is everything else.
/// </summary>
public sealed class NeighbourIdentificationTests
{
    private const string WorkId = "6f1a2c34-0000-4000-8000-000000000301";

    /// <summary>Two encodes of one work: two bits apart, the same running time.</summary>
    private const string Original = "0f0f0f0f0f0f0f0f";

    private const string Reencode = "0f0f0f0f0f0f0f0c";

    [Fact]
    public async Task An_established_work_becomes_a_candidate_on_its_perceptual_neighbour()
    {
        await using var store = await CreateAsync();

        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var candidate = Assert.Single(await database.IdentificationCandidates
            .Where(row => row.Status == IdentificationCandidateStatus.Pending)
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(IdentificationDimension.WorkIdentification, candidate.Dimension);
        Assert.Equal(WorkId, candidate.TargetKey);

        // It proposes and never claims: a similarity is this installation's own inference about
        // two pictures, not an inspection of the content by the catalogue that holds the work.
        Assert.Equal(IdentificationEvidenceClass.Suggestive, candidate.EvidenceClass);
        Assert.Equal(IdentificationSource.LocalInference, candidate.Source);
        Assert.Equal(IdentificationReviewReason.PerceptualNeighbour, candidate.Reason);
        Assert.Equal(2, candidate.NeighbourDistance);
        Assert.False(candidate.NeighbourDurationsAgree);

        var unidentified = await database.VideoFiles
            .SingleAsync(file => file.RelativePath == "re-encode.mp4", TestContext.Current.CancellationToken);
        Assert.Equal(unidentified.VideoId, candidate.VideoId);

        // The Video stays Unknown until somebody decides. Nothing local establishes a work.
        Assert.Empty(await database.IdentificationClaims
            .Where(claim => claim.VideoId == unidentified.VideoId &&
                            claim.Dimension == IdentificationDimension.WorkIdentification)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The case is not a file name beside a title. It is two files of this library beside each
    /// other, so it has to show the other one.
    /// </summary>
    [Fact]
    public async Task The_case_shows_the_other_file_of_this_library()
    {
        await using var store = await CreateAsync();
        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        var review = scope.ServiceProvider.GetRequiredService<IdentificationReviewService>();
        var item = Assert.Single(await review.QueueAsync(TestContext.Current.CancellationToken));
        var open = await review.GetCaseAsync(item.VideoId, TestContext.Current.CancellationToken);
        var candidate = Assert.Single(open!.OpenCandidates);
        var neighbour = candidate.Neighbour;

        Assert.NotNull(neighbour);
        Assert.Equal("original.mp4", neighbour.RelativePath);
        Assert.Equal(2, neighbour.Distance);
        Assert.False(neighbour.DurationsAgree);
        Assert.Equal(12_345, neighbour.DurationMilliseconds);
        Assert.Equal(VideoQualityBand.FullHd1080, neighbour.Quality);
        Assert.NotNull(neighbour.PreviewUrl);
        Assert.Contains("all but identical", neighbour.Summary);

        // And the queue says which rung it is, so a reviewer can tell it apart before opening it.
        Assert.Equal(IdentificationSource.LocalInference, item.Candidate!.Source);
        Assert.Contains("looks like this one", open.Explanation);
    }

    /// <summary>
    /// Two files of different lengths sample different moments, so a resemblance between them is
    /// as likely to be material the hash cannot describe as it is to be the same work. The case
    /// says so; it does not quietly refuse to raise the proposal.
    /// </summary>
    [Fact]
    public async Task A_pair_whose_running_times_disagree_is_still_proposed_and_says_why_it_is_weaker()
    {
        await using var store = await CreateAsync();
        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var candidate = Assert.Single(await database.IdentificationCandidates
            .Where(row => row.Status == IdentificationCandidateStatus.Pending)
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.False(candidate.NeighbourDurationsAgree);

        var review = scope.ServiceProvider.GetRequiredService<IdentificationReviewService>();
        var open = await review.GetCaseAsync(candidate.VideoId, TestContext.Current.CancellationToken);
        Assert.Contains(
            "describe different moments",
            Assert.Single(open!.OpenCandidates).Neighbour!.Summary);
    }

    /// <summary>
    /// Accepting is an Administrative Override, and because another Video already carries the work
    /// the two Videos merge — through the path a merge already takes, rather than a second one.
    /// </summary>
    [Fact]
    public async Task Accepting_establishes_an_override_and_merges_the_two_videos()
    {
        await using var store = await CreateAsync();
        await LibraryPipeline.DrainAsync(store);
        var administrator = await LibraryPipeline.AccountAsync(store, "administrator");

        await using var scope = store.Scope();
        var review = scope.ServiceProvider.GetRequiredService<IdentificationReviewService>();
        var item = Assert.Single(await review.QueueAsync(TestContext.Current.CancellationToken));
        var result = await review.DecideAsync(
            administrator,
            item.VideoId,
            new IdentificationDecisionRequest(
                IdentificationDecisionAction.AcceptCandidate,
                IdentificationDimension.WorkIdentification,
                item.CaseVersion,
                Confirm: true,
                CandidateId: item.Candidate!.Id,
                Note: "Same work, two encodes."),
            TestContext.Current.CancellationToken);

        Assert.Equal(IdentificationDecisionVerdict.Applied, result.Verdict);
        Assert.True(result.Consequence!.MergesAnotherVideo);

        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var surviving = await database.Videos
            .Where(video => video.SurvivingVideoId == null)
            .ToListAsync(TestContext.Current.CancellationToken);
        var video = Assert.Single(surviving);
        Assert.Equal(
            2,
            await database.VideoFiles.CountAsync(
                file => file.VideoId == video.Id,
                TestContext.Current.CancellationToken));

        var claim = Assert.Single(await database.IdentificationClaims
            .Where(row => row.VideoId == video.Id &&
                          row.Dimension == IdentificationDimension.WorkIdentification &&
                          row.Status == IdentificationClaimStatus.Current)
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.True(claim.IsAdministrativeOverride);
        Assert.Equal(WorkId, claim.TargetKey);
    }

    /// <summary>
    /// Every reading of a similarity is Suggestive, so a rejection that only held against a
    /// stronger class would hold for ever. It is overturned by the measurement instead: the two
    /// files moved closer together.
    /// </summary>
    [Fact]
    public async Task A_rejected_proposal_returns_only_when_the_two_files_move_closer()
    {
        var hashes = new Dictionary<string, string?>
        {
            ["original.mp4"] = Original,
            ["re-encode.mp4"] = "0f0f0f0f0f0f0f00",
        };
        await using var store = await CreateAsync(hashes: hashes);
        await LibraryPipeline.DrainAsync(store);
        var administrator = await LibraryPipeline.AccountAsync(store, "administrator");
        await RejectAsync(store, administrator);

        // The same evidence again changes nothing.
        await ReofferNeighboursAsync(store);
        Assert.Empty(await PendingAsync(store));

        // A reading that is further apart is weaker evidence, and stays rejected.
        hashes["re-encode.mp4"] = "0f0f0f0f0f0f0fc0";
        await RehashAsync(store, "re-encode.mp4");
        Assert.Empty(await PendingAsync(store));

        // A closer one is what the rule calls materially stronger.
        hashes["re-encode.mp4"] = Reencode;
        await RehashAsync(store, "re-encode.mp4");
        var returned = Assert.Single(await PendingAsync(store));
        Assert.Equal(2, returned.NeighbourDistance);
    }

    private static async Task RejectAsync(TestDatabase store, Guid administrator)
    {
        await using var scope = store.Scope();
        var review = scope.ServiceProvider.GetRequiredService<IdentificationReviewService>();
        var item = Assert.Single(await review.QueueAsync(TestContext.Current.CancellationToken));
        var result = await review.DecideAsync(
            administrator,
            item.VideoId,
            new IdentificationDecisionRequest(
                IdentificationDecisionAction.RejectCandidate,
                IdentificationDimension.WorkIdentification,
                item.CaseVersion,
                Confirm: true,
                CandidateId: item.Candidate!.Id),
            TestContext.Current.CancellationToken);
        Assert.Equal(IdentificationDecisionVerdict.Applied, result.Verdict);
    }

    private static async Task<IReadOnlyList<IdentificationCandidateRow>> PendingAsync(
        TestDatabase store)
    {
        await using var scope = store.Scope();
        return await scope.ServiceProvider
            .GetRequiredService<ViewerDbContext>()
            .IdentificationCandidates
            .Where(row => row.Status == IdentificationCandidateStatus.Pending)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Offers the neighbourhoods again without anything having changed.</summary>
    private static async Task ReofferNeighboursAsync(TestDatabase store)
    {
        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var files = await database.VideoFiles
            .Select(file => file.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
        await scope.ServiceProvider
            .GetRequiredService<LocalSimilarityService>()
            .OfferToNeighboursAsync(files, TestContext.Current.CancellationToken);
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

    /// <summary>
    /// One library of two files: prdb knows the first and has never heard of the second.
    /// </summary>
    private static async Task<TestDatabase> CreateAsync(
        long reencodeDuration = 11_000,
        Dictionary<string, string?>? hashes = null)
    {
        var perceptual = hashes ?? new Dictionary<string, string?>
        {
            ["original.mp4"] = Original,
            ["re-encode.mp4"] = Reencode,
        };
        var store = await TestDatabase.CreateAsync(
            mediaProbe: new FixtureProbe(duration: path =>
                Path.GetFileName(path) == "re-encode.mp4" ? reencodeDuration : 12_345),
            hasher: new FixtureHasher(path => new VideoFileHashes(
                FixtureHasher.OsHashOf(path),
                perceptual[Path.GetFileName(path)],
                null)),
            previewGenerator: new FixturePreviewGenerator(),
            identificationClient: new FixtureIdentificationClient()
                .Conclusive("original.mp4", WorkId, "The Established Work")
                .Unmatched("re-encode.mp4"));
        var source = Path.Combine(store.LibraryMountRoot.Path, "source");
        Directory.CreateDirectory(source);

        foreach (var (name, index) in perceptual.Keys.Select((name, index) => (name, index)))
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
