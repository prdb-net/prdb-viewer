using System.Text.Json;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// Reads the parts of a retained work that are held as documents rather than as columns: its
/// release names and what prdb knows it in.
/// </summary>
public static class RetainedWorkFacts
{
    /// <summary>
    /// How many of a work's pictures this installation holds. prdb offers a handful; the ceiling
    /// exists so an unexpected answer cannot fill the application data directory.
    /// </summary>
    public const int MaximumImages = 12;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<string> ReleaseNames(string? json) =>
        Read<string[]>(json)?
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray() ?? [];

    public static RemoteQualityOverview? QualityOverview(string? json) =>
        Read<RemoteQualityOverview>(json);

    private static T? Read<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, Json);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
