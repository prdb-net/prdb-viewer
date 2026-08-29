using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Prdb.Viewer.Host.Access;

using Xunit;

namespace Prdb.Viewer.Host.Tests.Access;

public sealed class AccessRouteTests
{
    [Fact]
    public async Task Bootstrap_claim_signs_in_once_and_sign_out_requires_csrf()
    {
        using var application = new ViewerApplication();
        using var client = application.CreateClient();

        var initial = await client.GetFromJsonAsync<JsonElement>(
            "/api/access/state",
            TestContext.Current.CancellationToken);
        Assert.False(initial.GetProperty("claimed").GetBoolean());
        Assert.False(initial.GetProperty("signedIn").GetBoolean());

        var authorization = await application.CreateBootstrapAuthorizationAsync();
        using var claim = await client.PostAsJsonAsync(
            "/api/access/bootstrap",
            new
            {
                authorization,
                username = "administrator",
                password = "administrator password",
                email = "admin@example.test",
            },
            TestContext.Current.CancellationToken);
        var claimed = await claim.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);
        Assert.Equal("Created", claimed.GetProperty("verdict").GetString());
        var sessionCookie = claim.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains("HttpOnly", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SameSite=Strict", sessionCookie, StringComparison.OrdinalIgnoreCase);

        var account = claimed.GetProperty("account");
        Assert.Equal("administrator", account.GetProperty("username").GetString());
        Assert.Equal("Administrator", account.GetProperty("authority").GetString());

        using var unverifiedSignOut = await client.PostAsync(
            "/api/access/sign-out",
            content: null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, unverifiedSignOut.StatusCode);

        using var signOut = new HttpRequestMessage(HttpMethod.Post, "/api/access/sign-out");
        signOut.Headers.Add(CsrfEndpointFilter.HeaderName, account.GetProperty("csrfToken").GetString());
        using var signedOut = await client.SendAsync(signOut, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);

        using var afterSignOut = await client.GetAsync(
            "/api/access/me",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, afterSignOut.StatusCode);
    }

    /// A second client of the same Session — another tab, or a reload — asks who it is. That must
    /// not disturb the token the first one is already using, which a rotating token did: every
    /// state-changing request from the older tab was refused until it was reloaded.
    [Fact]
    public async Task Asking_who_you_are_leaves_an_existing_csrf_token_usable()
    {
        using var application = new ViewerApplication();
        using var client = application.CreateClient();

        var authorization = await application.CreateBootstrapAuthorizationAsync();
        using var claim = await client.PostAsJsonAsync(
            "/api/access/bootstrap",
            new
            {
                authorization,
                username = "administrator",
                password = "administrator password",
                email = "admin@example.test",
            },
            TestContext.Current.CancellationToken);
        var claimed = await claim.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        var issued = claimed.GetProperty("account").GetProperty("csrfToken").GetString();

        var first = await client.GetFromJsonAsync<JsonElement>(
            "/api/access/me",
            TestContext.Current.CancellationToken);
        var second = await client.GetFromJsonAsync<JsonElement>(
            "/api/access/me",
            TestContext.Current.CancellationToken);

        Assert.Equal(issued, first.GetProperty("csrfToken").GetString());
        Assert.Equal(issued, second.GetProperty("csrfToken").GetString());

        using var signOut = new HttpRequestMessage(HttpMethod.Post, "/api/access/sign-out");
        signOut.Headers.Add(CsrfEndpointFilter.HeaderName, issued);
        using var signedOut = await client.SendAsync(signOut, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);
    }

    /// The token belongs to one Session. Another Account's token must not verify a request made
    /// with this Session's cookie.
    [Fact]
    public async Task A_csrf_token_from_another_session_is_refused()
    {
        using var application = new ViewerApplication();
        using var administrator = application.CreateClient();
        using var other = application.CreateClient();

        var authorization = await application.CreateBootstrapAuthorizationAsync();
        using var claim = await administrator.PostAsJsonAsync(
            "/api/access/bootstrap",
            new
            {
                authorization,
                username = "administrator",
                password = "administrator password",
                email = "admin@example.test",
            },
            TestContext.Current.CancellationToken);
        var claimed = await claim.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);

        using var secondSignIn = await other.PostAsJsonAsync(
            "/api/access/sign-in",
            new { username = "administrator", password = "administrator password" },
            TestContext.Current.CancellationToken);
        var secondSession = await secondSignIn.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        var otherToken = secondSession.GetProperty("account").GetProperty("csrfToken").GetString();

        Assert.NotEqual(
            claimed.GetProperty("account").GetProperty("csrfToken").GetString(),
            otherToken);

        using var borrowed = new HttpRequestMessage(HttpMethod.Post, "/api/access/sign-out");
        borrowed.Headers.Add(CsrfEndpointFilter.HeaderName, otherToken);
        using var refused = await administrator.SendAsync(
            borrowed,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task Registration_requires_administrator_approval_and_users_cannot_administer_accounts()
    {
        using var application = new ViewerApplication();
        using var administrator = application.CreateClient();
        var authorization = await application.CreateBootstrapAuthorizationAsync();
        var adminAccount = await ClaimAsync(administrator, authorization);
        var csrf = adminAccount.GetProperty("csrfToken").GetString()!;

        using var registration = await administrator.PostAsJsonAsync(
            "/api/access/registration-requests",
            new
            {
                username = "second-user",
                password = "second user password",
                email = (string?)null,
            },
            TestContext.Current.CancellationToken);
        var registrationResult = await registration.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        Assert.Equal("Submitted", registrationResult.GetProperty("verdict").GetString());

        using var user = application.CreateClient();
        var pendingSignIn = await SignInAsync(user, "second-user", "second user password");
        Assert.Equal("ApprovalPending", pendingSignIn.GetProperty("verdict").GetString());

        var accounts = await administrator.GetFromJsonAsync<JsonElement>(
            "/api/admin/accounts/",
            TestContext.Current.CancellationToken);
        var applicant = accounts.EnumerateArray().Single(account =>
            account.GetProperty("username").GetString() == "second-user");

        using var approve = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/admin/accounts/{applicant.GetProperty("id").GetGuid()}/approve");
        approve.Headers.Add(CsrfEndpointFilter.HeaderName, csrf);
        using var approved = await administrator.SendAsync(
            approve,
            TestContext.Current.CancellationToken);
        var approval = await approved.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        Assert.Equal("Completed", approval.GetProperty("verdict").GetString());

        var signedIn = await SignInAsync(user, "second-user", "second user password");
        Assert.Equal("SignedIn", signedIn.GetProperty("verdict").GetString());

        using var forbidden = await user.GetAsync(
            "/api/admin/accounts/",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Competing_bootstrap_claims_create_only_one_Administrator()
    {
        using var application = new ViewerApplication();
        using var firstClient = application.CreateClient();
        using var secondClient = application.CreateClient();
        var authorization = await application.CreateBootstrapAuthorizationAsync();

        var claims = await Task.WhenAll(
            ClaimResponseAsync(firstClient, authorization, "first-admin"),
            ClaimResponseAsync(secondClient, authorization, "second-admin"));

        Assert.Single(claims, verdict => verdict == "Created");
        Assert.Single(claims, verdict => verdict == "AlreadyClaimed");
    }

    private static async Task<JsonElement> ClaimAsync(HttpClient client, string authorization)
    {
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
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        return body.GetProperty("account").Clone();
    }

    private static async Task<JsonElement> SignInAsync(
        HttpClient client,
        string username,
        string password)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/access/sign-in",
            new { username, password },
            TestContext.Current.CancellationToken);
        return await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
    }

    private static async Task<string> ClaimResponseAsync(
        HttpClient client,
        string authorization,
        string username)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/access/bootstrap",
            new
            {
                authorization,
                username,
                password = "administrator password",
                email = (string?)null,
            },
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);

        return body.GetProperty("verdict").GetString()!;
    }
}
