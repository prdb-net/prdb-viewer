using System.Net;

using Prdb.Sdk;
using Prdb.Sdk.Generated.Models;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Configuration;

namespace Prdb.Viewer.Infrastructure.Library;

public enum ActorProfileFetchStatus
{
    Fetched,
    Rejected,
    Unavailable,
}

/// <summary>One picture prdb offers for an Actor.</summary>
public sealed record RemoteActorImage(string Id, string Url, int Kind, string? KindLabel);

/// <summary>One name an Actor is also credited under, and the Site it belongs to.</summary>
public sealed record RemoteActorAlias(string Name, string? SiteId);

/// <summary>One link away from prdb, as prdb offers it.</summary>
public sealed record RemoteActorLink(string Url, string? SiteLabel);

/// <summary>Everything prdb says about one Actor, in its own words and its own labels.</summary>
public sealed record RemoteActorProfile(
    string Id,
    string Name,
    string? GenderLabel,
    DateTime? Birthday,
    string? BirthdayPrecisionLabel,
    DateTime? Deathday,
    string? Birthplace,
    string? HaircolourLabel,
    string? EyecolourLabel,
    string? BreastTypeLabel,
    int? HeightCentimetres,
    string? BraSizeLabel,
    int? WaistCentimetres,
    int? HipCentimetres,
    string? NationalityLabel,
    string? EthnicityLabel,
    int? CareerStart,
    int? CareerEnd,
    string? Tattoos,
    string? Piercings,
    IReadOnlyList<RemoteActorImage> Images,
    IReadOnlyList<RemoteActorAlias> Aliases,
    IReadOnlyList<RemoteActorLink> Links,
    IReadOnlyList<string> Bios);

public sealed record ActorProfileFetchResult(
    ActorProfileFetchStatus Status,
    IReadOnlyList<RemoteActorProfile> Profiles,
    string? Detail = null);

public interface IPrdbActorProfileClient
{
    /// <summary>How many Actors one request may ask about, as the documented API allows.</summary>
    int BatchLimit => 50;

    Task<ActorProfileFetchResult> FetchAsync(
        string credential,
        IReadOnlyList<string> prdbActorIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Asks the documented public prdb API what it knows about the Actors this library's Videos
/// credit, through the official SDK. It sends nothing about the local library beyond the Actor
/// identifiers the library already holds.
/// </summary>
public sealed class PrdbActorProfileClient(
    IHttpMessageHandlerFactory handlers,
    PrdbEndpoint? endpoint = null)
    : IPrdbActorProfileClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    public async Task<ActorProfileFetchResult> FetchAsync(
        string credential,
        IReadOnlyList<string> prdbActorIds,
        CancellationToken cancellationToken = default)
    {
        var wanted = prdbActorIds
            .Select(id => Guid.TryParse(id, out var parsed) ? parsed : (Guid?)null)
            .OfType<Guid>()
            .Distinct()
            .Select(id => (Guid?)id)
            .ToList();

        if (wanted.Count == 0)
        {
            return new ActorProfileFetchResult(ActorProfileFetchStatus.Fetched, []);
        }

        var status = new ResponseStatusOption();
        var client = PrdbClientFactory.Create(
            credential,
            (endpoint ?? new PrdbEndpoint()).BaseUrl,
            transport: handlers.CreateHandler(PrdbConnectionVerifier.TransportName),
            retry: PrdbRetryOptions.Disabled,
            timeout: RequestTimeout);

        try
        {
            var response = await client.Actors.Batch.PostAsync(
                new GetActorsByIdsRequest { Ids = wanted },
                configuration => configuration.Options.Add(status),
                cancellationToken);

            return new ActorProfileFetchResult(
                ActorProfileFetchStatus.Fetched,
                (response ?? [])
                    .Select(Map)
                    .OfType<RemoteActorProfile>()
                    .ToArray());
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
        {
            return status.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? new ActorProfileFetchResult(
                    ActorProfileFetchStatus.Rejected,
                    [],
                    "prdb refused the installation credential.")
                : new ActorProfileFetchResult(
                    ActorProfileFetchStatus.Unavailable,
                    [],
                    $"prdb could not be reached ({(int?)status.StatusCode ?? 0}).");
        }
    }

    private static RemoteActorProfile? Map(ActorDetailDto actor) =>
        actor.Id is null || string.IsNullOrWhiteSpace(actor.Name)
            ? null
            : new RemoteActorProfile(
                actor.Id.Value.ToString(),
                actor.Name,
                ActorFacts.Stated(actor.GenderLabel),
                actor.Birthday?.DateTime,
                Trimmed(actor.BirthdayTypeLabel),
                actor.Deathday?.DateTime,
                Trimmed(actor.Birthplace),
                ActorFacts.Stated(actor.HaircolorLabel),
                ActorFacts.Stated(actor.EyecolorLabel),
                ActorFacts.Stated(actor.BreastTypeLabel),
                actor.Height,
                Trimmed(actor.BraSizeLabel),
                actor.WaistSize,
                actor.HipSize,
                ActorFacts.Stated(actor.NationalityLabel),
                ActorFacts.Stated(actor.EthnicityLabel),
                actor.CareerStart,
                actor.CareerEnd,
                Trimmed(actor.Tattoos),
                Trimmed(actor.Piercings),
                (actor.Images ?? [])
                    .Where(image => image.Id is not null &&
                                    !string.IsNullOrWhiteSpace(image.Url))
                    .Select(image => new RemoteActorImage(
                        image.Id!.Value.ToString(),
                        image.Url!,
                        (int?)image.ImageType ?? 0,
                        Trimmed(image.ImageTypeLabel)))
                    .ToArray(),
                (actor.Aliases ?? [])
                    .Where(alias => !string.IsNullOrWhiteSpace(alias.Name))
                    .Select(alias => new RemoteActorAlias(alias.Name!, alias.SiteId?.ToString()))
                    .ToArray(),
                (actor.Links ?? [])
                    .Where(link => !string.IsNullOrWhiteSpace(link.Url))
                    .Select(link => new RemoteActorLink(link.Url!, Trimmed(link.ExternalSiteLabel)))
                    .ToArray(),
                (actor.Bios ?? [])
                    .Select(bio => Trimmed(bio.Text))
                    .OfType<string>()
                    .ToArray());

    /// <summary>
    /// A field prdb sends empty says nothing, and a page that prints an empty label says less than
    /// one that omits the line.
    /// </summary>
    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
