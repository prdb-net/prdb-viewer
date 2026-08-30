using System.Net;

using Prdb.Sdk;

namespace Prdb.Viewer.Infrastructure.Configuration;

public sealed class PrdbConnectionVerifier(
    IHttpMessageHandlerFactory handlers,
    PrdbEndpoint? endpoint = null) : IPrdbConnectionVerifier
{
    public const string TransportName = "prdb-credentialed";
    private static readonly TimeSpan VerificationTimeout = TimeSpan.FromSeconds(10);

    public async Task<PrdbVerificationOutcome> VerifyAsync(
        string credential,
        CancellationToken cancellationToken = default)
    {
        var status = new ResponseStatusOption();
        var client = PrdbClientFactory.Create(
            credential,
            (endpoint ?? new PrdbEndpoint()).BaseUrl,
            transport: handlers.CreateHandler(TransportName),
            retry: PrdbRetryOptions.Disabled,
            timeout: VerificationTimeout);

        try
        {
            var response = await client.RateLimit.GetAsync(
                request => request.Options.Add(status),
                cancellationToken);

            // The window is what makes this an answer from prdb rather than merely an answer. A
            // 200 carrying JSON with none of the documented fields — a proxy, a gateway, an
            // endpoint that has moved — deserialises into an object with nothing in it, and
            // treating that as proof would report a credential as checked that was never checked
            // against anything.
            return status.StatusCode == HttpStatusCode.OK && response?.Hourly is not null
                ? PrdbVerificationOutcome.Verified
                : PrdbVerificationOutcome.Unavailable;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return PrdbVerificationOutcome.Unavailable;
        }
        catch (Exception) when (status.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return PrdbVerificationOutcome.Rejected;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return PrdbVerificationOutcome.Unavailable;
        }
    }
}
