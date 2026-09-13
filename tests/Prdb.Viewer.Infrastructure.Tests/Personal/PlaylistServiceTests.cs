using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Prdb.Viewer.Core.Access;
using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Core.Personal;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;
using Prdb.Viewer.Infrastructure.Personal;
using Prdb.Viewer.Infrastructure.Tests.Library;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Personal;

public sealed class PlaylistServiceTests
{
    [Fact]
    public async Task A_playlist_keeps_the_order_it_was_given_and_admits_each_video_once()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));
        await using var store = await TestDatabase.CreateAsync(timeProvider: time);
        var seeded = await SeedAsync(store);
        await using var scope = store.Scope();
        var service = scope.ServiceProvider.GetRequiredService<PlaylistService>();

        var created = await service.CreateAsync(
            seeded.FirstAccountId,
            "  Sunday evening  ",
            TestContext.Current.CancellationToken);
        Assert.Equal(PlaylistVerdict.Updated, created.Verdict);
        // A name is what was typed with its surrounding space taken off, and nothing else.
        Assert.Equal("Sunday evening", created.Playlist!.Name);
        var playlistId = created.Playlist.Id;

        Assert.Equal(
            PlaylistVerdict.InvalidName,
            (await service.CreateAsync(
                seeded.FirstAccountId,
                "   ",
                TestContext.Current.CancellationToken)).Verdict);

        foreach (var videoId in seeded.VideoIds)
        {
            Assert.Equal(
                PlaylistVerdict.Updated,
                (await service.AddAsync(
                    seeded.FirstAccountId,
                    playlistId,
                    videoId,
                    TestContext.Current.CancellationToken)).Verdict);
        }

        Assert.Equal(seeded.VideoIds, await OrderAsync(scope, playlistId));

        // Adding a Video that is already there changes nothing at all — least of all its place.
        var again = await service.AddAsync(
            seeded.FirstAccountId,
            playlistId,
            seeded.VideoIds[0],
            TestContext.Current.CancellationToken);
        Assert.Equal(3, again.Playlist!.VideoCount);
        Assert.Equal(seeded.VideoIds, await OrderAsync(scope, playlistId));

        // A move names a place in the whole Playlist, counted from zero.
        await service.MoveAsync(
            seeded.FirstAccountId,
            playlistId,
            seeded.VideoIds[2],
            0,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            [seeded.VideoIds[2], seeded.VideoIds[0], seeded.VideoIds[1]],
            await OrderAsync(scope, playlistId));

        // A removal closes the gap, so the next move counts in the same units the reader does.
        await service.RemoveAsync(
            seeded.FirstAccountId,
            playlistId,
            seeded.VideoIds[0],
            TestContext.Current.CancellationToken);
        Assert.Equal([seeded.VideoIds[2], seeded.VideoIds[1]], await OrderAsync(scope, playlistId));
        Assert.Equal(
            [0, 1],
            await scope.ServiceProvider.GetRequiredService<ViewerDbContext>().PlaylistEntries
                .Where(entry => entry.PlaylistId == playlistId)
                .OrderBy(entry => entry.Position)
                .Select(entry => entry.Position)
                .ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_playlist_narrows_the_library_and_survives_what_the_library_would_hide()
    {
        await using var store = await TestDatabase.CreateAsync();
        var seeded = await SeedAsync(store);
        await using var scope = store.Scope();
        var service = scope.ServiceProvider.GetRequiredService<PlaylistService>();
        var discovery = scope.ServiceProvider.GetRequiredService<LibraryDiscovery>();

        var playlistId = (await service.CreateAsync(
            seeded.FirstAccountId,
            "Kept",
            TestContext.Current.CancellationToken)).Playlist!.Id;
        await service.AddAsync(
            seeded.FirstAccountId,
            playlistId,
            seeded.VideoIds[1],
            TestContext.Current.CancellationToken);
        await service.AddAsync(
            seeded.FirstAccountId,
            playlistId,
            seeded.VideoIds[0],
            TestContext.Current.CancellationToken);

        var page = await PageAsync(discovery, seeded.FirstAccountId, playlistId);
        Assert.Equal(
            [seeded.VideoIds[1], seeded.VideoIds[0]],
            page.Videos.Select(video => video.Id));
        // Nothing is kept out of a personal list, so a Playlist has nothing to count as hidden.
        Assert.Equal(0, page.HiddenNotReadyForDirectPlay);
        Assert.Equal(0, page.HiddenUnavailable);

        // A Video the current client could not play is still what the User put there.
        await MakeUnplayableAsync(scope, seeded.VideoIds[1]);
        Assert.Equal(
            2,
            (await PageAsync(discovery, seeded.FirstAccountId, playlistId)).Videos.Count);

        // Paging is the Library's, over the Playlist's own order.
        var first = await PageAsync(discovery, seeded.FirstAccountId, playlistId, take: 1);
        Assert.Equal([seeded.VideoIds[1]], first.Videos.Select(video => video.Id));
        Assert.True(first.HasMore);
        Assert.Equal(2, first.TotalMatches);

        // And so is the search, inside the Playlist rather than beside it.
        var searched = await discovery.GetAsync(
            seeded.FirstAccountId,
            LibraryPipeline.ClientContext,
            new LibraryDiscoveryRequest
            {
                Playlist = playlistId,
                Sort = LibrarySortOrder.PlaylistOrder,
                Query = "second",
            },
            TestContext.Current.CancellationToken);
        Assert.Equal([seeded.VideoIds[1]], searched.Videos.Select(video => video.Id));
    }

    [Fact]
    public async Task A_playlist_belongs_to_one_account_and_says_nothing_to_another()
    {
        await using var store = await TestDatabase.CreateAsync();
        var seeded = await SeedAsync(store);
        await using var scope = store.Scope();
        var service = scope.ServiceProvider.GetRequiredService<PlaylistService>();
        var discovery = scope.ServiceProvider.GetRequiredService<LibraryDiscovery>();

        var playlistId = (await service.CreateAsync(
            seeded.FirstAccountId,
            "Mine",
            TestContext.Current.CancellationToken)).Playlist!.Id;
        await service.AddAsync(
            seeded.FirstAccountId,
            playlistId,
            seeded.VideoIds[0],
            TestContext.Current.CancellationToken);

        Assert.Empty(await service.ListAsync(
            seeded.SecondAccountId,
            cancellationToken: TestContext.Current.CancellationToken));

        // Every mutation answers the other Account exactly as one that does not exist would.
        Assert.Equal(
            PlaylistVerdict.NotFound,
            (await service.RenameAsync(
                seeded.SecondAccountId,
                playlistId,
                "Theirs",
                TestContext.Current.CancellationToken)).Verdict);
        Assert.Equal(
            PlaylistVerdict.NotFound,
            (await service.AddAsync(
                seeded.SecondAccountId,
                playlistId,
                seeded.VideoIds[1],
                TestContext.Current.CancellationToken)).Verdict);
        Assert.Equal(
            PlaylistVerdict.NotFound,
            (await service.RemoveAsync(
                seeded.SecondAccountId,
                playlistId,
                seeded.VideoIds[0],
                TestContext.Current.CancellationToken)).Verdict);
        Assert.Equal(
            PlaylistVerdict.NotFound,
            (await service.MoveAsync(
                seeded.SecondAccountId,
                playlistId,
                seeded.VideoIds[0],
                0,
                TestContext.Current.CancellationToken)).Verdict);
        Assert.Equal(
            PlaylistVerdict.NotFound,
            await service.DeleteAsync(
                seeded.SecondAccountId,
                playlistId,
                TestContext.Current.CancellationToken));

        // Naming it in a Library request narrows to nothing rather than to somebody else's filing.
        Assert.Empty((await PageAsync(discovery, seeded.SecondAccountId, playlistId)).Videos);
        Assert.Single((await PageAsync(discovery, seeded.FirstAccountId, playlistId)).Videos);

        // What the recommender reads is one Account's own membership and nobody else's.
        Assert.True(PlaylistRule.IsPositive(
            (await service.MembershipsAsync(
                seeded.FirstAccountId,
                seeded.VideoIds,
                TestContext.Current.CancellationToken))
                .GetValueOrDefault(seeded.VideoIds[0])));
        Assert.Empty(await service.MembershipsAsync(
            seeded.SecondAccountId,
            seeded.VideoIds,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Membership_is_one_input_however_many_playlists_hold_a_video_and_leaves_with_the_last_one()
    {
        await using var store = await TestDatabase.CreateAsync();
        var seeded = await SeedAsync(store);
        await using var scope = store.Scope();
        var service = scope.ServiceProvider.GetRequiredService<PlaylistService>();

        var first = (await service.CreateAsync(
            seeded.FirstAccountId,
            "One",
            TestContext.Current.CancellationToken)).Playlist!.Id;
        var second = (await service.CreateAsync(
            seeded.FirstAccountId,
            "Two",
            TestContext.Current.CancellationToken)).Playlist!.Id;
        await service.AddAsync(
            seeded.FirstAccountId,
            first,
            seeded.VideoIds[0],
            TestContext.Current.CancellationToken);
        await service.AddAsync(
            seeded.FirstAccountId,
            second,
            seeded.VideoIds[0],
            TestContext.Current.CancellationToken);

        var memberships = await service.MembershipsAsync(
            seeded.FirstAccountId,
            seeded.VideoIds,
            TestContext.Current.CancellationToken);
        Assert.Equal(2, memberships[seeded.VideoIds[0]]);
        // Two memberships are still one positive input: filing a Video twice is organisation
        // rather than a second endorsement.
        Assert.True(PlaylistRule.IsPositive(memberships[seeded.VideoIds[0]]));
        Assert.False(PlaylistRule.IsPositive(memberships.GetValueOrDefault(seeded.VideoIds[1])));

        // Deleting a Playlist deletes the organisation. The Video and the other Playlist stay.
        await service.DeleteAsync(
            seeded.FirstAccountId,
            second,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            1,
            (await service.MembershipsAsync(
                seeded.FirstAccountId,
                seeded.VideoIds,
                TestContext.Current.CancellationToken))[seeded.VideoIds[0]]);
        Assert.Equal(
            3,
            await scope.ServiceProvider.GetRequiredService<ViewerDbContext>().Videos
                .CountAsync(TestContext.Current.CancellationToken));

        // The contribution leaves with the last membership rather than lingering.
        await service.RemoveAsync(
            seeded.FirstAccountId,
            first,
            seeded.VideoIds[0],
            TestContext.Current.CancellationToken);
        Assert.False(PlaylistRule.IsPositive(
            (await service.MembershipsAsync(
                seeded.FirstAccountId,
                seeded.VideoIds,
                TestContext.Current.CancellationToken))
                .GetValueOrDefault(seeded.VideoIds[0])));
    }

    private static Task<LibraryPage> PageAsync(
        LibraryDiscovery discovery,
        Guid accountId,
        Guid playlistId,
        int take = 60) =>
        discovery.GetAsync(
            accountId,
            LibraryPipeline.ClientContext,
            new LibraryDiscoveryRequest
            {
                Playlist = playlistId,
                Sort = LibrarySortOrder.PlaylistOrder,
                Take = take,
            },
            TestContext.Current.CancellationToken);

    private static async Task<IReadOnlyList<Guid>> OrderAsync(AsyncServiceScope scope, Guid playlistId) =>
        await scope.ServiceProvider.GetRequiredService<ViewerDbContext>().PlaylistEntries
            .AsNoTracking()
            .Where(entry => entry.PlaylistId == playlistId)
            .OrderBy(entry => entry.Position)
            .Select(entry => entry.VideoId)
            .ToListAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// Makes every occurrence of a Video something no client can play directly. Ordinary Discovery
    /// then keeps it out, which is what makes it worth asking whether a Playlist still shows it.
    /// </summary>
    private static async Task MakeUnplayableAsync(AsyncServiceScope scope, Guid videoId)
    {
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        await database.VideoFiles
            .Where(file => file.VideoId == videoId)
            .ExecuteUpdateAsync(
                updates => updates.SetProperty(
                    file => file.DirectPlayClassification,
                    DirectPlayClassification.Unsupported),
                TestContext.Current.CancellationToken);
    }

    private static async Task<SeededIds> SeedAsync(TestDatabase store)
    {
        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var now = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);
        var firstAccountId = Guid.CreateVersion7();
        var secondAccountId = Guid.CreateVersion7();
        var directoryId = Guid.CreateVersion7();
        database.Accounts.AddRange(
            Account(firstAccountId, "first", now),
            Account(secondAccountId, "second", now));
        database.LibraryDirectories.Add(new LibraryDirectoryRow
        {
            Id = directoryId,
            Name = "Main Library",
            ContainerPath = store.LibraryMountRoot.Path,
            State = LibraryDirectoryState.Active,
            Health = LibraryDirectoryHealth.Healthy,
            ConfigurationGeneration = 1,
            CreatedAt = now,
            ActivatedAt = now,
        });

        var videoIds = new List<Guid>();

        foreach (var (index, label) in new[] { "first", "second", "third" }.Index())
        {
            var videoId = Guid.CreateVersion7();
            videoIds.Add(videoId);
            database.Videos.Add(new VideoRow
            {
                Id = videoId,
                DiscoveryDate = now.AddMinutes(index),
                DisplayLabel = label,
                SearchText = label,
            });
            database.VideoFiles.Add(new VideoFileRow
            {
                Id = Guid.CreateVersion7(),
                VideoId = videoId,
                LibraryDirectoryId = directoryId,
                RelativePath = $"{label}.mp4",
                Size = 100,
                LastWriteTimeUtc = now,
                Sha256 = new string((char)('a' + index), 64),
                PublicDeliveryId = Guid.NewGuid(),
                ContainerFormat = "mp4",
                VideoCodec = "h264",
                AudioCodec = "aac",
                DurationMilliseconds = 100_000,
                Width = 640,
                Height = 360,
                Availability = VideoFileAvailability.Available,
                DirectPlayClassification = DirectPlayClassification.BaselineCandidate,
                LastObservedScanId = Guid.CreateVersion7(),
                InspectedAt = now,
            });
        }

        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new SeededIds(firstAccountId, secondAccountId, videoIds);
    }

    private static AccountRow Account(Guid id, string username, DateTime now) => new()
    {
        Id = id,
        Username = username,
        NormalizedUsername = username.ToUpperInvariant(),
        PasswordHash = "not-used",
        Authority = AccountAuthority.User,
        State = AccountState.Approved,
        RegisteredAt = now,
        ApprovedAt = now,
    };

    private sealed record SeededIds(
        Guid FirstAccountId,
        Guid SecondAccountId,
        IReadOnlyList<Guid> VideoIds);
}
