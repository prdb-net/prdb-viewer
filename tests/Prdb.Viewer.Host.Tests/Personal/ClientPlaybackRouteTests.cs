using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Viewer.Host.Tests.Personal;

/// <summary>
/// The client-facing half of the direct-play contract over HTTP: a browser says which context it
/// speaks for, is asked what it can play, answers, and is then offered what it answered for.
/// </summary>
public sealed class ClientPlaybackRouteTests
{
    private const string Chrome = "route-chrome";
    private const string Firefox = "route-firefox";

    [Fact]
    public async Task A_client_is_offered_what_it_qualified_and_nothing_it_did_not()
    {
        using var application = new ViewerApplication();
        using var client = application.CreateClient();
        await AddClientDependentVideoAsync(application);
        var csrf = await ClaimAsync(application, client);

        // An unqualified browser is offered nothing it has not vouched for.
        var beforeQualification = await LibraryAsync(client, Chrome);
        Assert.Empty(beforeQualification.GetProperty("videos").EnumerateArray());
        Assert.Equal(
            1,
            beforeQualification.GetProperty("hiddenNotReadyForDirectPlay").GetInt32());

        var profiles = await ProfilesAsync(client, Chrome);
        var profile = Assert.Single(profiles.EnumerateArray());
        Assert.Equal(
            "video/mp4; codecs=\"avc1.640028\"",
            profile.GetProperty("videoContentType").GetString());

        using var withoutCsrf = await client.PutAsJsonAsync(
            "/api/personal/playback-assessments",
            new { assessments = Array.Empty<object>() },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, withoutCsrf.StatusCode);

        await AssessAsync(client, csrf, Chrome, profile.GetProperty("profileKey").GetString()!);

        var qualified = await LibraryAsync(client, Chrome);
        var video = Assert.Single(qualified.GetProperty("videos").EnumerateArray());
        Assert.Equal("ReadyForDirectPlay", video.GetProperty("playability").GetString());
        Assert.False(video.GetProperty("isUnsupportedVideo").GetBoolean());
        var variant = Assert.Single(video.GetProperty("videoFiles").EnumerateArray());
        Assert.Equal("PositivelyAssessedAndSmooth", variant.GetProperty("selectionReason").GetString());
        Assert.True(variant.GetProperty("readyForDirectPlay").GetBoolean());

        // The same Account in another browser is a separate question, and it is still open.
        var elsewhere = await LibraryAsync(client, Firefox);
        Assert.Empty(elsewhere.GetProperty("videos").EnumerateArray());
        Assert.Single((await ProfilesAsync(client, Firefox)).EnumerateArray());

        // A browser that says nothing about itself gets the unqualified context, not another's.
        var anonymousContext = await LibraryAsync(client, null);
        Assert.Empty(anonymousContext.GetProperty("videos").EnumerateArray());
    }

    [Fact]
    public async Task Only_a_media_failure_is_remembered_and_an_explicit_retry_forgets_it()
    {
        using var application = new ViewerApplication();
        using var client = application.CreateClient();
        var video = await AddClientDependentVideoAsync(application);
        var csrf = await ClaimAsync(application, client);
        var profiles = await ProfilesAsync(client, Chrome);
        await AssessAsync(
            client,
            csrf,
            Chrome,
            profiles.EnumerateArray().Single().GetProperty("profileKey").GetString()!);

        var delivery = await OutcomeAsync(client, csrf, Chrome, video.VideoFileId, "Delivery");
        Assert.False(delivery.GetProperty("recorded").GetBoolean());
        Assert.Single((await LibraryAsync(client, Chrome)).GetProperty("videos").EnumerateArray());

        var media = await OutcomeAsync(client, csrf, Chrome, video.VideoFileId, "Media");
        Assert.True(media.GetProperty("recorded").GetBoolean());
        Assert.Empty((await LibraryAsync(client, Chrome)).GetProperty("videos").EnumerateArray());

        using var retry = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/personal/videos/{video.VideoId}/playback-outcomes");
        retry.Headers.Add("X-CSRF-Token", csrf);
        retry.Headers.Add("X-Client-Context", Chrome);
        using var retried = await client.SendAsync(retry, TestContext.Current.CancellationToken);
        retried.EnsureSuccessStatusCode();
        Assert.Single((await LibraryAsync(client, Chrome)).GetProperty("videos").EnumerateArray());
    }

    private static async Task<JsonElement> LibraryAsync(HttpClient client, string? context)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/library/videos");

        if (context is not null)
        {
            request.Headers.Add("X-Client-Context", context);
        }

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
    }

    private static async Task<JsonElement> ProfilesAsync(HttpClient client, string context)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/personal/playback-profiles");
        request.Headers.Add("X-Client-Context", context);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
    }

    private static async Task AssessAsync(
        HttpClient client,
        string csrf,
        string context,
        string profileKey)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            "/api/personal/playback-assessments")
        {
            Content = JsonContent.Create(new
            {
                assessments = new[]
                {
                    new
                    {
                        profileKey,
                        verdict = "Positive",
                        smooth = true,
                        powerEfficient = true,
                        method = "MediaCapabilities",
                    },
                },
            }),
        };
        request.Headers.Add("X-CSRF-Token", csrf);
        request.Headers.Add("X-Client-Context", context);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<JsonElement> OutcomeAsync(
        HttpClient client,
        string csrf,
        string context,
        Guid videoFileId,
        string failureCategory)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/personal/playback-outcomes")
        {
            Content = JsonContent.Create(new
            {
                videoFileId,
                outcome = "Failed",
                failureCategory,
            }),
        };
        request.Headers.Add("X-CSRF-Token", csrf);
        request.Headers.Add("X-Client-Context", context);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
    }

    private static async Task<(Guid VideoId, Guid VideoFileId)> AddClientDependentVideoAsync(
        ViewerApplication application)
    {
        _ = application.Server;
        var source = Path.Combine(application.LibraryMountRoot, "main");
        Directory.CreateDirectory(source);
        var path = Path.Combine(source, "ordinary.mp4");
        await File.WriteAllTextAsync(path, "0123456789", TestContext.Current.CancellationToken);
        var file = new FileInfo(path);
        var directoryId = Guid.CreateVersion7();
        var videoId = Guid.CreateVersion7();
        var videoFileId = Guid.CreateVersion7();
        var media = new MediaConfiguration("mov,mp4,m4a,3gp,3g2,mj2", "h264", "aac")
        {
            VideoProfile = "High",
            VideoLevel = 40,
            BitDepth = 8,
            Width = 1920,
            Height = 1080,
            FrameRate = 25,
            VideoBitrate = 4_000_000,
            AudioChannels = 2,
            AudioSampleRate = 48_000,
        };

        await using var scope = application.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        database.LibraryDirectories.Add(new LibraryDirectoryRow
        {
            Id = directoryId,
            Name = "Main Library",
            ContainerPath = source,
            State = LibraryDirectoryState.Active,
            Health = LibraryDirectoryHealth.Healthy,
            ConfigurationGeneration = 1,
            CreatedAt = file.LastWriteTimeUtc,
            ActivatedAt = file.LastWriteTimeUtc,
        });
        database.Videos.Add(new VideoRow { Id = videoId, DiscoveryDate = file.LastWriteTimeUtc });

        var row = new VideoFileRow
        {
            Id = videoFileId,
            VideoId = videoId,
            LibraryDirectoryId = directoryId,
            RelativePath = "ordinary.mp4",
            Size = file.Length,
            LastWriteTimeUtc = file.LastWriteTimeUtc,
            Sha256 = new string('A', 64),
            PublicDeliveryId = Guid.NewGuid(),
            ContainerFormat = media.ContainerFormat,
            VideoCodec = media.VideoCodec,
            Availability = VideoFileAvailability.Available,
            LastObservedScanId = Guid.CreateVersion7(),
            InspectedAt = file.LastWriteTimeUtc,
        };
        row.ApplyInspectedMedia(media, 10_000);
        database.VideoFiles.Add(row);
        await scope.ServiceProvider
            .GetRequiredService<VideoProjection>()
            .RefreshTrackedAsync(TestContext.Current.CancellationToken);
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (videoId, videoFileId);
    }

    private static async Task<string> ClaimAsync(ViewerApplication application, HttpClient client)
    {
        var authorization = await application.CreateBootstrapAuthorizationAsync();
        using var response = await client.PostAsJsonAsync(
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
}
