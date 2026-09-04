using System.Net;

using Prdb.Sdk;
using Prdb.Sdk.Generated.Models;

using Prdb.Viewer.Infrastructure.Configuration;

namespace Prdb.Viewer.Infrastructure.Library;

public enum WorkDetailFetchStatus
{
    Fetched,
    Rejected,
    Unavailable,
}

/// <summary>What one attempt to ask prdb about established works produced.</summary>
public sealed record WorkDetailFetchResult(
    WorkDetailFetchStatus Status,
    IReadOnlyList<RemoteWork> Works,
    string? Detail = null);

public interface IPrdbWorkDetailClient
{
    /// <summary>How many works one request may ask about, as the documented API allows.</summary>
    int BatchLimit => 50;

    Task<WorkDetailFetchResult> FetchAsync(
        string credential,
        IReadOnlyList<string> prdbVideoIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Asks the documented public prdb API about works this library has already established, through
/// the official SDK. It sends nothing about the local library beyond the prdb identifiers the
/// library already holds, and it costs no hashing and no matching: it is the question "what do you
/// say about this work now", not "what is this file".
/// </summary>
public sealed class PrdbWorkDetailClient(
    IHttpMessageHandlerFactory handlers,
    PrdbEndpoint? endpoint = null)
    : IPrdbWorkDetailClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    public async Task<WorkDetailFetchResult> FetchAsync(
        string credential,
        IReadOnlyList<string> prdbVideoIds,
        CancellationToken cancellationToken = default)
    {
        var wanted = prdbVideoIds
            .Select(id => Guid.TryParse(id, out var parsed) ? parsed : (Guid?)null)
            .OfType<Guid>()
            .Distinct()
            .Select(id => (Guid?)id)
            .ToList();

        if (wanted.Count == 0)
        {
            return new WorkDetailFetchResult(WorkDetailFetchStatus.Fetched, []);
        }

        var status = new ResponseStatusOption();
        var client = PrdbClientFactory.Create(
            credential,
            (endpoint ?? new PrdbEndpoint()).BaseUrl,
            transport: handlers.CreateHandler(PrdbConnectionVerifier.TransportName),
            retry: PrdbRetryOptions.Disabled,
            timeout: RequestTimeout);

        try
        {
            var response = await client.Videos.Batch.PostAsync(
                new GetVideosByIdsRequest { Ids = wanted },
                configuration => configuration.Options.Add(status),
                cancellationToken);

            return new WorkDetailFetchResult(
                WorkDetailFetchStatus.Fetched,
                (response ?? [])
                    .Select(RemoteWorkFacts.Of)
                    .OfType<RemoteWork>()
                    .ToArray());
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
        {
            return status.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? new WorkDetailFetchResult(
                    WorkDetailFetchStatus.Rejected,
                    [],
                    "prdb refused the installation credential.")
                : new WorkDetailFetchResult(
                    WorkDetailFetchStatus.Unavailable,
                    [],
                    $"prdb could not be reached ({(int?)status.StatusCode ?? 0}).");
        }
    }
}
