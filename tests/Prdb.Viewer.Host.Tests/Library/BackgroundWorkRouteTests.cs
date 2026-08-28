using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Viewer.Host.Tests.Library;

public sealed class BackgroundWorkRouteTests
{
    [Fact]
    public async Task Only_administrators_see_operations_and_every_change_requires_csrf()
    {
        using var application = new ViewerApplication();
        using var administrator = application.CreateClient();
        using var user = application.CreateClient();
        var fixture = await AddIssueAsync(application);
        var csrf = await ClaimAsync(application, administrator);
        await RegisterApproveAndSignInAsync(administrator, user, csrf);

        using var anonymous = application.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync(
                "/api/admin/background-work/",
                TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await user.GetAsync(
                "/api/admin/background-work/",
                TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await user.GetAsync(
                $"/api/admin/background-work/issues/{fixture.IssueId}/items",
                TestContext.Current.CancellationToken)).StatusCode);

        using var withoutCsrf = await administrator.PostAsJsonAsync(
            "/api/admin/background-work/pause",
            new { paused = true },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, withoutCsrf.StatusCode);

        var status = await administrator.GetFromJsonAsync<JsonElement>(
            "/api/admin/background-work/",
            TestContext.Current.CancellationToken);
        Assert.True(status.GetProperty("operationalAttention").GetBoolean());
        Assert.Equal(1, status.GetProperty("operationalAttentionCount").GetInt32());
        Assert.False(status.GetProperty("paused").GetBoolean());
        var issue = Assert.Single(status.GetProperty("issues").EnumerateArray());
        Assert.Equal("SourceAccess", issue.GetProperty("cause").GetString());
        Assert.Equal("OperationalBlocker", issue.GetProperty("severity").GetString());
        Assert.Equal("InstallationOperator", issue.GetProperty("remediationOwner").GetString());
        Assert.Contains("cannot be scanned", issue.GetProperty("summary").GetString());
        Assert.Contains(
            "/library/films",
            issue.GetProperty("operatorHandoff").GetString()!,
            StringComparison.Ordinal);
        Assert.Contains(
            "CheckAgain",
            issue.GetProperty("actions").EnumerateArray().Select(action => action.GetString()));

        var work = Assert.Single(status.GetProperty("work").EnumerateArray());
        Assert.Equal("LibraryScan", work.GetProperty("category").GetString());
        Assert.True(work.GetProperty("cancellable").GetBoolean());

        // A Library Scan reports concrete counts and phases rather than a fabricated percentage.
        Assert.Equal(JsonValueKind.Null, work.GetProperty("completedPercent").ValueKind);

        var paused = await PostAsync(
            administrator,
            csrf,
            "/api/admin/background-work/pause",
            new { paused = true });
        Assert.True(paused.GetProperty("paused").GetBoolean());

        var cancelled = await PostAsync(
            administrator,
            csrf,
            $"/api/admin/background-work/{fixture.WorkId}/cancel",
            new { });
        Assert.Equal("Accepted", cancelled.GetProperty("verdict").GetString());

        var stale = await PostAsync(
            administrator,
            csrf,
            $"/api/admin/background-work/issues/{fixture.IssueId}/actions",
            new { action = "CheckAgain", version = 99 });
        Assert.Equal("Stale", stale.GetProperty("verdict").GetString());

        var items = await administrator.GetFromJsonAsync<JsonElement[]>(
            $"/api/admin/background-work/issues/{fixture.IssueId}/items",
            TestContext.Current.CancellationToken);
        Assert.Empty(items!);
    }

    private static async Task<JsonElement> PostAsync(
        HttpClient client,
        string csrf,
        string path,
        object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("X-CSRF-Token", csrf);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
    }

    private static async Task<Fixture> AddIssueAsync(ViewerApplication application)
    {
        _ = application.Server;
        await using var scope = application.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var at = DateTime.SpecifyKind(new DateTime(2026, 8, 28), DateTimeKind.Utc);
        var directoryId = Guid.CreateVersion7();
        var workId = Guid.CreateVersion7();
        var issueId = Guid.CreateVersion7();
        database.LibraryDirectories.Add(new LibraryDirectoryRow
        {
            Id = directoryId,
            Name = "Films",
            ContainerPath = "/library/films",
            State = LibraryDirectoryState.Active,
            Health = LibraryDirectoryHealth.Unreachable,
            ConfigurationGeneration = 1,
            CreatedAt = at,
            ActivatedAt = at,
        });
        database.BackgroundWork.Add(new BackgroundWorkRow
        {
            Id = workId,
            LibraryScanId = workId,
            Category = BackgroundWorkCategory.LibraryScan,
            State = BackgroundWorkState.Queued,
            Trigger = BackgroundWorkTrigger.Activation,
            Phase = BackgroundWorkPhases.Queued,
            LibraryDirectoryId = directoryId,
            ConfigurationGeneration = 1,
            RequestedAt = at,
            UpdatedAt = at,
        });
        database.WorkIssues.Add(new WorkIssueRow
        {
            Id = issueId,
            Reference = "WI-A1B2C3D4E5F6",
            BackgroundWorkId = workId,
            Category = BackgroundWorkCategory.LibraryScan,
            LibraryDirectoryId = directoryId,
            ConfigurationGeneration = 1,
            Severity = WorkIssueSeverity.OperationalBlocker,
            Cause = WorkIssueCause.SourceAccess,
            RemediationOwner = RemediationOwner.InstallationOperator,
            RetryDisposition = WorkIssueRetryDisposition.RetriesExhausted,
            AggregationKey = "SourceAccess|LibraryScan|Films:root",
            AffectedScope = "Films",
            ContainerPath = "/library/films",
            Phase = BackgroundWorkPhases.Traversing,
            Summary = WorkIssueMessages.DirectoryCannotBeScanned("Films"),
            Detail = "The Library Scan could not observe the directory.",
            Impact = "Nothing in this Library Directory can be discovered.",
            RequiredAction = "Ask the Installation Operator to restore the mount.",
            ExpectedResolutionEvidence = "A scan that completes its traversal.",
            SafeCause = "The container is not permitted to read the path.",
            FirstOccurredAt = at,
            LastOccurredAt = at,
            CreatedAt = at,
        });
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new Fixture(workId, issueId);
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

    private sealed record Fixture(Guid WorkId, Guid IssueId);
}
