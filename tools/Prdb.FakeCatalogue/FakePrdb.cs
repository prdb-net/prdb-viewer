using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Prdb.FakeCatalogue;

/// <summary>
/// Answers prdb's three endpoints in place of the network, so the clients that talk to them can be
/// tested as they actually ship.
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
/// The bodies come from <see cref="CatalogueAnswers"/>, which the runnable stand-in in
/// <c>tools/Prdb.FakeCatalogue.Server</c> answers out of as well. That is why this lives beside
/// the answers rather than in a test project: keeping one copy is not tidiness. Two imitations
/// drift, and drift here is quiet — the reply still parses, the client accepts it, and a test
/// that was meant to prove something passes anyway.
/// </summary>
public sealed class FakePrdb : HttpMessageHandler
{
    private static readonly Regex ArtworkPath = new(
        @"^/videos/([^/]+)/artwork\.bmp$",
        RegexOptions.Compiled);

    private static readonly Regex ActorImagePath = new(
        @"^/actors/([^/]+)/[^/]+\.bmp$",
        RegexOptions.Compiled);

    private readonly Dictionary<string, CatalogueEntry> catalogue =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every request this answered, so a test can assert on what was actually sent.</summary>
    public List<JsonNode> IdentifyRequests { get; } = [];

    /// <summary>Every address that was asked, so a test can see where the client went.</summary>
    public List<Uri> Requested { get; } = [];

    /// <summary>When set, every endpoint answers with this instead, the way an outage does.</summary>
    public HttpStatusCode? Failure { get; set; }

    /// <summary>
    /// When set, every endpoint answers 200 with this body, however malformed. It once applied to
    /// the identify endpoint alone, which quietly made a test that pointed it at the credential
    /// check meaningless — that endpoint went on answering correctly.
    /// </summary>
    public string? RawBody { get; set; }

    /// <summary>How many Sites each page holds. A page not named here is empty.</summary>
    public Dictionary<int, int> SitePages { get; } = new() { [1] = 1 };

    /// <summary>
    /// Site titles the directory publishes on its first page, in place of the synthetic ones.
    /// A test about paging wants Sites it can count; a test about recognition wants Sites whose
    /// titles a Video File's path can actually be read against.
    /// </summary>
    public List<string> PublishedSites { get; } = [];

    /// <summary>Every page number the Site Directory was asked for, in order.</summary>
    public List<int> SitePagesRequested { get; } = [];

    /// <summary>A Site with no title, which the client has to drop rather than keep as a blank.</summary>
    public bool IncludeUnusableSite { get; set; }

    public int RequestCount { get; private set; }

    /// <summary>Every batch of works the Enrichment lane asked about, as it asked.</summary>
    public List<JsonNode> VideoBatchRequests { get; } = [];

    /// <summary>Every batch of Actors it asked about.</summary>
    public List<JsonNode> ActorBatchRequests { get; } = [];

    /// <summary>Recognises one file name, the way prdb recognises content it holds.</summary>
    public FakePrdb Recognises(
        string fileName,
        string title,
        string siteTitle = "Example Site",
        int confidence = 4,
        int? matchedBy = 0,
        params string[] actors)
    {
        catalogue[fileName] = new CatalogueEntry(
            title,
            siteTitle,
            actors.Length == 0 ? ["Alex Doe"] : actors,
            confidence,
            matchedBy);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestCount++;
        Requested.Add(request.RequestUri!);

        if (Failure is { } failure)
        {
            return Respond(failure, """{"title":"Refused by the fixture."}""");
        }

        if (RawBody is not null)
        {
            return Respond(HttpStatusCode.OK, RawBody);
        }

        var path = request.RequestUri?.AbsolutePath;

        if (path is not null && (Artwork(path) ?? ActorImage(path)) is { } artwork)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(artwork)
                {
                    Headers = { ContentType = new MediaTypeHeaderValue("image/bmp") },
                },
            };
        }

        return path switch
        {
            "/videos/identify" => await IdentifyAsync(request, cancellationToken),
            "/videos/batch" => await VideosByIdsAsync(request, cancellationToken),
            "/actors/batch" => await ActorsByIdsAsync(request, cancellationToken),
            "/sites" => Respond(HttpStatusCode.OK, Sites(request.RequestUri!)),
            "/rate-limit" => Respond(HttpStatusCode.OK, CatalogueAnswers.RateLimit()),
            _ => Respond(HttpStatusCode.NotFound, """{"title":"No such endpoint."}"""),
        };
    }

    /// <summary>
    /// The picture an identify answer points at. It travels on the credential-free artwork
    /// transport, which this stands in for as well, so the whole way from a proposal to a picture
    /// an installation holds is exercised rather than assumed.
    /// </summary>
    private byte[]? Artwork(string path)
    {
        var match = ArtworkPath.Match(path);

        if (!match.Success || !Guid.TryParse(match.Groups[1].Value, out var videoId))
        {
            return null;
        }

        var entry = catalogue.Values.FirstOrDefault(candidate => candidate.VideoId == videoId);

        return entry is null ? null : CatalogueAnswers.Artwork(entry.Title);
    }

    /// <summary>
    /// The pictures an Actor's profile points at. Same credential-free transport as a work's, so
    /// the whole way from a credit to a picture an installation holds is exercised.
    /// </summary>
    private byte[]? ActorImage(string path)
    {
        var match = ActorImagePath.Match(path);

        return match.Success ? CatalogueAnswers.Artwork(match.Value) : null;
    }

    private async Task<HttpResponseMessage> IdentifyAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        IdentifyRequests.Add(JsonNode.Parse(body)!);

        return Respond(HttpStatusCode.OK, CatalogueAnswers.Identify(body, catalogue));
    }

    /// <summary>
    /// The Enrichment lane's question: what does prdb say about these works now. It is answered
    /// from the same catalogue the identification ladder reads, because it is the same catalogue.
    /// </summary>
    private async Task<HttpResponseMessage> VideosByIdsAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        VideoBatchRequests.Add(JsonNode.Parse(body)!);

        return Respond(HttpStatusCode.OK, CatalogueAnswers.VideosByIds(body, catalogue));
    }

    /// <summary>What prdb says about the Actors a work credits.</summary>
    private async Task<HttpResponseMessage> ActorsByIdsAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        ActorBatchRequests.Add(JsonNode.Parse(body)!);

        return Respond(HttpStatusCode.OK, CatalogueAnswers.ActorsByIds(body, catalogue));
    }

    private string Sites(Uri uri)
    {
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var page = int.TryParse(query["page"], out var asked) ? asked : 1;
        var pageSize = int.TryParse(query["pageSize"], out var size) ? size : 1_000;
        SitePagesRequested.Add(page);

        var items = PublishedSites.Count > 0
            ? (page == 1
                ? PublishedSites
                    .Select(title => CatalogueAnswers.SiteItem(
                        CatalogueEntry.Identifier($"site:{title}"),
                        title))
                    .ToList()
                : [])
            : Enumerable
                .Range(0, SitePages.GetValueOrDefault(page))
                .Select(index => CatalogueAnswers.SiteItem(
                    CatalogueEntry.Identifier($"page:{page}:{index}"),
                    $"Site {page}-{index}"))
                .ToList();

        if (IncludeUnusableSite && page == 1)
        {
            // Everything the schema requires except a title, which is what the client checks
            // before it keeps one.
            items.Add(CatalogueAnswers.SiteItem(Guid.CreateVersion7(), string.Empty));
        }

        return CatalogueAnswers.Sites(page, pageSize, items);
    }

    private static HttpResponseMessage Respond(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
}

/// <summary>
/// Hands the client under test the fake in place of the transport the Host would give it.
/// </summary>
public sealed class FakePrdbTransport(FakePrdb fake) : IHttpMessageHandlerFactory
{
    public HttpMessageHandler CreateHandler(string name) => fake;
}
