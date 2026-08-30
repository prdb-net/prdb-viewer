using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

/// <summary>
/// Answers prdb's three endpoints in place of the network, so the client that talks to them can be
/// tested as it actually ships.
///
/// Every other suite replaces <c>IPrdbIdentificationClient</c> wholesale, which is convenient and
/// tests nothing below it: not how the SDK serialises a request, not how a reply maps onto the
/// records the rest of the code reads, and not one of the failures the API documents. Those are
/// exactly the paths that cannot be exercised against the real service — nobody can ask prdb for a
/// 503 — and they are the ones that decide what an Administrator is told when something is wrong.
///
/// It is a message handler rather than a server on a port: no certificate, no port to collide, no
/// waiting for a socket. Everything above the socket is real, including the status codes and the
/// JSON, and the base url stays what it is in production because nothing ever leaves the process.
///
/// The shapes come from the SDK's own OpenAPI document. Two of them are worth stating, because
/// guessing them wrong is how a fake ends up testing itself: `confidence` and `matchedBy` are
/// integers on the wire, not names, and `ref` is echoed back exactly as it was sent.
/// </summary>
internal sealed class FakePrdb : HttpMessageHandler
{
    private readonly Dictionary<string, Match> matches = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every request this answered, so a test can assert on what was actually sent.</summary>
    public List<JsonNode> IdentifyRequests { get; } = [];

    /// <summary>When set, every endpoint answers with this instead, the way an outage does.</summary>
    public HttpStatusCode? Failure { get; set; }

    /// <summary>When set, the body is returned as-is, however malformed.</summary>
    public string? RawBody { get; set; }

    public int RequestCount { get; private set; }

    public sealed record Match(
        int Confidence,
        int? MatchedBy,
        Guid VideoId,
        string Title,
        Guid SiteId,
        string SiteTitle,
        string? SiteUrl = "https://example.invalid/site",
        string[]? Actors = null);

    /// <summary>Recognises one file name, the way prdb recognises content it holds.</summary>
    public FakePrdb Recognises(
        string fileName,
        string title,
        string siteTitle = "Example Site",
        int confidence = 4,
        int? matchedBy = 0,
        params string[] actors)
    {
        matches[fileName] = new Match(
            confidence,
            matchedBy,
            Guid.CreateVersion7(),
            title,
            Guid.CreateVersion7(),
            siteTitle,
            Actors: actors.Length == 0 ? ["Alex Doe"] : actors);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestCount++;

        if (Failure is { } failure)
        {
            return Respond(failure, """{"title":"Refused by the fixture."}""");
        }

        return request.RequestUri?.AbsolutePath switch
        {
            "/videos/identify" => await IdentifyAsync(request, cancellationToken),
            "/sites" => Respond(HttpStatusCode.OK, Sites()),
            "/rate-limit" => Respond(HttpStatusCode.OK, RateLimit()),
            _ => Respond(HttpStatusCode.NotFound, """{"title":"No such endpoint."}"""),
        };
    }

    private async Task<HttpResponseMessage> IdentifyAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var sent = JsonNode.Parse(
            await request.Content!.ReadAsStringAsync(cancellationToken))!;
        IdentifyRequests.Add(sent);

        if (RawBody is not null)
        {
            return Respond(HttpStatusCode.OK, RawBody);
        }

        var results = new JsonArray();

        foreach (var file in sent["files"]!.AsArray())
        {
            var name = file!["filename"]?.GetValue<string>() ?? string.Empty;
            var reference = file["ref"]!.GetValue<string>();

            // A file it does not hold comes back as a result all the same, at no confidence. That
            // is the ordinary answer for a library of things nobody has catalogued, and the state
            // an installation is mostly in.
            if (!matches.TryGetValue(name, out var match))
            {
                results.Add(new JsonObject
                {
                    ["ref"] = reference,
                    ["confidence"] = 0,
                    ["candidates"] = new JsonArray(),
                });
                continue;
            }

            var site = new JsonObject
            {
                ["id"] = match.SiteId.ToString(),
                ["title"] = match.SiteTitle,
                ["url"] = match.SiteUrl,
            };

            results.Add(new JsonObject
            {
                ["ref"] = reference,
                ["confidence"] = match.Confidence,
                ["matchedBy"] = match.MatchedBy,
                ["videoId"] = match.VideoId.ToString(),
                ["candidates"] = new JsonArray(),
                ["site"] = site,
                ["video"] = new JsonObject
                {
                    ["id"] = match.VideoId.ToString(),
                    ["title"] = match.Title,
                    ["site"] = (JsonNode)site.DeepClone(),
                    ["actors"] = new JsonArray(
                        (match.Actors ?? []).Select(actor => (JsonNode)new JsonObject
                        {
                            ["id"] = Guid.CreateVersion7().ToString(),
                            ["name"] = actor,
                        }).ToArray()),
                    ["durationSeconds"] = 12_345,
                },
            });
        }

        return Respond(
            HttpStatusCode.OK,
            new JsonObject { ["results"] = results }.ToJsonString());
    }

    private static string Sites() =>
        new JsonObject
        {
            ["sites"] = new JsonArray(new JsonObject
            {
                ["id"] = Guid.CreateVersion7().ToString(),
                ["title"] = "Example Site",
                ["url"] = "https://example.invalid/site",
                ["createdAtUtc"] = "2026-08-01T00:00:00Z",
                ["updatedAtUtc"] = "2026-08-01T00:00:00Z",
            }),
        }.ToJsonString();

    private static string RateLimit() =>
        new JsonObject
        {
            ["isEnforced"] = true,
            ["hourly"] = Window(1_000, 1),
            ["monthly"] = Window(10_000, 1),
        }.ToJsonString();

    private static JsonObject Window(int limit, int used) =>
        new()
        {
            ["limit"] = limit,
            ["used"] = used,
            ["remaining"] = limit - used,
            ["resetsInSeconds"] = 600,
        };

    private static HttpResponseMessage Respond(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
}

/// <summary>
/// Hands the client under test the fake in place of the transport the Host would give it.
/// </summary>
internal sealed class FakePrdbTransport(FakePrdb fake) : IHttpMessageHandlerFactory
{
    public HttpMessageHandler CreateHandler(string name) => fake;
}
