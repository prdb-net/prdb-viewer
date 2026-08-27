using Prdb.Viewer.Core.Access;
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

        work.MapPost("/library-directories/{libraryDirectoryId:guid}/scans", async (
            Guid libraryDirectoryId,
            LibraryWorkScheduler scheduler,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await scheduler.QueueScanAsync(
                libraryDirectoryId,
                cancellationToken)))
            .RequireCsrf();
    }
}
