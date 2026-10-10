using System.Net;

using Prdb.Sdk;

using Prdb.Viewer.Infrastructure.Configuration;

namespace Prdb.Viewer.Infrastructure.Library;

public enum SiteDirectoryFetchStatus
{
    Fetched,
    Rejected,
    Unavailable,
}

/// <summary>What one attempt to refresh the retained Site Directory produced.</summary>
public sealed record SiteDirectoryFetchResult(
    SiteDirectoryFetchStatus Status,
    IReadOnlyList<RemoteSite> Sites,
    string? Detail = null,
    CatalogueCursor? Cursor = null,
    bool HasMore = false,
    IReadOnlyList<string>? DeletedIds = null,
    TimeSpan? RetryAfter = null);

public interface IPrdbSiteDirectoryClient
{
    Task<SiteDirectoryFetchResult> FetchPageAsync(string credential, CatalogueCursor cursor,
        CancellationToken cancellationToken = default) => FetchAsync(credential, cancellationToken);

    Task<SiteDirectoryFetchResult> FetchAsync(
        string credential,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads the published list of sites through the documented public prdb API. It sends nothing
/// about the local library — the request carries only the installation credential — and it asks at
/// most once a day, because a site list is a slowly changing vocabulary rather than a lookup.
/// </summary>
public sealed class PrdbSiteDirectoryClient(
    IHttpMessageHandlerFactory handlers,
    PrdbEndpoint? endpoint = null)
    : IPrdbSiteDirectoryClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    public Task<SiteDirectoryFetchResult> FetchAsync(string credential, CancellationToken cancellationToken = default) =>
        FetchPageAsync(credential, CatalogueCursor.Baseline, cancellationToken);

    public async Task<SiteDirectoryFetchResult> FetchPageAsync(
        string credential, CatalogueCursor cursor, CancellationToken cancellationToken = default)
    {
        var origin = (endpoint ?? new PrdbEndpoint()).BaseUrl;
        var status = new ResponseStatusOption();
        var limits = new RateLimitOption();
        using var guidance = new CatalogueResponseGuidance(handlers.CreateHandler(PrdbConnectionVerifier.TransportName));
        var client = PrdbClientFactory.Create(credential, origin, transport: guidance,
            retry: PrdbRetryOptions.Disabled, timeout: RequestTimeout);
        try
        {
            var response = await client.Sites.Changes.WithUrl(cursor.Url(origin, "sites")).GetAsync(
                request => { request.Options.Add(status); request.Options.Add(limits); }, cancellationToken);
            if (response?.Items is null || response.HasMore is null || response.ServerTimeUtc is null ||
                (response.Items.Count > 0 && (response.NextCursor?.UpdatedAtUtc is null || response.NextCursor.Id is null)) ||
                (response.Items.Count == 0 && response.HasMore == true))
                return new(SiteDirectoryFetchStatus.Unavailable, [], "prdb returned an incomplete Site feed page.");
            var sites = new List<RemoteSite>();
            var deleted = new List<string>();
            foreach (var item in response.Items)
            {
                var site = item.Site;
                if (site?.Id is not { } id || site.IsDeleted is null || site.UpdatedAtUtc is null ||
                    item.EventType is not ("created" or "updated" or "deleted"))
                    return new(SiteDirectoryFetchStatus.Unavailable, [], "prdb returned an invalid Site feed item.");
                if ((site.IsDeleted == true) != (item.EventType == "deleted") ||
                    (site.IsDeleted == true && site.DeletedAtUtc is null))
                    return new(SiteDirectoryFetchStatus.Unavailable, [], "prdb returned an inconsistent Site tombstone.");
                if (site.IsDeleted == true || item.EventType == "deleted") { deleted.Add(id.ToString()); continue; }
                // A titleless live row cannot supply vocabulary, but its valid row cursor still advances.
                if (string.IsNullOrWhiteSpace(site.Title)) continue;
                sites.Add(new RemoteSite(id.ToString(), site.Title, site.Url)
                {
                    PlatformId = site.PlatformId?.ToString(),
                    PlatformKey = site.PlatformKey,
                    PlatformTitle = site.PlatformTitle,
                    AccountHandle = site.AccountHandle,
                    Links = (site.Links ?? []).Where(link => RemoteWorkFacts.SafeUrl(link.Url) is not null)
                        .Select(link => new RemoteSiteLink(link.Label, RemoteWorkFacts.SafeUrl(link.Url)!)).ToArray(),
                });
            }
            var next = response.Items.Count > 0
                ? CatalogueCursor.At(response.NextCursor!.UpdatedAtUtc!.Value, response.NextCursor.Id)
                : CatalogueCursor.At(response.ServerTimeUtc.Value, null);
            // Old/future cursors must not move backwards while the settled bound catches up.
            if (DateTimeOffset.Parse(next.Since) < DateTimeOffset.Parse(cursor.Since)) next = cursor;
            if (response.HasMore == true && next == cursor)
                return new(SiteDirectoryFetchStatus.Unavailable, [], "The Site feed did not advance its cursor.");
            return new(SiteDirectoryFetchStatus.Fetched, sites, Cursor: next, HasMore: response.HasMore.Value,
                DeletedIds: deleted, RetryAfter: Deferred(limits));
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return status.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? new(SiteDirectoryFetchStatus.Rejected, [], "prdb refused the installation credential.")
                : new(SiteDirectoryFetchStatus.Unavailable, [], $"prdb could not be reached ({(int?)status.StatusCode ?? 0}).",
                    RetryAfter: guidance.RetryAfter ?? Deferred(limits));
        }
    }

    internal static TimeSpan? Deferred(RateLimitOption limits) =>
        new[] { limits.Hour, limits.Month }.OfType<RateLimitWindow>().Where(window => window.Remaining <= 0)
            .Select(window => (TimeSpan?)TimeSpan.FromSeconds(Math.Max(1, window.ResetInSeconds))).Max();
}
