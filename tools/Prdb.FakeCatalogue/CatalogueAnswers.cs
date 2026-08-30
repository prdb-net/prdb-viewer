using System.Text.Json.Nodes;

namespace Prdb.FakeCatalogue;

/// <summary>One thing the catalogue knows about, keyed by the file name that finds it.</summary>
/// <param name="Title">The work's title, as it will read on the browsing screen.</param>
/// <param name="SiteTitle">The Site it belongs to, which is what a facet row is drawn from.</param>
/// <param name="Actors">Who is in it. The other facet row.</param>
/// <param name="Confidence">0 None, 1 Partial, 2 Probable, 3 Strong, 4 Exact, 5 Ambiguous.</param>
/// <param name="MatchedBy">
/// 0 OsHash, 1 PHash, 2 Filename, 3 ReleaseName, 4 Site. It decides more than it looks: only a
/// content match — OsHash or PHash — is evidence enough to establish a Work without a person
/// agreeing to it. A name is not, however sure the catalogue sounds, so a match by file name
/// lands in the identification review queue instead. Both are states worth seeing.
/// </param>
public sealed record CatalogueEntry(
    string Title,
    string SiteTitle,
    string[] Actors,
    int Confidence = 4,
    int? MatchedBy = 0)
{
    /// <summary>Stable per title, so a Site keeps its identity across restarts and rescans.</summary>
    public Guid SiteId { get; } = Identifier($"site:{SiteTitle}");

    public Guid VideoId { get; } = Identifier($"video:{Title}");

    public static Guid Identifier(string of)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(of));
        return new Guid(bytes);
    }
}

/// <summary>
/// The bodies prdb's three endpoints answer with, in one place.
///
/// Both stand-ins are built out of this: the message handler the tests hang below the SDK, and the
/// server this tool runs for a local installation to talk to. Two separate imitations would drift,
/// and drift here is not loud — the reply still parses, the client accepts it, and the test that
/// was supposed to prove something goes green. That already happened once during this work, with a
/// list of Sites answered under the wrong field name.
///
/// The shapes come from the SDK's OpenAPI document. Two are worth stating outright, because
/// guessing them is how the above happens: `confidence` and `matchedBy` are integers rather than
/// names, and the page of Sites is `items`.
/// </summary>
public static class CatalogueAnswers
{
    /// <summary>
    /// Answers an identify request, echoing each file's `ref` so results can be matched back.
    /// A file the catalogue does not hold is answered at no confidence rather than left out.
    /// </summary>
    public static string Identify(
        string requestBody,
        IReadOnlyDictionary<string, CatalogueEntry> catalogue)
    {
        var request = JsonNode.Parse(requestBody)!;
        var results = new JsonArray();

        foreach (var file in request["files"]!.AsArray())
        {
            var name = file!["filename"]?.GetValue<string>() ?? string.Empty;
            var reference = file["ref"]!.GetValue<string>();

            if (!catalogue.TryGetValue(name, out var entry))
            {
                results.Add(new JsonObject
                {
                    ["ref"] = reference,
                    ["confidence"] = 0,
                    ["candidates"] = new JsonArray(),
                });
                continue;
            }

            var site = Site(entry);

            results.Add(new JsonObject
            {
                ["ref"] = reference,
                ["confidence"] = entry.Confidence,
                ["matchedBy"] = entry.MatchedBy,
                ["videoId"] = entry.VideoId.ToString(),
                ["candidates"] = new JsonArray(),
                ["site"] = site,
                ["video"] = new JsonObject
                {
                    ["id"] = entry.VideoId.ToString(),
                    ["title"] = entry.Title,
                    ["site"] = (JsonNode)site.DeepClone(),
                    ["actors"] = new JsonArray(entry.Actors
                        .Select(actor => (JsonNode)new JsonObject
                        {
                            ["id"] = CatalogueEntry.Identifier($"actor:{actor}").ToString(),
                            ["name"] = actor,
                        })
                        .ToArray()),
                    ["durationSeconds"] = 12_345,
                },
            });
        }

        return new JsonObject { ["results"] = results }.ToJsonString();
    }

    /// <summary>
    /// One page of the Site directory. The field is `items`; a client reading it finds nothing
    /// under any other name and accepts the empty page without complaint.
    /// </summary>
    public static string Sites(int page, int pageSize, IEnumerable<JsonObject> items)
    {
        var array = new JsonArray(items.Cast<JsonNode>().ToArray());

        return new JsonObject
        {
            ["items"] = array,
            ["totalCount"] = array.Count,
            ["page"] = page,
            ["pageSize"] = pageSize,
            ["sortBy"] = "title",
            ["sortDirection"] = "asc",
        }.ToJsonString();
    }

    public static JsonObject SiteItem(Guid id, string title) =>
        new()
        {
            ["id"] = id.ToString(),
            ["title"] = title,
            ["url"] = "https://example.invalid/site",
            ["createdAtUtc"] = "2026-08-01T00:00:00Z",
            ["updatedAtUtc"] = "2026-08-01T00:00:00Z",
        };

    /// <summary>
    /// The rate limit. All three fields are required, and a reply short of them is what the
    /// verifier now refuses to read as proof that a credential works.
    /// </summary>
    public static string RateLimit() =>
        new JsonObject
        {
            ["isEnforced"] = true,
            ["hourly"] = Window(1_000),
            ["monthly"] = Window(10_000),
        }.ToJsonString();

    private static JsonObject Site(CatalogueEntry entry) =>
        SiteItem(entry.SiteId, entry.SiteTitle);

    private static JsonObject Window(int limit) =>
        new()
        {
            ["limit"] = limit,
            ["used"] = 1,
            ["remaining"] = limit - 1,
            ["resetsInSeconds"] = 600,
        };
}
