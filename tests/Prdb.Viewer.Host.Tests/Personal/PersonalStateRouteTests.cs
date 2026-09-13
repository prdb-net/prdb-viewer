using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Viewer.Host.Tests.Personal;

public sealed class PersonalStateRouteTests
{
    [Fact]
    public async Task Personal_routes_require_csrf_and_never_cross_account_ownership()
    {
        using var application = new ViewerApplication();
        using var administrator = application.CreateClient();
        using var user = application.CreateClient();
        var video = await AddVideoAsync(application);
        var administratorCsrf = await ClaimAsync(application, administrator);
        var userCsrf = await RegisterApproveAndSignInAsync(administrator, user, administratorCsrf);

        using var anonymous = application.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync(
                "/api/library/videos?shelf=Favourites",
                TestContext.Current.CancellationToken)).StatusCode);

        using var missingCsrf = await administrator.PostAsJsonAsync(
            $"/api/personal/videos/{video.VideoId}/playback-attempts",
            new { videoFileId = video.VideoFileId },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, missingCsrf.StatusCode);

        using var favourite = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/personal/videos/{video.VideoId}/favourite");
        favourite.Headers.Add("X-CSRF-Token", administratorCsrf);
        using var favouriteResponse = await administrator.SendAsync(
            favourite,
            TestContext.Current.CancellationToken);
        favouriteResponse.EnsureSuccessStatusCode();

        // A shelf is the Library narrowed to it, and each Account's shelf is its own.
        var administratorLibrary = await administrator.GetFromJsonAsync<JsonElement>(
            "/api/library/videos?shelf=Favourites",
            TestContext.Current.CancellationToken);
        Assert.Single(administratorLibrary.GetProperty("videos").EnumerateArray());
        var userLibrary = await user.GetFromJsonAsync<JsonElement>(
            "/api/library/videos?shelf=Favourites",
            TestContext.Current.CancellationToken);
        Assert.Empty(userLibrary.GetProperty("videos").EnumerateArray());

        var attempt = await SendJsonAsync(
            administrator,
            HttpMethod.Post,
            $"/api/personal/videos/{video.VideoId}/playback-attempts",
            new { videoFileId = video.VideoFileId },
            administratorCsrf);
        Assert.Equal("Started", attempt.GetProperty("verdict").GetString());
        var playbackAttemptId = attempt.GetProperty("playbackAttemptId").GetGuid();

        var foreignReport = await SendJsonAsync(
            user,
            HttpMethod.Post,
            $"/api/personal/playback-attempts/{playbackAttemptId}/reports",
            Report(video.VideoFileId, 0, 10_000, 10_000),
            userCsrf);
        Assert.Equal("NotFound", foreignReport.GetProperty("verdict").GetString());

        var report = await SendJsonAsync(
            administrator,
            HttpMethod.Post,
            $"/api/personal/playback-attempts/{playbackAttemptId}/reports",
            Report(video.VideoFileId, 0, 10_000, 10_000),
            administratorCsrf);
        Assert.Equal("Accepted", report.GetProperty("verdict").GetString());
        Assert.Equal("InProgress", report.GetProperty("personalState").GetProperty("playState").GetString());

        administratorLibrary = await administrator.GetFromJsonAsync<JsonElement>(
            "/api/library/videos?shelf=ContinueWatching",
            TestContext.Current.CancellationToken);
        Assert.Single(administratorLibrary.GetProperty("videos").EnumerateArray());
        userLibrary = await user.GetFromJsonAsync<JsonElement>(
            "/api/library/videos?shelf=ContinueWatching",
            TestContext.Current.CancellationToken);
        Assert.Empty(userLibrary.GetProperty("videos").EnumerateArray());

        var loved = await SendJsonAsync(
            administrator,
            HttpMethod.Put,
            $"/api/personal/videos/{video.VideoId}/reaction",
            new { reaction = "Love" },
            administratorCsrf);
        Assert.Equal("Love", loved.GetProperty("personalState").GetProperty("reaction").GetString());

        // Setting the same reaction again is the same answer, and setting another replaces it.
        var again = await SendJsonAsync(
            administrator,
            HttpMethod.Put,
            $"/api/personal/videos/{video.VideoId}/reaction",
            new { reaction = "Love" },
            administratorCsrf);
        Assert.Equal("Love", again.GetProperty("personalState").GetProperty("reaction").GetString());
        var shrugged = await SendJsonAsync(
            administrator,
            HttpMethod.Put,
            $"/api/personal/videos/{video.VideoId}/reaction",
            new { reaction = "Shrug" },
            administratorCsrf);
        Assert.Equal("Shrug", shrugged.GetProperty("personalState").GetProperty("reaction").GetString());

        // A Shrug is a statement; clearing removes the statement. The wire distinguishes them,
        // because a screen that showed "no opinion" for both would be inventing one of the two.
        var cleared = await SendJsonAsync(
            administrator,
            HttpMethod.Delete,
            $"/api/personal/videos/{video.VideoId}/reaction",
            null,
            administratorCsrf);
        Assert.Equal(
            JsonValueKind.Null,
            cleared.GetProperty("personalState").GetProperty("reaction").ValueKind);

        // What one Account says about a Video is not what the other reads.
        await SendJsonAsync(
            administrator,
            HttpMethod.Put,
            $"/api/personal/videos/{video.VideoId}/reaction",
            new { reaction = "Dislike" },
            administratorCsrf);
        var theirs = await SendJsonAsync(
            user,
            HttpMethod.Put,
            $"/api/personal/videos/{video.VideoId}/reaction",
            new { reaction = "Like" },
            userCsrf);
        Assert.Equal("Like", theirs.GetProperty("personalState").GetProperty("reaction").GetString());
        var mine = await administrator.GetFromJsonAsync<JsonElement>(
            $"/api/library/videos/{video.VideoId}",
            TestContext.Current.CancellationToken);
        Assert.Equal(
            "Dislike",
            mine.GetProperty("video").GetProperty("personalState").GetProperty("reaction").GetString());

        await PlaylistsBelongToOneAccountAsync(
            administrator,
            administratorCsrf,
            user,
            userCsrf,
            video.VideoId);

        await RecommendationsBelongToOneAccountAsync(
            administrator,
            administratorCsrf,
            user,
            video.VideoId);
    }

    /// <summary>
    /// Recommendations are Personal State, evidence and all. Nobody signed out reads them, a
    /// dismissal needs the CSRF token every change needs, and what one Account put aside is
    /// invisible to the other — including to the Administrator, who has no authority here.
    /// </summary>
    private static async Task RecommendationsBelongToOneAccountAsync(
        HttpClient administrator,
        string administratorCsrf,
        HttpClient user,
        Guid videoId)
    {
        using var anonymous = new HttpRequestMessage(HttpMethod.Get, "/api/personal/recommendations");
        using var refused = await administrator.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, $"/api/personal/recommendations/videos/{videoId}/not-today"),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        var dismissed = await SendJsonAsync(
            administrator,
            HttpMethod.Post,
            $"/api/personal/recommendations/videos/{videoId}/not-today",
            null,
            administratorCsrf);
        Assert.True(dismissed.GetProperty("dismissed").GetBoolean());

        var mine = await administrator.GetFromJsonAsync<JsonElement>(
            "/api/personal/recommendations",
            TestContext.Current.CancellationToken);
        Assert.DoesNotContain(
            videoId,
            mine.GetProperty("sections").EnumerateArray()
                .SelectMany(section => section.GetProperty("videos").EnumerateArray())
                .Select(offered => offered.GetProperty("video").GetProperty("id").GetGuid()));

        // The other Account is unaffected by what this one put aside, and the page it is answered
        // is computed from its own state.
        var theirs = await user.GetFromJsonAsync<JsonElement>(
            "/api/personal/recommendations",
            TestContext.Current.CancellationToken);
        Assert.Contains(
            videoId,
            theirs.GetProperty("sections").EnumerateArray()
                .SelectMany(section => section.GetProperty("videos").EnumerateArray())
                .Select(offered => offered.GetProperty("video").GetProperty("id").GetGuid()));
    }

    /// <summary>
    /// A Playlist is one Account's own filing. Another Account cannot list it, narrow the Library
    /// to it, change it, or delete it, and an Administrator has no more authority over one than
    /// anybody else — which is the case worth stating, because everywhere else in this product an
    /// Administrator has more.
    /// </summary>
    private static async Task PlaylistsBelongToOneAccountAsync(
        HttpClient administrator,
        string administratorCsrf,
        HttpClient user,
        string userCsrf,
        Guid videoId)
    {
        using var missingCsrf = await user.PostAsJsonAsync(
            "/api/personal/playlists",
            new { name = "Without a token" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, missingCsrf.StatusCode);

        var created = await SendJsonAsync(
            user,
            HttpMethod.Post,
            "/api/personal/playlists",
            new { name = "Theirs" },
            userCsrf);
        var playlistId = created.GetProperty("playlist").GetProperty("id").GetGuid();

        await SendJsonAsync(
            user,
            HttpMethod.Put,
            $"/api/personal/playlists/{playlistId}/videos/{videoId}",
            null,
            userCsrf);

        var theirs = await user.GetFromJsonAsync<JsonElement>(
            $"/api/personal/playlists?videoId={videoId}",
            TestContext.Current.CancellationToken);
        var listed = Assert.Single(theirs.GetProperty("playlists").EnumerateArray());
        Assert.True(listed.GetProperty("contains").GetBoolean());
        Assert.Equal(1, listed.GetProperty("videoCount").GetInt32());

        // The Administrator's own list is empty, and naming the other Account's Playlist in a
        // Library request narrows to nothing rather than to its contents.
        var mine = await administrator.GetFromJsonAsync<JsonElement>(
            "/api/personal/playlists",
            TestContext.Current.CancellationToken);
        Assert.Empty(mine.GetProperty("playlists").EnumerateArray());
        var borrowed = await administrator.GetFromJsonAsync<JsonElement>(
            $"/api/library/videos?playlist={playlistId}&sort=PlaylistOrder",
            TestContext.Current.CancellationToken);
        Assert.Empty(borrowed.GetProperty("videos").EnumerateArray());

        using var rename = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/personal/playlists/{playlistId}");
        rename.Headers.Add("X-CSRF-Token", administratorCsrf);
        rename.Content = JsonContent.Create(new { name = "Mine now" });
        using var renamed = await administrator.SendAsync(
            rename,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, renamed.StatusCode);

        using var delete = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/personal/playlists/{playlistId}");
        delete.Headers.Add("X-CSRF-Token", administratorCsrf);
        using var deleted = await administrator.SendAsync(
            delete,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);

        // And it is still there, under the name its owner gave it.
        var after = await user.GetFromJsonAsync<JsonElement>(
            "/api/personal/playlists",
            TestContext.Current.CancellationToken);
        Assert.Equal(
            "Theirs",
            Assert.Single(after.GetProperty("playlists").EnumerateArray())
                .GetProperty("name")
                .GetString());
    }

    private static object Report(
        Guid videoFileId,
        int sequence,
        long positionMilliseconds,
        long activeWatchingMilliseconds) => new
        {
            reportId = Guid.NewGuid(),
            sequence,
            videoFileId,
            positionMilliseconds,
            activeWatchingMilliseconds,
            naturalEndConfirmed = false,
            endSession = false,
        };

    private static async Task<string> ClaimAsync(
        ViewerApplication application,
        HttpClient administrator)
    {
        var authorization = await application.CreateBootstrapAuthorizationAsync();
        var response = await administrator.PostAsJsonAsync(
            "/api/access/bootstrap",
            new
            {
                authorization,
                username = "administrator",
                password = "administrator password",
                email = (string?)null,
            },
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken))
            .GetProperty("account")
            .GetProperty("csrfToken")
            .GetString()!;
    }

    private static async Task<string> RegisterApproveAndSignInAsync(
        HttpClient administrator,
        HttpClient user,
        string administratorCsrf)
    {
        using var registration = await user.PostAsJsonAsync(
            "/api/access/registration-requests",
            new
            {
                username = "user",
                password = "user password long enough",
                email = (string?)null,
            },
            TestContext.Current.CancellationToken);
        registration.EnsureSuccessStatusCode();
        var accounts = await administrator.GetFromJsonAsync<JsonElement[]>(
            "/api/admin/accounts/",
            TestContext.Current.CancellationToken);
        var accountId = accounts!
            .Single(account => account.GetProperty("username").GetString() == "user")
            .GetProperty("id")
            .GetGuid();
        using var approval = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/admin/accounts/{accountId}/approve");
        approval.Headers.Add("X-CSRF-Token", administratorCsrf);
        using var approvalResponse = await administrator.SendAsync(
            approval,
            TestContext.Current.CancellationToken);
        approvalResponse.EnsureSuccessStatusCode();

        using var signIn = await user.PostAsJsonAsync(
            "/api/access/sign-in",
            new { username = "user", password = "user password long enough" },
            TestContext.Current.CancellationToken);
        signIn.EnsureSuccessStatusCode();
        return (await signIn.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken))
            .GetProperty("account")
            .GetProperty("csrfToken")
            .GetString()!;
    }

    private static async Task<JsonElement> SendJsonAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        object? body,
        string csrfToken)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        request.Headers.Add("X-CSRF-Token", csrfToken);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
    }

    private static async Task<VideoIds> AddVideoAsync(ViewerApplication application)
    {
        _ = application.Server;
        var source = Path.Combine(application.LibraryMountRoot, "personal");
        Directory.CreateDirectory(source);
        var path = Path.Combine(source, "personal.mp4");
        await File.WriteAllTextAsync(path, "0123456789", TestContext.Current.CancellationToken);
        var file = new FileInfo(path);
        var directoryId = Guid.CreateVersion7();
        var videoId = Guid.CreateVersion7();
        var videoFileId = Guid.CreateVersion7();

        await using var scope = application.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        database.LibraryDirectories.Add(new LibraryDirectoryRow
        {
            Id = directoryId,
            Name = "Personal Test Library",
            ContainerPath = source,
            State = LibraryDirectoryState.Active,
            Health = LibraryDirectoryHealth.Healthy,
            ConfigurationGeneration = 1,
            CreatedAt = file.LastWriteTimeUtc,
            ActivatedAt = file.LastWriteTimeUtc,
        });
        // The projection would normally supply these; this row is written straight in, and
        // Ordinary Discovery reads them.
        database.Videos.Add(new VideoRow
        {
            Id = videoId,
            DiscoveryDate = file.LastWriteTimeUtc,
            Availability = VideoAvailability.Available,
            BestClassification = DirectPlayClassification.BaselineCandidate,
        });
        database.VideoFiles.Add(new VideoFileRow
        {
            Id = videoFileId,
            VideoId = videoId,
            LibraryDirectoryId = directoryId,
            RelativePath = "personal.mp4",
            Size = file.Length,
            LastWriteTimeUtc = file.LastWriteTimeUtc,
            Sha256 = new string('B', 64),
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
            InspectedAt = file.LastWriteTimeUtc,
        });
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new VideoIds(videoId, videoFileId);
    }

    private sealed record VideoIds(Guid VideoId, Guid VideoFileId);
}
