using Prdb.FakeCatalogue;

// A stand-in for prdb, so an installation can be run against a catalogue that answers on demand.
//
// The real service recognises content. A library assembled for a test is in no catalogue, so every
// file comes back unknown however good the credential is — which leaves the browsing screens with
// no Site, no Actor and therefore no facet row to look at. This answers for the files the seed
// writes, so those screens have something to be right or wrong about.
//
// It serves https because the SDK refuses to send a credential over anything else, and it uses the
// ASP.NET development certificate for it. Run `dotnet dev-certs https --trust` once.
//
// Nothing here is shipped, and it holds no secret: it accepts any credential, because what it is
// for is the shape of an answer rather than who is asking.
var builder = WebApplication.CreateSlimBuilder(args);

// The slim builder leaves https out, and binding an https address without this fails at startup
// with nothing but a note to call it.
builder.WebHost.UseKestrelHttpsConfiguration();
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("FAKE_PRDB_URL") ?? "https://127.0.0.1:5443");
builder.Logging.SetMinimumLevel(LogLevel.Warning);

var app = builder.Build();

// Keyed by the file name that finds it, which is how prdb matches when a content hash does not:
// these are the names the seed writes, so a seeded installation comes up identified.
var catalogue = new Dictionary<string, CatalogueEntry>(StringComparer.OrdinalIgnoreCase)
{
    ["first-film.mp4"] = new("The First Film", "Example Pictures", ["Alex Doe", "Sam Roe"]),
    ["second-film.webm"] = new("The Second Film", "Example Pictures", ["Alex Doe"]),
    // A different Site and cast, so the facet rows have more than one value to choose between —
    // which is the state the facets were twice defective in.
    ["third-film.mp4"] = new("The Third Film", "Second Example Studio", ["Jules Poe"]),
    // Matched by name rather than by content, which is not evidence enough to file a Work without
    // a person agreeing to it. It lands in the identification review queue, so that screen has
    // something on it too.
    ["fourth-film.mp4"] = new("The Fourth Film", "Second Example Studio", ["Jules Poe"], MatchedBy: 2),
};

app.MapGet("/rate-limit", () => Results.Content(CatalogueAnswers.RateLimit(), "application/json"));

app.MapPost("/videos/identify", async (HttpRequest request) =>
{
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync();

    return Results.Content(CatalogueAnswers.Identify(body, catalogue), "application/json");
});

app.MapGet("/sites", (int? page, int? pageSize) =>
{
    // Every Site the catalogue knows, on the first page. A second page would be empty, which is
    // how the client is told to stop asking.
    var items = page is null or 1
        ? catalogue.Values
            .Select(entry => (entry.SiteId, entry.SiteTitle))
            .Distinct()
            .Select(site => CatalogueAnswers.SiteItem(site.SiteId, site.SiteTitle))
        : [];

    return Results.Content(
        CatalogueAnswers.Sites(page ?? 1, pageSize ?? 1_000, items),
        "application/json");
});

// Anything else is a 404 rather than a plausible-looking empty answer, so a call to an endpoint
// this does not implement fails where it is made instead of somewhere later.
app.MapFallback(() => Results.NotFound(new { title = "The fake catalogue does not serve this." }));

Console.WriteLine($"Fake prdb catalogue on {string.Join(", ", app.Urls.DefaultIfEmpty("the configured url"))}");
Console.WriteLine($"Knows {catalogue.Count} files across {catalogue.Values.Select(e => e.SiteTitle).Distinct().Count()} Sites.");

await app.RunAsync();
