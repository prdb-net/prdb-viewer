using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Viewer.Host.Tests.Library;

public sealed class IdentificationRouteTests
{
    [Fact]
    public async Task Only_administrators_review_identification_and_decisions_require_csrf()
    {
        using var application = new ViewerApplication();
        using var administrator = application.CreateClient();
        using var user = application.CreateClient();
        var fixture = await AddCandidateAsync(application);
        var administratorCsrf = await ClaimAsync(application, administrator);
        await RegisterApproveAndSignInAsync(administrator, user, administratorCsrf);

        using var anonymous = application.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync(
                "/api/admin/identification/queue",
                TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await user.GetAsync(
                "/api/admin/identification/queue",
                TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await user.GetAsync(
                $"/api/admin/identification/videos/{fixture.VideoId}",
                TestContext.Current.CancellationToken)).StatusCode);

        var queue = await administrator.GetFromJsonAsync<JsonElement[]>(
            "/api/admin/identification/queue",
            TestContext.Current.CancellationToken);
        var item = Assert.Single(queue!);
        Assert.Equal(fixture.VideoId, item.GetProperty("videoId").GetGuid());
        Assert.Equal("A Guessed Work", item.GetProperty("candidate").GetProperty("targetTitle").GetString());
        Assert.Equal("Unknown", item.GetProperty("currentResolution").GetString());
        Assert.Equal($"/media/previews/{fixture.PreviewId}", item.GetProperty("previewUrl").GetString());

        var decision = new
        {
            action = "AcceptCandidate",
            dimension = "WorkIdentification",
            caseVersion = item.GetProperty("caseVersion").GetInt32(),
            confirm = true,
            candidateId = item.GetProperty("candidate").GetProperty("id").GetGuid(),
        };

        using var withoutCsrf = await administrator.PostAsJsonAsync(
            $"/api/admin/identification/videos/{fixture.VideoId}/decisions",
            decision,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, withoutCsrf.StatusCode);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/admin/identification/videos/{fixture.VideoId}/decisions")
        {
            Content = JsonContent.Create(decision),
        };
        request.Headers.Add("X-CSRF-Token", administratorCsrf);
        using var response = await administrator.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var applied = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        Assert.Equal("Applied", applied.GetProperty("verdict").GetString());
        Assert.Equal(
            "Established",
            applied.GetProperty("case").GetProperty("identification")
                .GetProperty("work").GetProperty("resolution").GetString());

        var catalogue = await user.GetFromJsonAsync<JsonElement>(
            "/api/library/videos",
            TestContext.Current.CancellationToken);
        var video = Assert.Single(catalogue.GetProperty("videos").EnumerateArray());
        Assert.Equal("A Guessed Work", video.GetProperty("displayTitle").GetString());
        Assert.Equal($"/media/previews/{fixture.PreviewId}", video.GetProperty("previewUrl").GetString());
        Assert.Equal(
            "Clear",
            video.GetProperty("identification").GetProperty("work")
                .GetProperty("reviewStatus").GetString());
        Assert.False(video.TryGetProperty("openCandidates", out _));
    }

    [Fact]
    public async Task Preview_delivery_is_anonymous_and_unknown_identifiers_are_not_found()
    {
        using var application = new ViewerApplication();
        using var client = application.CreateClient();
        var fixture = await AddCandidateAsync(application);

        using var preview = await client.GetAsync(
            $"/media/previews/{fixture.PreviewId}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal("image/jpeg", preview.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            3,
            (await preview.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).Length);

        using var missing = await client.GetAsync(
            $"/media/previews/{Guid.NewGuid()}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private static async Task<Fixture> AddCandidateAsync(ViewerApplication application)
    {
        _ = application.Server;
        var source = Path.Combine(application.LibraryMountRoot, "identified");
        Directory.CreateDirectory(source);
        var path = Path.Combine(source, "candidate.mp4");
        await File.WriteAllTextAsync(path, "0123456789", TestContext.Current.CancellationToken);
        var file = new FileInfo(path);
        var directoryId = Guid.CreateVersion7();
        var videoId = Guid.CreateVersion7();
        var videoFileId = Guid.CreateVersion7();
        var previewId = Guid.NewGuid();

        await using var scope = application.Services.CreateAsyncScope();
        var artifacts = scope.ServiceProvider.GetRequiredService<DerivedArtifactStore>();
        var previewRelativePath = DerivedArtifactStore.PreviewRelativePath(videoFileId);
        artifacts.EnsurePreviewDirectory(previewRelativePath);
        await File.WriteAllBytesAsync(
            artifacts.PreviewFullPath(previewRelativePath),
            [0xFF, 0xD8, 0xFF],
            TestContext.Current.CancellationToken);

        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        database.LibraryDirectories.Add(new LibraryDirectoryRow
        {
            Id = directoryId,
            Name = "Identified Library",
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
            RelativePath = "candidate.mp4",
            Size = file.Length,
            LastWriteTimeUtc = file.LastWriteTimeUtc,
            Sha256 = new string('C', 64),
            PublicDeliveryId = Guid.NewGuid(),
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
            HashState = VideoFileHashState.Computed,
            HashedSha256 = new string('C', 64),
            OsHash = "0123456789abcdef",
            PerceptualHash = "fedcba9876543210",
            PreviewState = VideoFilePreviewState.Generated,
            PreviewSha256 = new string('C', 64),
            PreviewRelativePath = previewRelativePath,
            PublicPreviewId = previewId,
            PreviewGeneratedAt = file.LastWriteTimeUtc,
        });
        database.IdentificationCandidates.Add(new IdentificationCandidateRow
        {
            Id = Guid.CreateVersion7(),
            VideoId = videoId,
            Dimension = IdentificationDimension.WorkIdentification,
            Status = IdentificationCandidateStatus.Pending,
            TargetKey = "6f1a2c34-0000-4000-8000-000000000001",
            TargetTitle = "A Guessed Work",
            EvidenceClass = IdentificationEvidenceClass.Suggestive,
            Reason = IdentificationReviewReason.SuggestiveEvidence,
            MatchedBy = "Filename",
            Confidence = "Probable",
            EvidenceKey = "Filename:candidate.mp4",
            SupportingVideoFileId = videoFileId,
            CreatedAt = file.LastWriteTimeUtc,
        });
        // A fixture that writes rows directly is a write path like any other: without the
        // discovery projection the Video exists but nothing can find it (ADR 0013).
        await scope.ServiceProvider
            .GetRequiredService<VideoProjection>()
            .RefreshTrackedAsync(TestContext.Current.CancellationToken);
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new Fixture(videoId, previewId);
    }

    private static async Task<string> ClaimAsync(
        ViewerApplication application,
        HttpClient administrator)
    {
        var authorization = await application.CreateBootstrapAuthorizationAsync();
        using var response = await administrator.PostAsJsonAsync(
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

    private static async Task RegisterApproveAndSignInAsync(
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
    }

    private sealed record Fixture(Guid VideoId, Guid PreviewId);
}
