using System.Net;

using Prdb.Sdk;

namespace Prdb.Viewer.Infrastructure.Configuration;

public sealed class PrdbConnectionVerifier(IHttpMessageHandlerFactory handlers) : IPrdbConnectionVerifier
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
            transport: handlers.CreateHandler(TransportName),
            retry: PrdbRetryOptions.Disabled,
            timeout: VerificationTimeout);

        try
        {
            var response = await client.RateLimit.GetAsync(
                request => request.Options.Add(status),
                cancellationToken);

            return status.StatusCode == HttpStatusCode.OK && response is not null
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
