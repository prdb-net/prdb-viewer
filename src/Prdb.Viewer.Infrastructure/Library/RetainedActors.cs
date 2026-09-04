using System.Text.Json;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// One Actor Credit as a retained metadata document names them: the name that document spells,
/// and the Actor it resolves to where prdb sent an identity.
/// </summary>
/// <remarks>
/// Only the name and the identity are kept here. What prdb says <em>about</em> that Actor belongs
/// to their Actor Profile (ADR 0020), which is the one place it is held and refreshed; keeping a
/// second, poorer copy of it beside every Video would be two answers to one question.
/// </remarks>
public sealed record RetainedActor(string Name, string? ActorId = null);

/// <summary>
/// Reads and writes the Actor Credits of a retained metadata document.
/// </summary>
/// <remarks>
/// The document was a plain array of names before Actors had an identity, and a retained document
/// survives an upgrade untouched: nothing rewrites one until the work is asked about again. So the
/// reader accepts both shapes rather than a version field — the element itself says which it is,
/// and a string element is a credit that resolves to nobody, which is exactly what those documents
/// meant. Everything written from now on is the object shape.
/// </remarks>
public static class RetainedActors
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string? Serialize(IReadOnlyList<RetainedActor> actors) =>
        actors.Count == 0 ? null : JsonSerializer.Serialize(actors, Options);

    public static IReadOnlyList<RetainedActor> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var actors = new List<RetainedActor>();

            foreach (var element in document.RootElement.EnumerateArray())
            {
                var actor = Read(element);

                if (actor is not null)
                {
                    actors.Add(actor);
                }
            }

            return actors;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>The names alone, for the surfaces that only ever showed names.</summary>
    public static IReadOnlyList<string> Names(string? json) =>
        Deserialize(json).Select(actor => actor.Name).ToArray();

    private static RetainedActor? Read(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            var name = element.GetString();

            return string.IsNullOrWhiteSpace(name) ? null : new RetainedActor(name);
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!element.TryGetProperty("name", out var named) ||
            named.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var spelling = named.GetString();

        if (string.IsNullOrWhiteSpace(spelling))
        {
            return null;
        }

        var identity = element.TryGetProperty("actorId", out var actorId) &&
                       actorId.ValueKind == JsonValueKind.String
            ? actorId.GetString()
            : null;

        return new RetainedActor(
            spelling,
            string.IsNullOrWhiteSpace(identity) ? null : identity);
    }
}
