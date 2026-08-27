using Prdb.Viewer.Infrastructure.Access;

namespace Prdb.Viewer.Host.Access;

public sealed class CsrfEndpointFilter : IEndpointFilter
{
    public const string HeaderName = "X-CSRF-Token";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var sessionId = http.User.SessionId();
        var accountId = http.User.AccountId();
        var token = http.Request.Headers[HeaderName].ToString();

        if (sessionId is null ||
            accountId is null ||
            !await http.RequestServices
                .GetRequiredService<AccessService>()
                .ValidateCsrfTokenAsync(sessionId.Value, accountId.Value, token, http.RequestAborted))
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
