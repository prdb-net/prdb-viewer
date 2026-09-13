using Microsoft.AspNetCore.Http.HttpResults;

using Prdb.Viewer.Host.Access;
using Prdb.Viewer.Infrastructure.Personal;

namespace Prdb.Viewer.Host.Personal;

/// <summary>
/// One Account's Playlists. Every route takes the Account from the authenticated session and never
/// from the request, so there is no identifier a caller could put in one to reach somebody else's
/// filing — and an Administrator reaches these no more than anyone else does.
/// </summary>
public static class PlaylistEndpoints
{
    public static void MapPlaylists(this IEndpointRouteBuilder routes)
    {
        var playlists = routes.MapGroup("/api/personal/playlists").WithTags("Personal State");

        // A Video may be named, and then each Playlist says whether it is in there. That is what a
        // screen offering "add to a Playlist" needs, and asking each Playlist separately for it
        // would be one request per Playlist for one line of state.
        playlists.MapGet("", async (
            PlaylistService service,
            HttpContext http,
            CancellationToken cancellationToken,
            Guid? videoId = null) =>
            TypedResults.Ok(new PlaylistsResponse(
                await service.ListAsync(
                    http.User.AccountId()!.Value,
                    videoId,
                    cancellationToken))));

        playlists.MapPost("", async Task<Results<Ok<PlaylistResult>, BadRequest<PlaylistResult>>> (
            PlaylistNameRequest request,
            PlaylistService service,
            HttpContext http,
            CancellationToken cancellationToken) =>
            Answer(await service.CreateAsync(
                http.User.AccountId()!.Value,
                request.Name,
                cancellationToken)))
            .RequireCsrf();

        playlists.MapPut("/{playlistId:guid}", async Task<Results<Ok<PlaylistResult>, BadRequest<PlaylistResult>>> (
            Guid playlistId,
            PlaylistNameRequest request,
            PlaylistService service,
            HttpContext http,
            CancellationToken cancellationToken) =>
            Answer(await service.RenameAsync(
                http.User.AccountId()!.Value,
                playlistId,
                request.Name,
                cancellationToken)))
            .RequireCsrf();

        playlists.MapDelete("/{playlistId:guid}", async Task<Results<Ok<PlaylistDeletion>, NotFound>> (
            Guid playlistId,
            PlaylistService service,
            HttpContext http,
            CancellationToken cancellationToken) =>
            await service.DeleteAsync(http.User.AccountId()!.Value, playlistId, cancellationToken)
                == PlaylistVerdict.Updated
                ? TypedResults.Ok(new PlaylistDeletion(true))
                : TypedResults.NotFound())
            .RequireCsrf();

        playlists.MapPut("/{playlistId:guid}/videos/{videoId:guid}", async Task<Results<Ok<PlaylistResult>, BadRequest<PlaylistResult>>> (
            Guid playlistId,
            Guid videoId,
            PlaylistService service,
            HttpContext http,
            CancellationToken cancellationToken) =>
            Answer(await service.AddAsync(
                http.User.AccountId()!.Value,
                playlistId,
                videoId,
                cancellationToken)))
            .RequireCsrf();

        playlists.MapDelete("/{playlistId:guid}/videos/{videoId:guid}", async Task<Results<Ok<PlaylistResult>, BadRequest<PlaylistResult>>> (
            Guid playlistId,
            Guid videoId,
            PlaylistService service,
            HttpContext http,
            CancellationToken cancellationToken) =>
            Answer(await service.RemoveAsync(
                http.User.AccountId()!.Value,
                playlistId,
                videoId,
                cancellationToken)))
            .RequireCsrf();

        // The destination is a place in the whole Playlist, counted from zero. It is deliberately
        // not "before that other Video": read from a narrowed list that would quietly rearrange
        // the entries the caller could not see.
        playlists.MapPost("/{playlistId:guid}/videos/{videoId:guid}/position", async Task<Results<Ok<PlaylistResult>, BadRequest<PlaylistResult>>> (
            Guid playlistId,
            Guid videoId,
            PlaylistPositionRequest request,
            PlaylistService service,
            HttpContext http,
            CancellationToken cancellationToken) =>
            Answer(await service.MoveAsync(
                http.User.AccountId()!.Value,
                playlistId,
                videoId,
                request.Position,
                cancellationToken)))
            .RequireCsrf();
    }

    /// <summary>
    /// A Playlist somebody else owns answers exactly as one that does not exist: the verdict says
    /// not found, and nothing in the answer distinguishes the two.
    /// </summary>
    private static Results<Ok<PlaylistResult>, BadRequest<PlaylistResult>> Answer(
        PlaylistResult result) =>
        result.Verdict == PlaylistVerdict.Updated
            ? TypedResults.Ok(result)
            : TypedResults.BadRequest(result);
}

public sealed record PlaylistsResponse(IReadOnlyList<PlaylistSummary> Playlists);

public sealed record PlaylistNameRequest(string? Name);

public sealed record PlaylistPositionRequest(int Position);

public sealed record PlaylistDeletion(bool Deleted);
