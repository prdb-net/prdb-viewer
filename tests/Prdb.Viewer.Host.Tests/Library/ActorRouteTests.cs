using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Viewer.Host.Tests.Library;

/// <summary>
/// What the Actor routes answer, as distinct from what an Actor's page is made of.
///
/// The library side of this is settled elsewhere, against the services themselves. What only shows
/// up at the boundary is who may ask: an Actor is Shared Library Knowledge that every User reads,
/// keeping one is Personal State that no other Account sees, and an Actor's pictures are anonymous
/// because a browser fetches them without the application's credentials.
/// </summary>
public sealed class ActorRouteTests
{
    private static readonly string ActorId = Guid.CreateVersion7().ToString();

    [Fact]
    public async Task Actors_are_read_by_any_signed_in_account_and_by_nobody_else()
    {
        using var application = new ViewerApplication();
        using var administrator = application.CreateClient();
        using var user = application.CreateClient();
        using var anonymous = application.CreateClient();
        await SeedAsync(application);
        var administratorCsrf = await ClaimAsync(application, administrator);
        await RegisterApproveAndSignInAsync(administrator, user, administratorCsrf);

        foreach (var path in new[] { "/api/library/actors", $"/api/library/actors/{ActorId}" })
        {
            using var refused = await anonymous.GetAsync(path, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        }

        // An Actor is not an administrative surface: an approved ordinary User reads them.
        var index = await user.GetFromJsonAsync<JsonElement>(
            "/api/library/actors",
            TestContext.Current.CancellationToken);
        var listed = Assert.Single(index.GetProperty("actors").EnumerateArray());
        Assert.Equal("Alex Doe", listed.GetProperty("name").GetString());
        Assert.Equal(1, listed.GetProperty("videoCount").GetInt32());

        var actor = await user.GetFromJsonAsync<JsonElement>(
            $"/api/library/actors/{ActorId}",
            TestContext.Current.CancellationToken);
        Assert.Equal("Alex Doe", actor.GetProperty("name").GetString());

        // An identity this installation does not hold is not there, rather than an empty Actor.
        using var unknown = await user.GetAsync(
            $"/api/library/actors/{Guid.CreateVersion7()}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Keeping_an_actor_needs_the_csrf_token_and_belongs_to_one_account()
    {
        using var application = new ViewerApplication();
        using var administrator = application.CreateClient();
        using var user = application.CreateClient();
        await SeedAsync(application);
        var administratorCsrf = await ClaimAsync(application, administrator);
        var userCsrf = await RegisterApproveAndSignInAsync(administrator, user, administratorCsrf);

        using var missingCsrf = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/personal/actors/{ActorId}/favourite");
        using var refused = await administrator.SendAsync(
            missingCsrf,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        var kept = await SendAsync(
            administrator,
            HttpMethod.Put,
            $"/api/personal/actors/{ActorId}/favourite",
            administratorCsrf);
        Assert.True(kept.GetProperty("favourite").GetBoolean());

        // Personal State, so the other Account sees its own answer rather than this one.
        var mine = await administrator.GetFromJsonAsync<JsonElement>(
            $"/api/library/actors/{ActorId}",
            TestContext.Current.CancellationToken);
        Assert.True(mine.GetProperty("favourite").GetBoolean());
        var theirs = await user.GetFromJsonAsync<JsonElement>(
            $"/api/library/actors/{ActorId}",
            TestContext.Current.CancellationToken);
        Assert.False(theirs.GetProperty("favourite").GetBoolean());

        var released = await SendAsync(
            administrator,
            HttpMethod.Delete,
            $"/api/personal/actors/{ActorId}/favourite",
            administratorCsrf);
        Assert.False(released.GetProperty("favourite").GetBoolean());

        // Nobody can be kept who is not here, and saying so is not the same as accepting it.
        using var unknown = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/personal/actors/{Guid.CreateVersion7()}/favourite");
        unknown.Headers.Add("X-CSRF-Token", userCsrf);
        using var unknownResponse = await user.SendAsync(
            unknown,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, unknownResponse.StatusCode);
    }

    [Fact]
    public async Task Retained_pictures_are_anonymous_and_answer_for_nothing_they_do_not_hold()
    {
        using var application = new ViewerApplication();
        using var anonymous = application.CreateClient();
        await SeedAsync(application);

        // Anonymous by design: a browser's own img element fetches these without the application's
        // credentials. The protection is the random identifier, so an identifier this installation
        // holds nothing for is a plain absence rather than a refusal that says something exists.
        foreach (var path in new[] { "/media/actors", "/media/works" })
        {
            using var response = await anonymous.GetAsync(
                $"{path}/{Guid.NewGuid()}",
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    private static async Task<JsonElement> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string csrfToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-CSRF-Token", csrfToken);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
    }

    /// <summary>One Actor, credited by one Video this library holds.</summary>
    private static async Task SeedAsync(ViewerApplication application)
    {
        _ = application.Server;
        var source = Path.Combine(application.LibraryMountRoot, "actors");
        Directory.CreateDirectory(source);
        var path = Path.Combine(source, "credited.mp4");
        await File.WriteAllTextAsync(path, "0123456789", TestContext.Current.CancellationToken);
        var file = new FileInfo(path);
        var directoryId = Guid.CreateVersion7();
        var videoId = Guid.CreateVersion7();

        await using var scope = application.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        database.LibraryDirectories.Add(new LibraryDirectoryRow
        {
            Id = directoryId,
            Name = "Actor Test Library",
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
            Id = Guid.CreateVersion7(),
            VideoId = videoId,
            LibraryDirectoryId = directoryId,
            RelativePath = "credited.mp4",
            Size = file.Length,
            LastWriteTimeUtc = file.LastWriteTimeUtc,
            Sha256 = new string('C', 64),
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
        database.VideoActors.Add(new VideoActorRow
        {
            Id = Guid.CreateVersion7(),
            VideoId = videoId,
            PrdbActorId = ActorId,
            Name = "Alex Doe",
            NormalizedName = LibrarySearchRule.Normalize("Alex Doe"),
        });
        database.Actors.Add(new ActorRow
        {
            Id = Guid.CreateVersion7(),
            PrdbActorId = ActorId,
            Name = "Alex Doe",
            NormalizedName = LibrarySearchRule.Normalize("Alex Doe"),
            ProfileState = ActorProfileState.Unavailable,
        });
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
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
}
