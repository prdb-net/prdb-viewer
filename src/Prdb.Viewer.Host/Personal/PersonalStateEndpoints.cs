using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Host.Access;
using Prdb.Viewer.Host.Library;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Personal;

namespace Prdb.Viewer.Host.Personal;

public static class PersonalStateEndpoints
{
    public static void MapPersonalState(this IEndpointRouteBuilder routes)
    {
        var personal = routes.MapGroup("/api/personal").WithTags("Personal State");

        // The client's own qualification of this library's media configurations, and what it
        // observed when it played them. Both are Personal State scoped to the Account and the
        // client context the request speaks for.
        personal.MapGet("/playback-profiles", async (
            ClientPlaybackService service,
            HttpContext http,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.UnassessedProfilesAsync(
                http.User.AccountId()!.Value,
                http.ClientContextKey(),
                cancellationToken)));

        personal.MapPut("/playback-assessments", async (
            ClientPlaybackAssessmentsRequest request,
            ClientPlaybackService service,
            HttpContext http,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(new ClientPlaybackAssessmentsResponse(
                await service.RecordAssessmentsAsync(
                    http.User.AccountId()!.Value,
                    http.ClientContextKey(),
                    request.Assessments,
                    cancellationToken))))
            .RequireCsrf();

        personal.MapPost("/playback-outcomes", async (
            ObservedPlaybackOutcomeRequest request,
            ClientPlaybackService service,
            HttpContext http,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(new ObservedPlaybackOutcomeResponse(
                await service.RecordOutcomeAsync(
                    http.User.AccountId()!.Value,
                    http.ClientContextKey(),
                    request.VideoFileId,
                    request.Outcome,
                    request.FailureCategory,
                    cancellationToken))))
            .RequireCsrf();

        personal.MapDelete("/videos/{videoId:guid}/playback-outcomes", async (
            Guid videoId,
            ClientPlaybackService service,
            HttpContext http,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(new ObservedPlaybackOutcomeResponse(
                await service.ForgetOutcomesAsync(
                    http.User.AccountId()!.Value,
                    http.ClientContextKey(),
                    videoId,
                    cancellationToken) > 0)))
            .RequireCsrf();

        personal.MapPost("/videos/{videoId:guid}/playback-attempts", async (
            Guid videoId,
            PlaybackAttemptRequest request,
            PersonalStateService service,
            HttpContext http,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.StartPlaybackAttemptAsync(
                http.User.AccountId()!.Value,
                videoId,
                request.VideoFileId,
                cancellationToken)))
            .RequireCsrf();

        personal.MapPost("/playback-attempts/{playbackAttemptId:guid}/reports", async (
            Guid playbackAttemptId,
            PlaybackReportRequest request,
            PersonalStateService service,
            HttpContext http,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.ReportPlaybackAsync(
                http.User.AccountId()!.Value,
                playbackAttemptId,
                request.ReportId,
                request.Sequence,
                request.VideoFileId,
                request.PositionMilliseconds,
                request.ActiveWatchingMilliseconds,
                request.NaturalEndConfirmed,
                request.EndSession,
                cancellationToken)))
            .RequireCsrf();

        personal.MapPost("/playback-attempts/{playbackAttemptId:guid}/end", async (
            Guid playbackAttemptId,
            PersonalStateService service,
            HttpContext http,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(new EndPlaybackAttemptResponse(
                await service.EndPlaybackAttemptAsync(
                    http.User.AccountId()!.Value,
                    playbackAttemptId,
                    cancellationToken))))
            .RequireCsrf();

        personal.MapPut("/videos/{videoId:guid}/favourite", async (
            Guid videoId,
            PersonalStateService service,
            HttpContext http,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.SetFavouriteAsync(
                http.User.AccountId()!.Value,
                videoId,
                selected: true,
                cancellationToken)))
            .RequireCsrf();

        personal.MapDelete("/videos/{videoId:guid}/favourite", async (
            Guid videoId,
            PersonalStateService service,
            HttpContext http,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.SetFavouriteAsync(
                http.User.AccountId()!.Value,
                videoId,
                selected: false,
                cancellationToken)))
            .RequireCsrf();

        personal.MapPut("/videos/{videoId:guid}/watch-later", async (
            Guid videoId,
            PersonalStateService service,
            HttpContext http,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.SetWatchLaterAsync(
                http.User.AccountId()!.Value,
                videoId,
                selected: true,
                cancellationToken)))
            .RequireCsrf();

        personal.MapDelete("/videos/{videoId:guid}/watch-later", async (
            Guid videoId,
            PersonalStateService service,
            HttpContext http,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.SetWatchLaterAsync(
                http.User.AccountId()!.Value,
                videoId,
                selected: false,
                cancellationToken)))
            .RequireCsrf();

        personal.MapPut("/videos/{videoId:guid}/rating", async (
            Guid videoId,
            PersonalRatingRequest request,
            PersonalStateService service,
            HttpContext http,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.SetRatingAsync(
                http.User.AccountId()!.Value,
                videoId,
                request.Rating,
                cancellationToken)))
            .RequireCsrf();

        personal.MapDelete("/videos/{videoId:guid}/rating", async (
            Guid videoId,
            PersonalStateService service,
            HttpContext http,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.SetRatingAsync(
                http.User.AccountId()!.Value,
                videoId,
                rating: null,
                cancellationToken)))
            .RequireCsrf();

        personal.MapPost("/videos/{videoId:guid}/continue-watching/dismiss", async (
            Guid videoId,
            PersonalStateService service,
            HttpContext http,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.DismissContinueWatchingAsync(
                http.User.AccountId()!.Value,
                videoId,
                cancellationToken)))
            .RequireCsrf();
    }
}

public sealed record PlaybackAttemptRequest(Guid VideoFileId);

public sealed record ClientPlaybackAssessmentsRequest(
    IReadOnlyList<ClientPlaybackAssessmentReport> Assessments);

public sealed record ClientPlaybackAssessmentsResponse(int Recorded);

public sealed record ObservedPlaybackOutcomeRequest(
    Guid VideoFileId,
    ObservedPlaybackOutcome Outcome,
    PlaybackFailureCategory? FailureCategory);

public sealed record ObservedPlaybackOutcomeResponse(bool Recorded);

public sealed record PlaybackReportRequest(
    Guid ReportId,
    int Sequence,
    Guid VideoFileId,
    long PositionMilliseconds,
    long ActiveWatchingMilliseconds,
    bool NaturalEndConfirmed,
    bool EndSession);

public sealed record PersonalRatingRequest(int? Rating);

public sealed record EndPlaybackAttemptResponse(bool Ended);
