using System.Security.Claims;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

using Prdb.Viewer.Infrastructure.Access;

namespace Prdb.Viewer.Host.Access;

public static class SessionAuthentication
{
    public const string Scheme = "ViewerSession";
    public const string CookieName = "prdb_viewer_session";
    public const string SessionIdClaim = "viewer:sid";

    public static Guid? SessionId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(SessionIdClaim), out var id) ? id : null;

    public static Guid? AccountId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static void AppendSessionCookie(
        this HttpContext http,
        string token,
        DateTimeOffset expiresAt) =>
        http.Response.Cookies.Append(CookieName, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = http.Request.IsHttps,
            Path = "/",
            Expires = expiresAt,
        });

    public static void DeleteSessionCookie(this HttpContext http) =>
        http.Response.Cookies.Delete(CookieName, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = http.Request.IsHttps,
            Path = "/",
        });
}

public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    AccessService access) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Cookies.TryGetValue(SessionAuthentication.CookieName, out var token))
        {
            return AuthenticateResult.NoResult();
        }

        var session = await access.AuthenticateAsync(token, Context.RequestAborted);

        if (session is null)
        {
            Context.DeleteSessionCookie();
            return AuthenticateResult.NoResult();
        }

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, session.Account.Id.ToString()),
            new Claim(ClaimTypes.Name, session.Account.Username),
            new Claim(ClaimTypes.Role, session.Account.Authority.ToString()),
            new Claim(SessionAuthentication.SessionIdClaim, session.SessionId.ToString()),
        };

        if (session.Account.Email is not null)
        {
            claims.Add(new Claim(ClaimTypes.Email, session.Account.Email));
        }
        var identity = new ClaimsIdentity(claims, SessionAuthentication.Scheme);

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SessionAuthentication.Scheme));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        await Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Not signed in.",
                detail: "Sign in with an approved local Account to use this application.")
            .ExecuteAsync(Context);
    }
}
