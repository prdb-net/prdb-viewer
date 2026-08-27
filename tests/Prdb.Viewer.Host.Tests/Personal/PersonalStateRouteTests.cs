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
                "/api/personal/library",
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

        var administratorLibrary = await administrator.GetFromJsonAsync<JsonElement>(
            "/api/personal/library",
            TestContext.Current.CancellationToken);
        Assert.Single(administratorLibrary.GetProperty("favourites").EnumerateArray());
        var userLibrary = await user.GetFromJsonAsync<JsonElement>(
            "/api/personal/library",
            TestContext.Current.CancellationToken);
        Assert.Empty(userLibrary.GetProperty("favourites").EnumerateArray());

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
            "/api/personal/library",
            TestContext.Current.CancellationToken);
        Assert.Single(administratorLibrary.GetProperty("continueWatching").EnumerateArray());
        userLibrary = await user.GetFromJsonAsync<JsonElement>(
            "/api/personal/library",
            TestContext.Current.CancellationToken);
        Assert.Empty(userLibrary.GetProperty("continueWatching").EnumerateArray());

        var rating = await SendJsonAsync(
            administrator,
            HttpMethod.Put,
            $"/api/personal/videos/{video.VideoId}/rating",
            new { rating = 5 },
            administratorCsrf);
        Assert.Equal(5, rating.GetProperty("personalState").GetProperty("personalRating").GetInt32());
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
        object body,
        string csrfToken)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
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
        database.Videos.Add(new VideoRow { Id = videoId, DiscoveryDate = file.LastWriteTimeUtc });
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
