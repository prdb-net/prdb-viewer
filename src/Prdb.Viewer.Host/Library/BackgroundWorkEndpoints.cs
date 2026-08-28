using Microsoft.AspNetCore.Http.HttpResults;

using Prdb.Viewer.Core.Access;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Host.Access;
using Prdb.Viewer.Infrastructure.Library;

namespace Prdb.Viewer.Host.Library;

public static class BackgroundWorkEndpoints
{
    public static void MapBackgroundWork(this IEndpointRouteBuilder routes)
    {
        var work = routes.MapGroup("/api/admin/background-work")
            .WithTags("Background Work")
            .RequireAuthorization(policy =>
                policy.RequireRole(AccountAuthority.Administrator.ToString()));

        work.MapGet("/", async (
            BackgroundWorkQuery query,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await query.GetAsync(cancellationToken)));

        work.MapGet("/issues/{workIssueId:guid}/items", async Task<Results<
            Ok<IReadOnlyList<WorkIssueAffectedItem>>,
            NotFound>> (
            Guid workIssueId,
            BackgroundWorkQuery query,
            CancellationToken cancellationToken) =>
            await query.GetAffectedItemsAsync(workIssueId, cancellationToken) is { } items
                ? TypedResults.Ok(items)
                : TypedResults.NotFound());

        work.MapPost("/issues/{workIssueId:guid}/actions", async (
            Guid workIssueId,
            WorkIssueActionRequest request,
            BackgroundWorkOperations operations,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await operations.AdvanceIssueAsync(
                workIssueId,
                request.Version,
                request.Action,
                cancellationToken)))
            .RequireCsrf();

        work.MapPost("/pause", async (
            BackgroundWorkPauseRequest request,
            BackgroundWorkOperations operations,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await operations.SetPausedAsync(request.Paused, cancellationToken)))
            .RequireCsrf();

        work.MapPost("/{workId:guid}/cancel", async (
            Guid workId,
            BackgroundWorkOperations operations,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await operations.CancelAsync(workId, cancellationToken)))
            .RequireCsrf();

        work.MapPost("/library-directories/{libraryDirectoryId:guid}/scans", async (
            Guid libraryDirectoryId,
            LibraryWorkScheduler scheduler,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await scheduler.QueueScanAsync(
                libraryDirectoryId,
                BackgroundWorkTrigger.Administrator,
                cancellationToken)))
            .RequireCsrf();
    }
}

/// <summary>
/// The Work Issue version an Administrator was shown. Binding the action to it is what stops a
/// stale `Retry now` from committing after someone else already changed the situation.
/// </summary>
public sealed record WorkIssueActionRequest(WorkIssueAction Action, int Version);

public sealed record BackgroundWorkPauseRequest(bool Paused);
