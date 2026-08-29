using Prdb.Viewer.Infrastructure.Access;

namespace Prdb.Viewer.Host.Access;

/// Proof that a state-changing request came from this application rather than from a page that
/// merely knows the browser carries the Session cookie.
///
/// The expected token is derived from the Session cookie on every request, so it needs neither a
/// database round trip nor stored state that one client could rotate out from under another. See
/// <see cref="CsrfToken"/> for why the derivation is the protection it looks like.
public sealed class CsrfEndpointFilter : IEndpointFilter
{
    public const string HeaderName = "X-CSRF-Token";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var presented = http.Request.Headers[HeaderName].ToString();
        http.Request.Cookies.TryGetValue(SessionAuthentication.CookieName, out var sessionToken);

        if (http.User.SessionId() is null ||
            http.User.AccountId() is null ||
            !CsrfToken.Matches(sessionToken, presented))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "The request could not be verified.",
                detail: "Refresh the page and try the action again.");
        }

        return await next(context);
    }
}

public static class CsrfEndpointConventionExtensions
{
    public static RouteHandlerBuilder RequireCsrf(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter<CsrfEndpointFilter>();
}
