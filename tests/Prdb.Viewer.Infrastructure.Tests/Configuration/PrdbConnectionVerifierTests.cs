using System.Net;
using System.Net.Http.Headers;

using Prdb.Viewer.Infrastructure.Configuration;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Configuration;

public sealed class PrdbConnectionVerifierTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK, PrdbVerificationOutcome.Verified)]
    [InlineData(HttpStatusCode.Unauthorized, PrdbVerificationOutcome.Rejected)]
    [InlineData(HttpStatusCode.Forbidden, PrdbVerificationOutcome.Rejected)]
    [InlineData(HttpStatusCode.ServiceUnavailable, PrdbVerificationOutcome.Unavailable)]
    public async Task Verification_uses_one_credentialed_sdk_request(
        HttpStatusCode status,
        PrdbVerificationOutcome expected)
    {
        var transport = new RecordingHandler(status);
        var userAgent = new ProductUserAgentHandler { InnerHandler = transport };
        var verifier = new PrdbConnectionVerifier(new SingleHandlerFactory(userAgent));

        Assert.Equal(
            expected,
            await verifier.VerifyAsync(
                "test-api-key",
                TestContext.Current.CancellationToken));

        var request = Assert.Single(transport.Requests);
        Assert.Equal(new Uri("https://api.prdb.net/rate-limit"), request.Uri);
        Assert.Equal("test-api-key", request.ApiKey);
        Assert.Contains(request.UserAgent, product => product.Product?.Name == "prdb-viewer");
    }

    private sealed class SingleHandlerFactory(HttpMessageHandler handler) : IHttpMessageHandlerFactory
    {
        public HttpMessageHandler CreateHandler(string name)
        {
            Assert.Equal(PrdbConnectionVerifier.TransportName, name);
            return handler;
        }
    }

    private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.RequestUri!,
                request.Headers.GetValues("X-Api-Key").Single(),
                [.. request.Headers.UserAgent]));
            // The whole of the documented answer, not a corner of it. `isEnforced`, `hourly` and
            // `monthly` are all required, and a reply short of them is one the verifier now
            // declines to read as proof — a body that answers 200 and says nothing is what a
            // proxy or a moved endpoint produces, not what prdb produces.
            const string window =
                "{\"limit\":1000,\"used\":1,\"remaining\":999,\"resetsInSeconds\":600}";
            var body = status == HttpStatusCode.OK
                ? $"{{\"isEnforced\":true,\"hourly\":{window},\"monthly\":{window}}}"
                : $"{{\"status\":{(int)status},\"title\":\"Request refused\"}}";

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, MediaTypeHeaderValue.Parse("application/json")),
                RequestMessage = request,
            });
        }
    }

    private sealed record RecordedRequest(
        Uri Uri,
        string ApiKey,
        IReadOnlyList<ProductInfoHeaderValue> UserAgent);
}
