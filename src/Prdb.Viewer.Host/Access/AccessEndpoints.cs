using Microsoft.AspNetCore.Http.HttpResults;

using Prdb.Viewer.Core.Access;
using Prdb.Viewer.Infrastructure.Access;
using Prdb.Viewer.Infrastructure.Personal;

namespace Prdb.Viewer.Host.Access;

public static class AccessEndpoints
{
    public static void MapAccess(this IEndpointRouteBuilder routes)
    {
        var access = routes.MapGroup("/api/access").WithTags("Access");

        access.MapGet("/state", async (
            AccessService service,
            HttpContext http,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(new AccessStateResponse(
                await service.IsClaimedAsync(cancellationToken),
                http.User.Identity?.IsAuthenticated == true)))
            .AllowAnonymous();

        access.MapPost("/bootstrap", async (
            BootstrapRequest request,
            AccessService service,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ClaimAsync(
                request.Authorization ?? string.Empty,
                request.Username ?? string.Empty,
                request.Password ?? string.Empty,
                request.Email,
                cancellationToken);

            if (result.Session is not null)
            {
                http.AppendSessionCookie(result.Session.SessionToken, result.Session.ExpiresAt);
            }

            return TypedResults.Ok(new BootstrapResponse(
                result.Verdict,
                result.Session is null ? null : SignedIn(result.Session)));
        }).AllowAnonymous().RequireRateLimiting("anonymous-access");

        access.MapPost("/sign-in", async (
            SignInRequest request,
            AccessService service,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var result = await service.SignInAsync(
                request.Username ?? string.Empty,
                request.Password ?? string.Empty,
                cancellationToken);

            if (result.Session is not null)
            {
                http.AppendSessionCookie(result.Session.SessionToken, result.Session.ExpiresAt);
            }

            return TypedResults.Ok(new SignInResponse(
                result.Verdict,
                result.Session is null ? null : SignedIn(result.Session)));
        }).AllowAnonymous().RequireRateLimiting("anonymous-access");

        access.MapPost("/registration-requests", async (
            RegistrationRequest request,
            AccessService service,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(new RegistrationRequestResponse(
                await service.SubmitRegistrationRequestAsync(
                    request.Username ?? string.Empty,
                    request.Password ?? string.Empty,
                    request.Email,
                    cancellationToken))))
            .AllowAnonymous()
            .RequireRateLimiting("anonymous-access");

        access.MapPost("/recover", async (
            RecoverRequest request,
            AccessService service,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(new RecoverResponse(
                await service.RecoverAsync(
                    request.Username ?? string.Empty,
                    request.RecoveryCode ?? string.Empty,
                    request.NewPassword ?? string.Empty,
                    cancellationToken))))
            .AllowAnonymous()
            .RequireRateLimiting("anonymous-access");

        access.MapGet("/me", CurrentAccount);

        access.MapPost("/sign-out", async (
            AccessService service,
            PersonalStateService personalState,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            await personalState.EndAccountPlaybackAttemptsAsync(
                http.User.AccountId()!.Value,
                cancellationToken);
            await service.SignOutAsync(
                http.User.SessionId()!.Value,
                http.User.AccountId()!.Value,
                cancellationToken);
            http.DeleteSessionCookie();
            return TypedResults.NoContent();
        }).RequireCsrf();

        var accounts = routes.MapGroup("/api/admin/accounts")
            .WithTags("Accounts")
            .RequireAuthorization(policy => policy.RequireRole(AccountAuthority.Administrator.ToString()));

        accounts.MapGet("/", async (
            AccessService service,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.ListAccountsAsync(cancellationToken)));

        accounts.MapPost("/{accountId:guid}/approve", async (
            Guid accountId,
            AccessService service,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(new AccountActionResponse(
                await service.ApproveAsync(accountId, cancellationToken))))
            .RequireCsrf();

        accounts.MapPost("/{accountId:guid}/reinstate", async (
            Guid accountId,
            AccessService service,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(new AccountActionResponse(
                await service.ReinstateAsync(accountId, cancellationToken))))
            .RequireCsrf();

        accounts.MapPost("/{accountId:guid}/disable", async (
            Guid accountId,
            AccessService service,
            PersonalStateService personalState,
            CancellationToken cancellationToken) =>
        {
            var verdict = await service.DisableAsync(accountId, cancellationToken);
            if (verdict == AccountActionVerdict.Completed)
            {
                await personalState.EndAccountPlaybackAttemptsAsync(accountId, cancellationToken);
            }

            return TypedResults.Ok(new AccountActionResponse(verdict));
        })
            .RequireCsrf();

        accounts.MapPost("/{accountId:guid}/recovery-code", async (
            Guid accountId,
            AccessService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.IssueRecoveryCodeAsync(accountId, cancellationToken);
            return TypedResults.Ok(new RecoveryCodeResponse(
                result.Verdict,
                result.RecoveryCode,
                result.ExpiresAt));
        }).RequireCsrf();
    }

    private static SignedInAccountResponse SignedIn(SessionGrant grant) =>
        new(
            grant.Account.Id,
            grant.Account.Username,
            grant.Account.Email,
            grant.Account.Authority,
            grant.CsrfToken);

    /// Asking who you are is a question, not a change. It reports the Session's CSRF token rather
    /// than issuing a new one, so a second tab — or a reload — leaves the token every other client
    /// of the same Session is already using intact.
    private static Results<Ok<SignedInAccountResponse>, UnauthorizedHttpResult> CurrentAccount(
        HttpContext http)
    {
        if (!http.Request.Cookies.TryGetValue(SessionAuthentication.CookieName, out var sessionToken) ||
            string.IsNullOrEmpty(sessionToken))
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(new SignedInAccountResponse(
            http.User.AccountId()!.Value,
            http.User.Identity!.Name!,
            http.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value,
            Enum.Parse<AccountAuthority>(http.User.FindFirst(System.Security.Claims.ClaimTypes.Role)!.Value),
            CsrfToken.For(sessionToken)));
    }
}

public sealed record AccessStateResponse(bool Claimed, bool SignedIn);

public sealed record BootstrapRequest(
    string? Authorization,
    string? Username,
    string? Password,
    string? Email);

public sealed record BootstrapResponse(
    BootstrapClaimVerdict Verdict,
    SignedInAccountResponse? Account);

public sealed record SignInRequest(string? Username, string? Password);

public sealed record SignInResponse(SignInVerdict Verdict, SignedInAccountResponse? Account);

public sealed record RegistrationRequest(string? Username, string? Password, string? Email);

public sealed record RegistrationRequestResponse(RegistrationRequestVerdict Verdict);

public sealed record RecoverRequest(string? Username, string? RecoveryCode, string? NewPassword);

public sealed record RecoverResponse(RecoveryVerdict Verdict);

public sealed record SignedInAccountResponse(
    Guid Id,
    string Username,
    string? Email,
    AccountAuthority Authority,
    string CsrfToken);

public sealed record AccountActionResponse(AccountActionVerdict Verdict);

public sealed record RecoveryCodeResponse(
    AccountActionVerdict Verdict,
    string? RecoveryCode,
    DateTimeOffset? ExpiresAt);
