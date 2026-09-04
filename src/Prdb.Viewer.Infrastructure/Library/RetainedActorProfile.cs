using System.Text.Json;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// Reads the parts of an Actor Profile that are held as documents rather than as rows: the links
/// away from prdb and the bios. Nothing ever looks inside them, so nothing has to index them.
/// </summary>
public static class RetainedActorProfile
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<ActorLinkView> Links(string? json) =>
        Read<RemoteActorLink[]>(json)?
            .Where(link => !string.IsNullOrWhiteSpace(link.Url))
            .Select(link => new ActorLinkView(link.Url, link.SiteLabel))
            .ToArray() ?? [];

    public static IReadOnlyList<string> Bios(string? json) =>
        Read<string[]>(json)?
            .Where(bio => !string.IsNullOrWhiteSpace(bio))
            .ToArray() ?? [];

    private static T? Read<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
