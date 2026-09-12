using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

/// <summary>
/// The shape a large library is reviewed from. The MVP queue decides one case at a time, which is
/// the right shape for tens of cases and the wrong one for a library that produced thousands.
/// </summary>
public sealed class ReviewQueueShapeTests
{
    private const string OneSite = "5b1a2c34-0000-4000-8000-0000000005aa";

    private const string AnotherSite = "5b1a2c34-0000-4000-8000-0000000005bb";

    [Fact]
    public async Task Cases_that_share_one_question_are_one_group()
    {
        await using var store = await CreateAsync(sameSite: 5, otherSite: 2);

        var queue = await QueueAsync(store);

        Assert.Equal(7, queue.CaseCount);
        Assert.Equal(2, queue.GroupCount);

        var largest = queue.Groups[0];
        Assert.Equal(5, largest.CaseCount);
        Assert.Equal(IdentificationDimension.SiteRecognition, largest.Dimension);
        Assert.Equal(OneSite, largest.TargetKey);

        // A count on its own is not a description of a group, so it says what the cases share and
        // where they differ rather than leaving a reviewer to guess at four hundred of them.
        Assert.Contains("5 Videos are proposed as", largest.InCommon);
        Assert.Contains("which Video is being asked about", largest.Differ);

        // And a group is something somebody can look inside before answering it.
        Assert.NotEmpty(largest.Cases);
        Assert.All(largest.Cases, item => Assert.Equal("One Site", item.Candidate!.TargetTitle));
    }

    /// <summary>
    /// A group of four hundred is still a group somebody has to be able to look inside, so the
    /// cases it carries are a sample and the queue says there are more.
    /// </summary>
    [Fact]
    public async Task A_large_group_shows_a_sample_and_says_that_it_is_one()
    {
        await using var store = await CreateAsync(sameSite: 12, otherSite: 0);

        var group = Assert.Single((await QueueAsync(store)).Groups);

        Assert.Equal(12, group.CaseCount);
        Assert.Equal(6, group.Cases.Count);
        Assert.True(group.HasMoreCases);
    }

    /// <summary>
    /// Each proposed association is its own question: two particular files, and whether they carry
    /// the same content. Grouping them together would make one large group nobody could answer.
    /// </summary>
    [Fact]
    public async Task Every_proposed_association_is_a_group_of_its_own()
    {
        await using var store = await CreateAsync(sameSite: 0, otherSite: 0, associations: 2);

        var queue = await QueueAsync(store);

        Assert.Equal(2, queue.GroupCount);
        Assert.All(queue.Groups, group => Assert.Equal(1, group.CaseCount));
        Assert.All(queue.Groups, group => Assert.Contains("its own question", group.InCommon));
    }

    [Fact]
    public async Task The_queue_is_paged_and_counted()
    {
        await using var store = await CreateAsync(sameSite: 3, otherSite: 2);

        var first = await QueueAsync(store, new IdentificationQueueRequest { Take = 1 });
        var second = await QueueAsync(store, new IdentificationQueueRequest { Skip = 1, Take = 1 });

        Assert.Equal(2, first.GroupCount);
        Assert.Equal(5, first.CaseCount);
        Assert.Equal(3, Assert.Single(first.Groups).CaseCount);
        Assert.Equal(2, Assert.Single(second.Groups).CaseCount);
    }

    /// <summary>
    /// So a reviewer can decide what they are in the mood to do instead of taking whatever is on
    /// top.
    /// </summary>
    [Fact]
    public async Task The_queue_is_filterable_and_says_what_each_filter_would_leave()
    {
        await using var store = await CreateAsync(sameSite: 3, otherSite: 0, associations: 1);

        var all = await QueueAsync(store);
        var site = await QueueAsync(
            store,
            new IdentificationQueueRequest { Dimension = IdentificationDimension.SiteRecognition });

        Assert.Equal(4, all.CaseCount);
        Assert.Equal(3, site.CaseCount);

        // Each row is counted with every filter applied except its own, so a reviewer who has
        // chosen one dimension can still see what the other would give them — and can therefore
        // change their mind.
        Assert.Equal(
            2,
            site.Facets.Dimensions.Count);
        Assert.Equal(
            1,
            site.Facets.Dimensions
                .Single(facet => facet.Value == nameof(IdentificationDimension.WorkIdentification))
                .CaseCount);
        Assert.Equal(
            3,
            all.Facets.Dimensions
                .Single(facet => facet.Value == nameof(IdentificationDimension.SiteRecognition))
                .CaseCount);
        Assert.Equal(
            1,
            all.Facets.Reasons
                .Single(facet => facet.Value == nameof(IdentificationReviewReason.PerceptualNeighbour))
                .CaseCount);
    }

    private static async Task<IdentificationQueue> QueueAsync(
        TestDatabase store,
        IdentificationQueueRequest? request = null)
    {
        await using var scope = store.Scope();
        return await scope.ServiceProvider
            .GetRequiredService<IdentificationReviewService>()
            .GetQueueAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A library whose open cases are written directly, because what is under test is the shape the
    /// backlog is read in rather than the lanes that fill it.
    /// </summary>
    private static async Task<TestDatabase> CreateAsync(
        int sameSite,
        int otherSite,
        int associations = 0)
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

        for (var index = 0; index < sameSite + otherSite; index++)
        {
            var video = NewVideo(database, directory, index);
            database.IdentificationCandidates.Add(Candidate(
                video,
                index < sameSite ? OneSite : AnotherSite,
                index < sameSite ? "One Site" : "Another Site",
                At(index)));
        }

        for (var index = 0; index < associations; index++)
        {
            var left = NewVideo(database, directory, 1_000 + index);
            var right = NewVideo(database, directory, 2_000 + index);
            database.WorkAssociations.Add(new WorkAssociationRow
            {
                Id = Guid.CreateVersion7(),
                Status = WorkAssociationStatus.Proposed,
                VideoId = left.Video,
                OtherVideoId = right.Video,
                VideoFileId = left.File,
                OtherVideoFileId = right.File,
                Distance = 2,
                DurationsAgree = false,
                DurationMilliseconds = 12_345,
                OtherDurationMilliseconds = 11_000,
                Source = IdentificationSource.LocalInference,
                CreatedAt = At(index),
            });
        }

        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        return store;
    }

    private static (Guid Video, Guid File) NewVideo(
        ViewerDbContext database,
        LibraryDirectoryRow directory,
        int index)
    {
        var video = new VideoRow
        {
            Id = Guid.CreateVersion7(),
            DiscoveryDate = At(index),
            DisplayLabel = $"video-{index:0000}",
        };
        var file = new VideoFileRow
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
        };
        database.Videos.Add(video);
        database.VideoFiles.Add(file);
        return (video.Id, file.Id);
    }

    private static IdentificationCandidateRow Candidate(
        (Guid Video, Guid File) video,
        string targetKey,
        string targetTitle,
        DateTime at) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            VideoId = video.Video,
            Dimension = IdentificationDimension.SiteRecognition,
            Status = IdentificationCandidateStatus.Pending,
            TargetKey = targetKey,
            TargetTitle = targetTitle,
            EvidenceClass = IdentificationEvidenceClass.Suggestive,
            Reason = IdentificationReviewReason.SuggestiveEvidence,
            Source = IdentificationSource.LocalInference,
            EvidenceKey = $"LocalSiteName:{targetTitle}",
            SupportingVideoFileId = video.File,
            CreatedAt = at,
        };

    private static DateTime At(int index) =>
        DateTime.SpecifyKind(new DateTime(2026, 9, 1), DateTimeKind.Utc).AddMinutes(index);
}
