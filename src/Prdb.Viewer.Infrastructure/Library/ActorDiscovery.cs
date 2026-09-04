using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// The Actors this library's Videos credit: an index to browse them from, and one Actor with
/// everything prdb says about them and the Videos they are in here.
/// </summary>
/// <remarks>
/// Admission follows Direct Address rather than Ordinary Discovery: opening an Actor is the User's
/// own decision to look at them, so what this library holds them in is shown whether or not this
/// client can play it, with the card saying what will not play. Only what has left the active
/// library is refused.
/// </remarks>
public sealed class ActorDiscovery(ViewerDbContext database, LibraryDiscovery library)
{
    /// <summary>
    /// How many of an Actor's Videos their own page carries. Beyond it the page sends the reader
    /// to the Library narrowed to that Actor, which already has the search, the facets, the order
    /// and the paging that a longer list would need (ADR 0019).
    /// </summary>
    public const int VideosOnAPage = 60;

    public async Task<ActorIndexPage> IndexAsync(
        Guid accountId,
        ActorIndexRequest request,
        CancellationToken cancellationToken = default)
    {
        var term = LibrarySearchRule.Normalize(request.Query);
        // Only Actors this library's Videos actually credit. An Actor whose every Video has left
        // the active library keeps their profile — it costs a row and is expensive to fetch
        // again — but they are not somebody this installation's index should offer.
        var matched = database.Actors
            .AsNoTracking()
            .Where(actor => database.VideoActors.Any(credit =>
                credit.PrdbActorId == actor.PrdbActorId &&
                credit.Video.Availability != VideoAvailability.Removed));

        if (term.Length > 0)
        {
            // A name or an alias: somebody types the name they know, which is not always the one
            // prdb leads with.
            matched = matched.Where(actor =>
                actor.NormalizedName.Contains(term) ||
                actor.Aliases.Any(alias => alias.NormalizedName.Contains(term)));
        }

        var total = await matched.CountAsync(cancellationToken);
        var awaiting = await matched.CountAsync(
            actor => actor.ProfileState == ActorProfileState.Pending,
            cancellationToken);
        var take = LibraryPaging.Clamp(request.Take);
        var counted = matched.Select(actor => new
        {
            Actor = actor,
            Videos = database.VideoActors.Count(credit =>
                credit.PrdbActorId == actor.PrdbActorId &&
                credit.Video.Availability != VideoAvailability.Removed),
        });
        var ordered = request.Sort == ActorSortOrder.MostHere
            ? counted.OrderByDescending(row => row.Videos).ThenBy(row => row.Actor.NormalizedName)
            : counted.OrderBy(row => row.Actor.NormalizedName);
        var page = await ordered
            .Skip(Math.Max(0, request.Skip))
            .Take(take + 1)
            .Select(row => new
            {
                row.Actor.PrdbActorId,
                row.Actor.Name,
                row.Actor.GenderLabel,
                row.Actor.ProfileState,
                row.Videos,
                Portrait = row.Actor.Images
                    .Where(image => image.State == ActorImageState.Retained &&
                                    image.PublicImageId != null)
                    .OrderBy(image => image.Kind)
                    .ThenBy(image => image.Position)
                    .Select(image => image.PublicImageId)
                    .FirstOrDefault(),
                Favourite = database.PersonalActorStates.Any(state =>
                    state.AccountId == accountId &&
                    state.PrdbActorId == row.Actor.PrdbActorId),
            })
            .ToListAsync(cancellationToken);

        return new ActorIndexPage(
            page.Take(take)
                .Select(row => new ActorSummary(
                    row.PrdbActorId,
                    row.Name,
                    PortraitUrl(row.Portrait),
                    row.GenderLabel,
                    row.Videos,
                    row.ProfileState,
                    row.Favourite))
                .ToArray(),
            total,
            page.Count > take,
            awaiting);
    }

    public async Task<ActorDetail?> GetAsync(
        string actorId,
        Guid accountId,
        string clientContextKey,
        CancellationToken cancellationToken = default)
    {
        var actor = await database.Actors
            .AsNoTracking()
            .Include(row => row.Aliases)
            .Include(row => row.Images)
            .SingleOrDefaultAsync(row => row.PrdbActorId == actorId, cancellationToken);

        if (actor is null)
        {
            return null;
        }

        var credits = database.VideoActors.Where(credit =>
            credit.PrdbActorId == actor.PrdbActorId &&
            credit.Video.Availability != VideoAvailability.Removed);
        var total = await credits.CountAsync(cancellationToken);
        var ids = await credits
            // The Library's own default: what arrived most recently leads.
            .OrderByDescending(credit => credit.Video.DiscoveryDate)
            .ThenBy(credit => credit.VideoId)
            .Take(VideosOnAPage)
            .Select(credit => credit.VideoId)
            .ToListAsync(cancellationToken);
        var creditedNames = await credits
            .Select(credit => credit.Name)
            .Distinct()
            .OrderBy(name => name)
            .ToListAsync(cancellationToken);

        return new ActorDetail(
            actor.PrdbActorId,
            actor.Name,
            actor.ProfileState,
            VideoPresentation.AsOffset(actor.FetchedAt),
            actor.GenderLabel,
            VideoPresentation.AsOffset(actor.Birthday),
            actor.BirthdayPrecisionLabel,
            VideoPresentation.AsOffset(actor.Deathday),
            actor.Birthplace,
            actor.HaircolourLabel,
            actor.EyecolourLabel,
            actor.BreastTypeLabel,
            actor.HeightCentimetres,
            actor.BraSizeLabel,
            actor.WaistCentimetres,
            actor.HipCentimetres,
            actor.NationalityLabel,
            actor.EthnicityLabel,
            actor.CareerStart,
            actor.CareerEnd,
            actor.Tattoos,
            actor.Piercings,
            actor.Aliases.OrderBy(alias => alias.NormalizedName).Select(alias => alias.Name).ToArray(),
            RetainedActorProfile.Links(actor.LinksJson),
            RetainedActorProfile.Bios(actor.BiosJson),
            actor.Images
                .Where(image => image.State == ActorImageState.Retained &&
                                image.PublicImageId != null)
                .OrderBy(image => image.Kind)
                .ThenBy(image => image.Position)
                .Select(image => new ActorImageView(
                    PortraitUrl(image.PublicImageId)!,
                    image.KindLabel))
                .ToArray(),
            actor.OfferedImageCount,
            await library.LoadAsync(accountId, clientContextKey, ids, cancellationToken),
            total,
            creditedNames,
            await database.PersonalActorStates.AnyAsync(
                state => state.AccountId == accountId &&
                         state.PrdbActorId == actor.PrdbActorId,
                cancellationToken));
    }

    private static string? PortraitUrl(Guid? publicImageId) =>
        publicImageId is null ? null : $"/media/actors/{publicImageId}";
}
