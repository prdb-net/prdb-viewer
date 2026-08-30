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
    string? Detail = null);

public interface IPrdbSiteDirectoryClient
{
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
    private const int PageSize = 1_000;

    /// <summary>
    /// A ceiling on how much of a growing catalogue one refresh reads. It exists so a surprising
    /// answer cannot turn a daily refresh into an unbounded loop, not because more sites than this
    /// are expected.
    /// </summary>
    private const int PageLimit = 20;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    public async Task<SiteDirectoryFetchResult> FetchAsync(
        string credential,
        CancellationToken cancellationToken = default)
    {
        var status = new ResponseStatusOption();
        var client = PrdbClientFactory.Create(
            credential,
            (endpoint ?? new PrdbEndpoint()).BaseUrl,
            transport: handlers.CreateHandler(PrdbConnectionVerifier.TransportName),
            retry: PrdbRetryOptions.Disabled,
            timeout: RequestTimeout);
        var sites = new List<RemoteSite>();

        try
        {
            for (var page = 1; page <= PageLimit; page++)
            {
                var current = page;
                var response = await client.Sites.GetAsync(
                    request =>
                    {
                        request.QueryParameters.Page = current;
                        request.QueryParameters.PageSize = PageSize;
                        request.Options.Add(status);
                    },
                    cancellationToken);
                var items = response?.Items ?? [];

                foreach (var item in items)
                {
                    if (item.Id is { } id && !string.IsNullOrWhiteSpace(item.Title))
                    {
                        sites.Add(new RemoteSite(id.ToString(), item.Title, item.Url));
                    }
                }

                if (items.Count < PageSize)
                {
                    break;
                }
            }

            return new SiteDirectoryFetchResult(SiteDirectoryFetchStatus.Fetched, sites);
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
        {
            return status.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? new SiteDirectoryFetchResult(
                    SiteDirectoryFetchStatus.Rejected,
                    [],
                    "prdb refused the installation credential.")
                : new SiteDirectoryFetchResult(
                    SiteDirectoryFetchStatus.Unavailable,
                    [],
                    $"prdb could not be reached ({(int?)status.StatusCode ?? 0}).");
        }
    }
}
