using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// Brings in what prdb says about the Actors this library's Videos credit.
/// </summary>
/// <remarks>
/// An Actor exists here because an Established Work Identification credited them, so this creates
/// the Actor from the credit and then asks prdb what it knows. Both halves are bounded: a slice
/// creates what the credits ask for and asks about at most one batch of Actors, and gives the lane
/// back.
///
/// A profile that does not arrive is not a Work Issue. The Actor is named, their Videos are
/// listed, and their page says the rest has not arrived — which is the whole of the loss, and not
/// something to call an Administrator to. It is the reason
/// <see cref="ProposedWorkArtworkRetention"/> gives for the same decision.
/// </remarks>
public sealed class ActorProfileRetention(
    ViewerDbContext database,
    IPrdbActorProfileClient client,
    TimeProvider timeProvider)
{
    /// <summary>
    /// How long a retained profile stands before it is asked about again. An Actor gains pictures,
    /// aliases and a career end after they are first asked about, and none of that is urgent.
    /// </summary>
    public static readonly TimeSpan RefreshHorizon = TimeSpan.FromDays(30);

    /// <summary>
    /// How many of an Actor's pictures this installation holds.
    /// </summary>
    /// <remarks>
    /// prdb offers a Thumbnail, a Poster and a Face, so this is a ceiling rather than a policy —
    /// it exists so that one Actor with an unexpected three hundred pictures cannot fill the
    /// application data directory. Where it is reached, the Actor records how many were offered
    /// and their page says so, because a capped gallery presented as the whole of one is a lie
    /// about the catalogue.
    /// </remarks>
    public const int MaximumImages = 24;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Creates an Actor for every credit that resolves to one and has none yet. The name is the
    /// credit's until prdb answers with its own, so an Actor is nameable — and their page
    /// readable — before any profile arrives.
    /// </summary>
    public async Task<int> EnsureActorsAsync(CancellationToken cancellationToken = default)
    {
        var missing = await database.VideoActors
            .AsNoTracking()
            .Where(credit => credit.PrdbActorId != null &&
                             !database.Actors.Any(actor =>
                                 actor.PrdbActorId == credit.PrdbActorId))
            .Select(credit => new { credit.PrdbActorId, credit.Name, credit.NormalizedName })
            .Distinct()
            .Take(500)
            .ToListAsync(cancellationToken);

        var created = 0;

        foreach (var credit in missing.DistinctBy(
                     credit => credit.PrdbActorId,
                     StringComparer.OrdinalIgnoreCase))
        {
            database.Actors.Add(new ActorRow
            {
                Id = Guid.CreateVersion7(),
                PrdbActorId = credit.PrdbActorId!,
                Name = credit.Name,
                NormalizedName = credit.NormalizedName,
                ProfileState = ActorProfileState.Pending,
            });
            created++;
        }

        if (created > 0)
        {
            await database.SaveChangesAsync(cancellationToken);
        }

        return created;
    }

    /// <summary>
    /// Asks prdb about one batch of Actors whose profile is missing or stale, and retains what it
    /// answers. Returns what the request itself did, so the lane can tell an outage from an
    /// answer without inspecting anything.
    /// </summary>
    public async Task<ActorProfileFetchStatus> RetainAsync(
        string credential,
        CancellationToken cancellationToken = default)
    {
        var horizon = Now() - RefreshHorizon;
        var outstanding = await database.Actors
            .AsTracking()
            .Where(actor => actor.ProfileState == ActorProfileState.Pending ||
                            actor.FetchedAt == null ||
                            actor.FetchedAt < horizon)
            .OrderBy(actor => actor.FetchedAt ?? DateTime.MinValue)
            .Take(client.BatchLimit)
            .Include(actor => actor.Aliases)
            .Include(actor => actor.Images)
            .ToListAsync(cancellationToken);

        if (outstanding.Count == 0)
        {
            return ActorProfileFetchStatus.Fetched;
        }

        var result = await client.FetchAsync(
            credential,
            outstanding.Select(actor => actor.PrdbActorId).ToArray(),
            cancellationToken);

        if (result.Status != ActorProfileFetchStatus.Fetched)
        {
            return result.Status;
        }

        var answered = result.Profiles.ToDictionary(
            profile => profile.Id,
            StringComparer.OrdinalIgnoreCase);
        var now = Now();

        foreach (var actor in outstanding)
        {
            if (answered.TryGetValue(actor.PrdbActorId, out var profile))
            {
                Apply(actor, profile, now);
                continue;
            }

            // prdb answered, and had nothing for this Actor. That is a fact about the Actor rather
            // than about the request, so it is recorded as one and asked about again at the next
            // horizon rather than on the next slice.
            actor.ProfileState = ActorProfileState.Unavailable;
            actor.FetchedAt = now;
        }

        await database.SaveChangesAsync(cancellationToken);
        return ActorProfileFetchStatus.Fetched;
    }

    private void Apply(ActorRow actor, RemoteActorProfile profile, DateTime now)
    {
        actor.Name = profile.Name;
        actor.NormalizedName = LibrarySearchRule.Normalize(profile.Name);
        actor.GenderLabel = profile.GenderLabel;
        actor.Birthday = profile.Birthday;
        actor.BirthdayPrecisionLabel = profile.BirthdayPrecisionLabel;
        actor.Deathday = profile.Deathday;
        actor.Birthplace = profile.Birthplace;
        actor.HaircolourLabel = profile.HaircolourLabel;
        actor.EyecolourLabel = profile.EyecolourLabel;
        actor.BreastTypeLabel = profile.BreastTypeLabel;
        actor.HeightCentimetres = profile.HeightCentimetres;
        actor.BraSizeLabel = profile.BraSizeLabel;
        actor.WaistCentimetres = profile.WaistCentimetres;
        actor.HipCentimetres = profile.HipCentimetres;
        actor.NationalityLabel = profile.NationalityLabel;
        actor.EthnicityLabel = profile.EthnicityLabel;
        actor.CareerStart = profile.CareerStart;
        actor.CareerEnd = profile.CareerEnd;
        actor.Tattoos = profile.Tattoos;
        actor.Piercings = profile.Piercings;
        actor.LinksJson = profile.Links.Count == 0
            ? null
            : JsonSerializer.Serialize(profile.Links, Json);
        actor.BiosJson = profile.Bios.Count == 0
            ? null
            : JsonSerializer.Serialize(profile.Bios, Json);
        actor.ProfileState = ActorProfileState.Retained;
        actor.FetchedAt = now;

        RefreshAliases(actor, profile.Aliases);
        RefreshImages(actor, profile.Images);
    }

    private void RefreshAliases(ActorRow actor, IReadOnlyList<RemoteActorAlias> aliases)
    {
        var wanted = aliases
            .Select(alias => (alias.Name, alias.SiteId, Normalized: LibrarySearchRule.Normalize(alias.Name)))
            .Where(alias => alias.Normalized.Length > 0 && alias.Normalized != actor.NormalizedName)
            .DistinctBy(alias => alias.Normalized)
            .ToArray();

        foreach (var existing in actor.Aliases.ToArray())
        {
            if (!wanted.Any(alias => alias.Normalized == existing.NormalizedName))
            {
                actor.Aliases.Remove(existing);
                database.ActorAliases.Remove(existing);
            }
        }

        foreach (var alias in wanted)
        {
            var existing = actor.Aliases
                .FirstOrDefault(row => row.NormalizedName == alias.Normalized);

            if (existing is null)
            {
                actor.Aliases.Add(new ActorAliasRow
                {
                    Id = Guid.CreateVersion7(),
                    ActorId = actor.Id,
                    Name = alias.Name,
                    NormalizedName = alias.Normalized,
                    PrdbSiteId = alias.SiteId,
                });
                continue;
            }

            existing.Name = alias.Name;
            existing.PrdbSiteId = alias.SiteId;
        }
    }

    /// <summary>
    /// Records which pictures prdb offers, and leaves the bytes to the retention lane. A picture
    /// whose address has changed is asked for again and is not served in the meantime: the file on
    /// disk is still there, but it is no longer the picture prdb offers, and a gallery that says a
    /// picture has not arrived is more use than one showing the wrong one.
    /// </summary>
    private void RefreshImages(ActorRow actor, IReadOnlyList<RemoteActorImage> images)
    {
        var offered = images
            .Where(image => !string.IsNullOrWhiteSpace(image.Id))
            .DistinctBy(image => image.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var wanted = offered.Take(MaximumImages).ToArray();
        actor.OfferedImageCount = offered.Length;

        foreach (var existing in actor.Images.ToArray())
        {
            if (!wanted.Any(image =>
                    string.Equals(image.Id, existing.PrdbImageId, StringComparison.OrdinalIgnoreCase)))
            {
                actor.Images.Remove(existing);
                database.ActorImages.Remove(existing);
            }
        }

        for (var position = 0; position < wanted.Length; position++)
        {
            var image = wanted[position];
            var existing = actor.Images.FirstOrDefault(row =>
                string.Equals(row.PrdbImageId, image.Id, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                actor.Images.Add(new ActorImageRow
                {
                    Id = Guid.CreateVersion7(),
                    ActorId = actor.Id,
                    PrdbImageId = image.Id,
                    SourceUrl = image.Url,
                    Kind = image.Kind,
                    KindLabel = image.KindLabel,
                    Position = position,
                    State = ActorImageState.Pending,
                });
                continue;
            }

            if (!string.Equals(existing.SourceUrl, image.Url, StringComparison.Ordinal))
            {
                existing.SourceUrl = image.Url;
                existing.State = ActorImageState.Pending;
            }
            else if (existing.State == ActorImageState.Unavailable)
            {
                // A refresh is the one moment worth trying a picture that did not arrive again. A
                // brief outage would otherwise cost an Actor their gallery permanently, since the
                // address prdb offers has not changed and nothing else would ever ask for it.
                existing.State = ActorImageState.Pending;
            }

            existing.Kind = image.Kind;
            existing.KindLabel = image.KindLabel;
            existing.Position = position;
        }
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
}
