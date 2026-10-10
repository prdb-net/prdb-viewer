using System.Net;

using Prdb.Sdk;
using Prdb.Sdk.Generated.Models;
using Prdb.Viewer.Infrastructure.Configuration;

namespace Prdb.Viewer.Infrastructure.Library;

public sealed record RemoteWorkChange(string Id, RemoteWork? Work, bool IsDeleted,
    DateTimeOffset UpdatedAt, DateTimeOffset? DeletedAt, string? MergedIntoId);

public sealed record VideoFeedFetchResult(WorkDetailFetchStatus Status, IReadOnlyList<RemoteWorkChange> Changes,
    CatalogueCursor? Cursor = null, bool HasMore = false, DateTimeOffset? ServerTime = null,
    string? Detail = null, TimeSpan? RetryAfter = null);

public interface IPrdbVideoFeedClient
{
    Task<VideoFeedFetchResult> FetchAsync(string credential, CatalogueCursor cursor, CancellationToken cancellationToken = default);
}

public sealed class PrdbVideoFeedClient(IHttpMessageHandlerFactory handlers, PrdbEndpoint? endpoint = null) : IPrdbVideoFeedClient
{
    public async Task<VideoFeedFetchResult> FetchAsync(string credential, CatalogueCursor cursor, CancellationToken cancellationToken = default)
    {
        var origin = (endpoint ?? new PrdbEndpoint()).BaseUrl;
        var status = new ResponseStatusOption();
        var limits = new RateLimitOption();
        using var guidance = new CatalogueResponseGuidance(handlers.CreateHandler(PrdbConnectionVerifier.TransportName));
        var client = PrdbClientFactory.Create(credential, origin, transport: guidance,
            retry: PrdbRetryOptions.Disabled, timeout: TimeSpan.FromSeconds(60));
        try
        {
            var response = await client.Videos.Changes.WithUrl(cursor.Url(origin, "videos")).GetAsync(
                request => { request.Options.Add(status); request.Options.Add(limits); }, cancellationToken);
            if (response?.Items is null || response.HasMore is null || response.ServerTimeUtc is null ||
                (response.Items.Count > 0 && (response.NextCursor?.UpdatedAtUtc is null || response.NextCursor.Id is null)) ||
                (response.Items.Count == 0 && response.HasMore == true))
                return new(WorkDetailFetchStatus.Unavailable, [], Detail: "prdb returned an incomplete Video feed page.");
            var changes = new List<RemoteWorkChange>();
            foreach (var item in response.Items)
            {
                var video = item.Video;
                if (video?.Id is not { } id || video.IsDeleted is null || video.UpdatedAtUtc is null ||
                    item.EventType is not ("created" or "updated" or "deleted") ||
                    (video.IsDeleted == true) != (item.EventType == "deleted") ||
                    (video.IsDeleted == true && video.DeletedAtUtc is null))
                    return new(WorkDetailFetchStatus.Unavailable, [], Detail: "prdb returned an invalid Video feed item.");
                // Tombstones carry no replacement content. They never enter the work-facts mapper.
                var work = video.IsDeleted == true ? null : RemoteWorkFacts.Of(video);
                changes.Add(new(id.ToString(), work, video.IsDeleted.Value,
                    video.UpdatedAtUtc.Value, video.DeletedAtUtc, video.MergedIntoId?.ToString()));
            }
            var next = response.Items.Count > 0
                ? CatalogueCursor.At(response.NextCursor!.UpdatedAtUtc!.Value, response.NextCursor.Id)
                : CatalogueCursor.At(response.ServerTimeUtc.Value, null);
            if (DateTimeOffset.Parse(next.Since) < DateTimeOffset.Parse(cursor.Since)) next = cursor;
            if (response.HasMore == true && next == cursor)
                return new(WorkDetailFetchStatus.Unavailable, [], Detail: "The Video feed did not advance its cursor.");
            return new(WorkDetailFetchStatus.Fetched, changes, next, response.HasMore.Value,
                response.ServerTimeUtc, RetryAfter: PrdbSiteDirectoryClient.Deferred(limits));
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return status.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? new(WorkDetailFetchStatus.Rejected, [], Detail: "prdb refused the installation credential.")
                : new(WorkDetailFetchStatus.Unavailable, [], Detail: $"prdb could not be reached ({(int?)status.StatusCode ?? 0}).",
                    RetryAfter: guidance.RetryAfter ?? PrdbSiteDirectoryClient.Deferred(limits));
        }
    }
}
