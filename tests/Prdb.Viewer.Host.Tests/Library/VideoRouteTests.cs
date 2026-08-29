using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Viewer.Host.Tests.Library;

public sealed class VideoRouteTests
{
    [Fact]
    public async Task Approved_user_browses_catalogue_and_anonymous_delivery_supports_ranges()
    {
        using var application = new ViewerApplication();
        using var client = application.CreateClient();
        var deliveryId = await AddVideoAsync(application);

        using var anonymousCatalogue = await client.GetAsync(
            "/api/library/videos",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousCatalogue.StatusCode);

        using var range = new HttpRequestMessage(HttpMethod.Get, $"/media/videos/{deliveryId}");
        range.Headers.Range = new RangeHeaderValue(2, 5);
        using var partial = await client.SendAsync(range, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
        Assert.Equal("video/mp4", partial.Content.Headers.ContentType?.MediaType);
        Assert.Equal("bytes 2-5/10", partial.Content.Headers.ContentRange?.ToString());
        Assert.Equal("2345", await partial.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken));

        var authorization = await application.CreateBootstrapAuthorizationAsync();
        using var claim = await client.PostAsJsonAsync(
            "/api/access/bootstrap",
            new
            {
                authorization,
                username = "administrator",
                password = "administrator password",
                email = (string?)null,
            },
            TestContext.Current.CancellationToken);
        claim.EnsureSuccessStatusCode();

        var catalogue = await client.GetFromJsonAsync<JsonElement>(
            "/api/library/videos",
            TestContext.Current.CancellationToken);
        Assert.Equal(1, catalogue.GetProperty("totalMatches").GetInt32());
        Assert.False(catalogue.GetProperty("hasMore").GetBoolean());
        Assert.Equal(0, catalogue.GetProperty("hiddenNotReadyForDirectPlay").GetInt32());
        var video = Assert.Single(catalogue.GetProperty("videos").EnumerateArray());
        Assert.Equal("sample", video.GetProperty("displayTitle").GetString());
        var videoFile = Assert.Single(video.GetProperty("videoFiles").EnumerateArray());
        Assert.Equal("BaselineCandidate", videoFile.GetProperty("directPlayClassification").GetString());
        Assert.Equal($"/media/videos/{deliveryId}", videoFile.GetProperty("deliveryUrl").GetString());

        using var missing = await client.GetAsync(
            $"/media/videos/{Guid.NewGuid()}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        // One Video answered on its own, which is what a link to it needs.
        var addressed = await client.GetFromJsonAsync<JsonElement>(
            $"/api/library/videos/{video.GetProperty("id").GetString()}",
            TestContext.Current.CancellationToken);
        Assert.Equal("sample", addressed.GetProperty("video").GetProperty("displayTitle").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            addressed.GetProperty("supersededVideoId").ValueKind);

        using var unknown = await client.GetAsync(
            $"/api/library/videos/{Guid.CreateVersion7()}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    private static async Task<Guid> AddVideoAsync(ViewerApplication application)
    {
        _ = application.Server;
        var source = Path.Combine(application.LibraryMountRoot, "main");
        Directory.CreateDirectory(source);
        var path = Path.Combine(source, "sample.mp4");
        await File.WriteAllTextAsync(path, "0123456789", TestContext.Current.CancellationToken);
        var file = new FileInfo(path);
        var directoryId = Guid.CreateVersion7();
        var videoId = Guid.CreateVersion7();
        var deliveryId = Guid.NewGuid();

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
        database.Videos.Add(new VideoRow
        {
            Id = videoId,
            DiscoveryDate = file.LastWriteTimeUtc,
        });
        database.VideoFiles.Add(new VideoFileRow
        {
            Id = Guid.CreateVersion7(),
            VideoId = videoId,
            LibraryDirectoryId = directoryId,
            RelativePath = "sample.mp4",
            Size = file.Length,
            LastWriteTimeUtc = file.LastWriteTimeUtc,
            Sha256 = new string('A', 64),
            PublicDeliveryId = deliveryId,
            ContainerFormat = "mp4",
            VideoCodec = "h264",
            AudioCodec = "aac",
            DurationMilliseconds = 10_000,
            Width = 640,
            Height = 360,
            Availability = VideoFileAvailability.Available,
            DirectPlayClassification = DirectPlayClassification.BaselineCandidate,
            LastObservedScanId = Guid.CreateVersion7(),
            InspectedAt = file.LastWriteTimeUtc,
        });
        // A fixture that writes rows directly is a write path like any other: without the
        // discovery projection the Video exists but nothing can find it (ADR 0013).
        await scope.ServiceProvider
            .GetRequiredService<VideoProjection>()
            .RefreshTrackedAsync(TestContext.Current.CancellationToken);
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        return deliveryId;
    }
}
