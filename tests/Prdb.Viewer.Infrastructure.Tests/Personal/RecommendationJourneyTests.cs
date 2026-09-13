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

/// <summary>
/// The whole journey over one deliberately awkward collection, shared by two Accounts.
///
/// Every case the rules turn on is in the same library at the same time: a Video watched as six
/// ten-second stretches, one watched straight through, one returned to repeatedly, a liked one
/// nobody has watched for months, a disliked one, one put aside for the day, one nothing here can
/// play, one carrying an older installation's aggregate with no sessions behind it, and one whose
/// history was written across two Video Files. Nothing is mocked: the evidence is produced by
/// reporting playback the way a browser does.
/// </summary>
public sealed class RecommendationJourneyTests
{
    private const string Phone = "phone";
    private const string Television = "television";

    [Fact]
    public async Task The_whole_page_holds_together_over_one_awkward_collection()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));
        await using var store = await TestDatabase.CreateAsync(timeProvider: time);
        var library = await SeedAsync(store, time);
        await using var scope = store.Scope();
        var personal = scope.ServiceProvider.GetRequiredService<PersonalStateService>();
        var playlists = scope.ServiceProvider.GetRequiredService<PlaylistService>();
        var service = scope.ServiceProvider.GetRequiredService<RecommendationService>();
        var mine = library.AccountId;

        // Six ten-second stretches separated by seeks, months ago: a minute of interest, and long
        // unseen by the time the page is drawn.
        await FragmentedMinuteAsync(personal, library, "fragmented", time);
        time.Advance(TimeSpan.FromDays(40));

        // One continuous quarter of an hour, also months ago.
        await ContinuousAsync(personal, library, "continuous", time, TimeSpan.FromMinutes(15));
        time.Advance(TimeSpan.FromDays(40));

        // Returned to three times, most recently today, so it is return interest rather than long
        // unseen.
        for (var visit = 0; visit < 3; visit++)
        {
            await ContinuousAsync(personal, library, "revisited", time, TimeSpan.FromMinutes(3));
            time.Advance(TimeSpan.FromMinutes(31));
        }

        // Liked and watched long ago; disliked after real watching; and one put aside today.
        await ContinuousAsync(personal, library, "liked", time, TimeSpan.FromMinutes(11));
        await personal.SetReactionAsync(
            mine,
            library.Videos["liked"],
            PersonalReaction.Like,
            TestContext.Current.CancellationToken);
        await ContinuousAsync(personal, library, "disliked", time, TimeSpan.FromMinutes(20));
        await personal.SetReactionAsync(
            mine,
            library.Videos["disliked"],
            PersonalReaction.Dislike,
            TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromDays(20));

        // The same Video watched on one of its files and then the other. It is one Video, so it is
        // one history and one card.
        await ContinuousOnFileAsync(
            personal,
            library,
            "two-files",
            library.SecondFile,
            time,
            TimeSpan.FromMinutes(4));
        time.Advance(TimeSpan.FromMinutes(31));
        await ContinuousAsync(personal, library, "two-files", time, TimeSpan.FromMinutes(4));

        var playlistId = (await playlists.CreateAsync(
            mine,
            "Kept",
            TestContext.Current.CancellationToken)).Playlist!.Id;
        await playlists.AddAsync(
            mine,
            playlistId,
            library.Videos["filed"],
            TestContext.Current.CancellationToken);

        await service.DismissAsync(
            mine,
            library.Videos["revisited"],
            TestContext.Current.CancellationToken);

        var page = await service.GetAsync(
            mine,
            Phone,
            seed: 17,
            take: 12,
            cancellationToken: TestContext.Current.CancellationToken);
        var offered = All(page);

        // Nothing appears twice, and one Video with two files is one identity.
        Assert.Equal(offered.Distinct().Count(), offered.Count);
        Assert.Single(offered, id => id == library.Videos["two-files"]);
        Assert.Equal(
            8 * 60_000,
            (await personal.GetSummaryAsync(
                mine,
                library.Videos["two-files"],
                TestContext.Current.CancellationToken)).AccumulatedWatchDurationMilliseconds);

        // A Dislike, a Temporary Dismissal and a Video nothing here can play are all absent, and
        // each for its own reason.
        Assert.DoesNotContain(library.Videos["disliked"], offered);
        Assert.DoesNotContain(library.Videos["revisited"], offered);
        Assert.DoesNotContain(library.Videos["unplayable"], offered);

        // Long unseen holds the two watched months ago and the liked one, oldest first, and not
        // the one watched an hour ago.
        Assert.Equal(
            [library.Videos["fragmented"], library.Videos["continuous"], library.Videos["liked"]],
            Section(page, RecommendationSection.LongUnseen));

        // What is left of return interest is the Video filed in a Playlist and the one watched
        // across two files; both are positive evidence without being long unseen.
        Assert.Equal(
            [library.Videos["two-files"], library.Videos["filed"]],
            Section(page, RecommendationSection.ForYouToWatchAgain));

        // Never watched means never watched: a failed attempt leaves eligibility, an older
        // installation's aggregate removes it, and the Video with no metadata at all is reachable.
        var discovered = Section(page, RecommendationSection.NotYetDiscovered);
        Assert.Contains(library.Videos["attempted"], discovered);
        Assert.Contains(library.Videos["bare"], discovered);
        Assert.DoesNotContain(library.Videos["legacy"], discovered);
        Assert.All(
            page.Sections.Single(section => section.Section == RecommendationSection.NotYetDiscovered).Videos,
            video => Assert.Contains(RecommendationReason.NeverWatched, video.Reasons));

        // The other Account watched none of it and said none of it. Its page is a cold start.
        // Asked for the whole collection, so that "not offered" cannot be a section running out.
        var theirs = await service.GetAsync(
            library.SecondAccountId,
            Phone,
            seed: 17,
            take: RecommendationService.MaximumSectionSize,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(theirs.HasHistory);
        Assert.Empty(Section(theirs, RecommendationSection.ForYouToWatchAgain));
        Assert.Empty(Section(theirs, RecommendationSection.LongUnseen));
        Assert.Contains(library.Videos["disliked"], All(theirs));
        Assert.Contains(library.Videos["revisited"], All(theirs));
        Assert.Empty(await service.ActiveDismissalsAsync(
            library.SecondAccountId,
            TestContext.Current.CancellationToken));

        // Every exclusion survives being asked again, from a different client of the same Account.
        var reloaded = await service.GetAsync(
            mine,
            Television,
            seed: 17,
            take: 12,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.DoesNotContain(library.Videos["disliked"], All(reloaded));
        Assert.DoesNotContain(library.Videos["revisited"], All(reloaded));

        // Clearing the Dislike gives the Video back; the dismissal lifts on its own after a day.
        await personal.SetReactionAsync(
            mine,
            library.Videos["disliked"],
            null,
            TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromHours(25));
        var later = await service.GetAsync(
            mine,
            Phone,
            seed: 17,
            take: 12,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(library.Videos["disliked"], All(later));
        Assert.Contains(library.Videos["revisited"], All(later));
    }

    [Fact]
    public async Task A_favourite_watched_in_this_visit_leads_again_in_the_next_one()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));
        await using var store = await TestDatabase.CreateAsync(timeProvider: time);
        var library = await SeedAsync(store, time);
        await using var scope = store.Scope();
        var personal = scope.ServiceProvider.GetRequiredService<PersonalStateService>();
        var service = scope.ServiceProvider.GetRequiredService<RecommendationService>();
        var mine = library.AccountId;

        // Two Videos with the same history, so that the only thing between them is the visit.
        // The second of them is watched later, and leads on that alone while the scores are equal.
        foreach (var name in new[] { "continuous", "revisited" })
        {
            for (var visit = 0; visit < 2; visit++)
            {
                await ContinuousAsync(personal, library, name, time, TimeSpan.FromMinutes(11));
                time.Advance(TimeSpan.FromMinutes(31));
            }
        }

        var settled = Section(
            await service.GetAsync(mine, Phone, 1, 12, TestContext.Current.CancellationToken),
            RecommendationSection.ForYouToWatchAgain);
        Assert.Equal(
            [library.Videos["revisited"], library.Videos["continuous"]],
            settled);

        // A minute of one of them just now, which is more evidence and still moves it down for the
        // rest of this visit. It is moved, not removed.
        await FragmentedMinuteAsync(personal, library, "revisited", time);
        var during = Section(
            await service.GetAsync(mine, Phone, 1, 12, TestContext.Current.CancellationToken),
            RecommendationSection.ForYouToWatchAgain);
        Assert.Equal(
            [library.Videos["continuous"], library.Videos["revisited"]],
            during);

        // The next visit is a different question, and there is no cooldown to wait out: the
        // favourite leads again on the strength of the evidence it just added.
        time.Advance(TimeSpan.FromMinutes(31));
        var next = Section(
            await service.GetAsync(mine, Phone, 1, 12, TestContext.Current.CancellationToken),
            RecommendationSection.ForYouToWatchAgain);
        Assert.Equal(library.Videos["revisited"], next[0]);
    }

    /// <summary>
    /// A migrated database holds no star ratings, because it holds no column that could. The
    /// migration drops it outright rather than leaving it behind unread, which is the difference
    /// between data that is gone and data that is merely ignored.
    /// </summary>
    [Fact]
    public async Task An_upgraded_database_keeps_no_star_ratings_at_all()
    {
        await using var store = await TestDatabase.CreateAsync();
        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var columns = new List<string>();

        await using var command = database.Database.GetDbConnection().CreateCommand();
        await database.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        command.CommandText = "SELECT name FROM pragma_table_info('personal_video_state')";

        await using (var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                columns.Add(reader.GetString(0));
            }
        }

        Assert.DoesNotContain("PersonalRating", columns);
        Assert.Contains("Reaction", columns);
        // Everything else a Personal Video State held is still held.
        Assert.Contains("PlayCount", columns);
        Assert.Contains("AccumulatedWatchDurationMilliseconds", columns);
        Assert.Contains("FavouriteAddedAt", columns);
        Assert.Contains("WatchLaterAddedAt", columns);
        Assert.Contains("HasViewingCompletion", columns);
    }

    /// <summary>
    /// Nothing in the recommendation path can reach the network, which is a structural fact rather
    /// than an observation about one run: none of the services that compute a page takes an
    /// outbound client of any kind, so there is no code path by which a User's viewing could leave
    /// this installation.
    /// </summary>
    [Fact]
    public void Computing_a_recommendation_has_no_way_to_reach_the_network()
    {
        Type[] path =
        [
            typeof(RecommendationService),
            typeof(ReturnInterestService),
            typeof(ResurfacingService),
            typeof(PlaylistService),
            typeof(PersonalStateService),
            typeof(LibraryDiscovery),
        ];

        foreach (var service in path)
        {
            foreach (var dependency in service
                         .GetConstructors()
                         .SelectMany(constructor => constructor.GetParameters())
                         .Select(parameter => parameter.ParameterType))
            {
                Assert.False(
                    dependency == typeof(HttpClient) ||
                    typeof(HttpMessageHandler).IsAssignableFrom(dependency) ||
                    dependency.Name.StartsWith("IPrdb", StringComparison.Ordinal),
                    $"{service.Name} depends on {dependency.Name}, which can reach the network.");
            }
        }
    }

    private static IReadOnlyList<Guid> All(RecommendationPage page) =>
        page.Sections.SelectMany(section => section.Videos.Select(video => video.Video.Id)).ToArray();

    private static IReadOnlyList<Guid> Section(RecommendationPage page, RecommendationSection section) =>
        page.Sections
            .Single(candidate => candidate.Section == section)
            .Videos
            .Select(video => video.Video.Id)
            .ToArray();

    /// <summary>Six ten-second stretches, each somewhere else in the Video.</summary>
    private static async Task FragmentedMinuteAsync(
        PersonalStateService service,
        Library library,
        string video,
        FakeTimeProvider time)
    {
        var videoId = library.Videos[video];
        var attemptId = await StartAsync(service, library, videoId, library.Files[videoId]);

        for (var stretch = 0; stretch < 6; stretch++)
        {
            time.Advance(TimeSpan.FromSeconds(10));
            await service.ReportPlaybackAsync(
                library.AccountId,
                attemptId,
                Guid.NewGuid(),
                stretch,
                library.Files[videoId],
                600_000 + (stretch * 300_000) + 10_000,
                10_000,
                naturalEndConfirmed: false,
                endSession: false,
                Phone,
                TestContext.Current.CancellationToken);
        }

        await service.EndPlaybackAttemptAsync(
            library.AccountId,
            attemptId,
            PlaybackDeparture.Closed,
            TestContext.Current.CancellationToken);
    }

    private static Task ContinuousAsync(
        PersonalStateService service,
        Library library,
        string video,
        FakeTimeProvider time,
        TimeSpan length) =>
        ContinuousOnFileAsync(service, library, video, null, time, length);

    private static async Task ContinuousOnFileAsync(
        PersonalStateService service,
        Library library,
        string video,
        Guid? videoFileId,
        FakeTimeProvider time,
        TimeSpan length)
    {
        var videoId = library.Videos[video];
        var file = videoFileId ?? library.Files[videoId];
        var attemptId = await StartAsync(service, library, videoId, file);
        var stretches = Math.Max(1, (int)(length.TotalSeconds / 10));

        for (var stretch = 0; stretch < stretches; stretch++)
        {
            time.Advance(TimeSpan.FromSeconds(10));
            await service.ReportPlaybackAsync(
                library.AccountId,
                attemptId,
                Guid.NewGuid(),
                stretch,
                file,
                (stretch + 1) * 10_000,
                10_000,
                naturalEndConfirmed: false,
                endSession: false,
                Phone,
                TestContext.Current.CancellationToken);
        }

        await service.EndPlaybackAttemptAsync(
            library.AccountId,
            attemptId,
            PlaybackDeparture.Closed,
            TestContext.Current.CancellationToken);
    }

    private static async Task<Guid> StartAsync(
        PersonalStateService service,
        Library library,
        Guid videoId,
        Guid videoFileId) =>
        (await service.StartPlaybackAttemptAsync(
            library.AccountId,
            videoId,
            videoFileId,
            TestContext.Current.CancellationToken)).PlaybackAttemptId!.Value;

    private static async Task<Library> SeedAsync(TestDatabase store, FakeTimeProvider time)
    {
        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var now = new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc);
        var accountId = Guid.CreateVersion7();
        var secondAccountId = Guid.CreateVersion7();
        var directoryId = Guid.CreateVersion7();
        database.Accounts.AddRange(
            Account(accountId, "mine", now),
            Account(secondAccountId, "theirs", now));
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

        string[] names =
        [
            "fragmented", "continuous", "revisited", "liked", "disliked",
            "filed", "two-files", "legacy", "attempted", "unplayable", "bare",
            "spare-one", "spare-two", "spare-three",
        ];
        var videos = new Dictionary<string, Guid>();
        var files = new Dictionary<Guid, Guid>();
        var secondFile = Guid.CreateVersion7();

        foreach (var (index, name) in names.Index())
        {
            var videoId = Guid.CreateVersion7();
            var videoFileId = Guid.CreateVersion7();
            videos[name] = videoId;
            files[videoId] = videoFileId;
            var playable = name != "unplayable";
            database.Videos.Add(new VideoRow
            {
                Id = videoId,
                DiscoveryDate = now.AddMinutes(index),
                DisplayLabel = name,
                SearchText = name,
                // "bare" has no Site and no Actors at all, which is the sparse-metadata case only
                // independent discovery can reach.
                EstablishedSite = name == "bare" ? null : "Example Pictures",
                Availability = VideoAvailability.Available,
                BestClassification = playable
                    ? DirectPlayClassification.BaselineCandidate
                    : DirectPlayClassification.Unsupported,
            });
            database.VideoFiles.Add(File(videoFileId, videoId, directoryId, $"{name}.mp4", index, playable));

            if (name != "bare")
            {
                database.VideoActors.Add(new VideoActorRow
                {
                    Id = Guid.CreateVersion7(),
                    VideoId = videoId,
                    Name = "Alex Doe",
                    NormalizedName = "alex doe",
                });
            }
        }

        // A second encode of one Video, so that one history can be written across two files.
        database.VideoFiles.Add(File(
            secondFile,
            videos["two-files"],
            directoryId,
            "two-files-720p.mp4",
            99,
            playable: true));

        // What an older installation left behind: an aggregate and a Play Count, and no sessions.
        database.PersonalVideoStates.Add(new PersonalVideoStateRow
        {
            AccountId = accountId,
            VideoId = videos["legacy"],
            PlayCount = 3,
            AccumulatedWatchDurationMilliseconds = 2_000_000,
            PlayState = PersonalPlayState.Completed,
            HasViewingCompletion = true,
            UpdatedAt = now,
        });

        await database.SaveChangesAsync(TestContext.Current.CancellationToken);

        // An attempt that never confirmed a second of playback. It is a failed attempt rather than
        // watching, so the Video is still one nobody has watched.
        var personal = scope.ServiceProvider.GetRequiredService<PersonalStateService>();
        await personal.StartPlaybackAttemptAsync(
            accountId,
            videos["attempted"],
            files[videos["attempted"]],
            TestContext.Current.CancellationToken);

        return new Library(accountId, secondAccountId, videos, files, secondFile);
    }

    private static VideoFileRow File(
        Guid id,
        Guid videoId,
        Guid directoryId,
        string path,
        int index,
        bool playable) => new()
        {
            Id = id,
            VideoId = videoId,
            LibraryDirectoryId = directoryId,
            RelativePath = path,
            Size = 100,
            LastWriteTimeUtc = new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc),
            Sha256 = index.ToString("x64"),
            PublicDeliveryId = Guid.NewGuid(),
            ContainerFormat = "mp4",
            VideoCodec = "h264",
            AudioCodec = "aac",
            DurationMilliseconds = 7_200_000,
            Width = 640,
            Height = 360,
            Availability = VideoFileAvailability.Available,
            DirectPlayClassification = playable
                ? DirectPlayClassification.BaselineCandidate
                : DirectPlayClassification.Unsupported,
            LastObservedScanId = Guid.CreateVersion7(),
            InspectedAt = new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc),
        };

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

    private sealed record Library(
        Guid AccountId,
        Guid SecondAccountId,
        IReadOnlyDictionary<string, Guid> Videos,
        IReadOnlyDictionary<Guid, Guid> Files,
        Guid SecondFile);
}
