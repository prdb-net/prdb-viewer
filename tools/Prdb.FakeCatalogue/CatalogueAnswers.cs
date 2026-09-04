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
        IReadOnlyDictionary<string, CatalogueEntry> catalogue,
        string artworkBaseUrl = "http://127.0.0.1:5080")
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

            results.Add(new JsonObject
            {
                ["ref"] = reference,
                ["confidence"] = entry.Confidence,
                ["matchedBy"] = entry.MatchedBy,
                ["videoId"] = entry.VideoId.ToString(),
                ["candidates"] = new JsonArray(),
                ["site"] = Site(entry),
                ["video"] = VideoDetail(entry, artworkBaseUrl),
            });
        }

        return new JsonObject { ["results"] = results }.ToJsonString();
    }

    /// <summary>
    /// Everything prdb says about one work. It is the same document whether it arrives as the
    /// detail of an identification or as an answer to <c>POST /videos/batch</c>, so both are built
    /// here and a test cannot see a difference the real API does not have.
    /// </summary>
    public static JsonObject VideoDetail(CatalogueEntry entry, string artworkBaseUrl) =>
        new()
        {
            ["id"] = entry.VideoId.ToString(),
            ["title"] = entry.Title,
            ["site"] = Site(entry),
            ["actors"] = new JsonArray(entry.Actors
                .Select(actor => (JsonNode)new JsonObject
                {
                    ["id"] = CatalogueEntry.Identifier($"actor:{actor}").ToString(),
                    ["name"] = actor,
                })
                .ToArray()),
            ["durationMs"] = 12_345_000,
            ["releaseDate"] = "2025-06-01",
            // The picture prdb offers for the work. An installation retains it rather than
            // pointing a browser here, so the address only has to be one this tool answers.
            ["images"] = new JsonArray(new JsonObject
            {
                ["id"] = CatalogueEntry.Identifier($"image:{entry.Title}").ToString(),
                ["url"] = $"{artworkBaseUrl}/videos/{entry.VideoId}/artwork.bmp",
            }),
        };

    /// <summary>
    /// The answer to <c>POST /actors/batch</c>: a bare array of Actor documents, built from the
    /// names the catalogue's works credit. An Actor is described sparsely on purpose — a real
    /// catalogue holds a name and a handful of fields for most people, and a screen that only
    /// looks right against a complete profile is a screen that is wrong in production.
    /// </summary>
    public static string ActorsByIds(
        string requestBody,
        IReadOnlyDictionary<string, CatalogueEntry> catalogue,
        string artworkBaseUrl = "http://127.0.0.1:5080")
    {
        var asked = (JsonNode.Parse(requestBody)!["ids"]?.AsArray() ?? [])
            .Select(id => id?.GetValue<string>())
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var named = catalogue.Values
            .SelectMany(entry => entry.Actors)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(actor => asked.Contains(CatalogueEntry.Identifier($"actor:{actor}").ToString()))
            .Select(actor => (JsonNode)ActorDetail(actor, artworkBaseUrl))
            .ToArray();

        return new JsonArray(named).ToJsonString();
    }

    /// <summary>Everything this stand-in says about one Actor.</summary>
    public static JsonObject ActorDetail(string name, string artworkBaseUrl)
    {
        var id = CatalogueEntry.Identifier($"actor:{name}");
        var detail = new JsonObject
        {
            ["id"] = id.ToString(),
            ["name"] = name,
            ["images"] = new JsonArray(
                new JsonObject
                {
                    ["id"] = CatalogueEntry.Identifier($"actor-image:{name}:1").ToString(),
                    ["imageType"] = 1,
                    ["imageTypeLabel"] = "Thumbnail",
                    ["url"] = $"{artworkBaseUrl}/actors/{id}/thumbnail.bmp",
                },
                new JsonObject
                {
                    ["id"] = CatalogueEntry.Identifier($"actor-image:{name}:2").ToString(),
                    ["imageType"] = 2,
                    ["imageTypeLabel"] = "Poster",
                    ["url"] = $"{artworkBaseUrl}/actors/{id}/poster.bmp",
                }),
            ["aliases"] = new JsonArray(),
            ["links"] = new JsonArray(),
            ["bios"] = new JsonArray(),
        };

        // One Actor the catalogue knows well, and the rest as sparsely as a real one holds them.
        if (Described.Contains(name))
        {
            detail["genderLabel"] = "Female";
            detail["birthday"] = "1994-03-17";
            detail["birthdayTypeLabel"] = "Exact";
            detail["birthplace"] = "Example City";
            detail["haircolorLabel"] = "Brown";
            detail["eyecolorLabel"] = "Green";
            detail["height"] = 170;
            detail["nationalityLabel"] = "Example Nation";
            detail["ethnicityLabel"] = "Example Ethnicity";
            detail["careerStart"] = 2014;
            detail["tattoos"] = "A small star behind the left ear.";
            detail["aliases"] = new JsonArray(new JsonObject { ["name"] = $"{name} X" });
            detail["links"] = new JsonArray(new JsonObject
            {
                ["externalSiteLabel"] = "Twitter",
                ["url"] = $"https://example.invalid/{id}",
            });
            detail["bios"] = new JsonArray(new JsonObject
            {
                ["id"] = CatalogueEntry.Identifier($"bio:{name}").ToString(),
                ["text"] = $"{name} has been in front of a camera since 2014.",
            });
        }

        return detail;
    }

    /// <summary>The Actors this stand-in describes fully. Everyone else is a name and pictures.</summary>
    private static readonly HashSet<string> Described =
        new(StringComparer.OrdinalIgnoreCase) { "Alex Doe", "Robin Fay" };

    /// <summary>The answer to <c>POST /videos/batch</c>: a bare array of work documents.</summary>
    public static string VideosByIds(
        string requestBody,
        IReadOnlyDictionary<string, CatalogueEntry> catalogue,
        string artworkBaseUrl = "http://127.0.0.1:5080")
    {
        var asked = (JsonNode.Parse(requestBody)!["ids"]?.AsArray() ?? [])
            .Select(id => Guid.TryParse(id?.GetValue<string>(), out var parsed) ? parsed : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToHashSet();

        return new JsonArray(catalogue.Values
            .DistinctBy(entry => entry.VideoId)
            .Where(entry => asked.Contains(entry.VideoId))
            .Select(entry => (JsonNode)VideoDetail(entry, artworkBaseUrl))
            .ToArray())
            .ToJsonString();
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

    /// <summary>
    /// A picture for one work: a small uncompressed bitmap whose colour follows the title, so the
    /// four seeded films are told apart at a glance and a review case has two pictures to compare.
    /// </summary>
    /// <remarks>
    /// BMP rather than PNG because it needs no compression and no checksum, and rather than SVG
    /// because a viewer that retains a picture and serves it back under its own address refuses
    /// the one format that can carry markup.
    /// </remarks>
    public static byte[] Artwork(string title)
    {
        const int width = 160;
        const int height = 90;
        var seed = CatalogueEntry.Identifier($"image:{title}").ToByteArray();
        var stride = (width * 3) + ((4 - ((width * 3) % 4)) % 4);
        var pixels = new byte[stride * height];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // A flat ground with one diagonal band across it, so the picture reads as a
                // picture rather than as a failed load.
                var band = (x + y) % 40 < 12;
                var offset = (y * stride) + (x * 3);
                pixels[offset] = Shade(seed[0], band);
                pixels[offset + 1] = Shade(seed[1], band);
                pixels[offset + 2] = Shade(seed[2], band);
            }
        }

        var file = new byte[54 + pixels.Length];
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BitConverter.GetBytes(file.Length).CopyTo(file, 2);
        BitConverter.GetBytes(54).CopyTo(file, 10);
        BitConverter.GetBytes(40).CopyTo(file, 14);
        BitConverter.GetBytes(width).CopyTo(file, 18);
        BitConverter.GetBytes(height).CopyTo(file, 22);
        BitConverter.GetBytes((short)1).CopyTo(file, 26);
        BitConverter.GetBytes((short)24).CopyTo(file, 28);
        BitConverter.GetBytes(pixels.Length).CopyTo(file, 34);
        pixels.CopyTo(file, 54);

        return file;
    }

    private static byte Shade(byte channel, bool band) =>
        (byte)Math.Clamp(60 + (channel / 2) + (band ? 70 : 0), 0, 255);

    private static JsonObject Window(int limit) =>
        new()
        {
            ["limit"] = limit,
            ["used"] = 1,
            ["remaining"] = limit - 1,
            ["resetsInSeconds"] = 600,
        };
}
