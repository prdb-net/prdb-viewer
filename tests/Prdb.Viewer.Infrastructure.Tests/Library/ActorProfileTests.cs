using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

/// <summary>
/// What prdb says about the Actors this library credits, held so that an Actor's page reads
/// through an outage (ADR 0020).
///
/// The state that matters most is the incomplete one: an Actor exists here the moment a credit
/// resolves to them, long before any profile arrives, and the page has to be a page in that state
/// too. So does an Actor prdb has nothing to say about.
/// </summary>
public sealed class ActorProfileTests
{
    private const string WorkId = "6f1a2c34-0000-4000-8000-000000000001";

    private static readonly string ActorId =
        FixtureIdentificationClient.Actor("Alex Doe").Id!;

    [Fact]
    public async Task A_credit_creates_the_actor_before_any_profile_arrives()
    {
        // prdb answers nothing about this Actor, which is the outage case and the ordinary case
        // on an installation whose lane has not caught up.
        var profiles = new FixtureActorProfileClient();
        await using var store = await CreateAsync(profiles);
        await IdentifiedAsync(store);

        await using var scope = store.Scope();
        var actor = Assert.Single(await ActorsAsync(scope));
        Assert.Equal(ActorId, actor.PrdbActorId);

        // Named from the credit, so the Actor can be named and their Videos listed with no profile
        // at all.
        Assert.Equal("Alex Doe", actor.Name);
        Assert.Equal("alex doe", actor.NormalizedName);
        Assert.Equal(ActorProfileState.Unavailable, actor.ProfileState);
    }

    [Fact]
    public async Task What_prdb_says_about_an_actor_is_retained_in_its_own_words()
    {
        var profiles = new FixtureActorProfileClient().Describes(
            ActorId,
            "Alexandra Doe",
            images:
            [
                new RemoteActorImage("image-2", "https://example.invalid/poster.jpg", 2, "Poster"),
                new RemoteActorImage("image-1", "https://example.invalid/thumb.jpg", 1, "Thumbnail"),
            ],
            aliases: [new RemoteActorAlias("Alex Doe", "site-1")]);
        await using var store = await CreateAsync(profiles);
        await IdentifiedAsync(store);

        await using var scope = store.Scope();
        var actor = Assert.Single(await ActorsAsync(scope));
        Assert.Equal(ActorProfileState.Retained, actor.ProfileState);

        // prdb's own name for the Actor replaces the credit's spelling of it, and the normalised
        // form follows, because the index is searched by it.
        Assert.Equal("Alexandra Doe", actor.Name);
        Assert.Equal("alexandra doe", actor.NormalizedName);

        // The labels prdb sends are what is kept. Nothing here translates an enumeration, so
        // nothing here prints "Unknown (7)" the day prdb learns an eighth value.
        Assert.Equal("Female", actor.GenderLabel);
        Assert.Equal("Brown", actor.HaircolourLabel);
        Assert.Equal("Exact", actor.BirthdayPrecisionLabel);
        Assert.Equal(170, actor.HeightCentimetres);
        Assert.Equal(2014, actor.CareerStart);
        Assert.Equal("Example City", actor.Birthplace);
        Assert.NotNull(actor.LinksJson);
        Assert.NotNull(actor.BiosJson);

        // The name somebody would search for is the one they know, which is not always the one
        // prdb leads with, so an alias is a row of its own.
        var alias = Assert.Single(actor.Aliases);
        Assert.Equal("Alex Doe", alias.Name);
        Assert.Equal("alex doe", alias.NormalizedName);
        Assert.Equal("site-1", alias.PrdbSiteId);

        // Every picture is recorded, in prdb's order rather than in one this application invents,
        // because that order is what decides which of them is the Portrait.
        Assert.Equal(
            [("image-2", 2), ("image-1", 1)],
            actor.Images.OrderBy(image => image.Position)
                .Select(image => (image.PrdbImageId, image.Kind)));
        Assert.Equal(2, actor.OfferedImageCount);
    }

    [Fact]
    public async Task An_actor_already_described_is_not_asked_about_again()
    {
        var profiles = new FixtureActorProfileClient().Describes(ActorId, "Alex Doe");
        await using var store = await CreateAsync(profiles);
        var directoryId = await IdentifiedAsync(store);
        var asked = profiles.Calls;

        await LibraryPipeline.RescanAsync(store, directoryId);

        Assert.Equal(asked, profiles.Calls);
    }

    [Fact]
    public async Task A_changed_picture_is_asked_for_again_and_not_served_in_the_meantime()
    {
        var profiles = new FixtureActorProfileClient().Describes(
            ActorId,
            "Alex Doe",
            images: [new RemoteActorImage("image-1", "https://example.invalid/one.jpg", 1, "Thumbnail")]);
        await using var store = await CreateAsync(profiles);
        await IdentifiedAsync(store);

        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            await database.ActorImages.ExecuteUpdateAsync(
                update => update.SetProperty(image => image.State, ActorImageState.Retained),
                TestContext.Current.CancellationToken);

            // The profile ages past its horizon, which is the only thing that makes prdb worth
            // asking about an Actor it has already answered for.
            await database.Actors.ExecuteUpdateAsync(
                update => update.SetProperty(
                    actor => actor.FetchedAt,
                    DateTime.UtcNow - ActorProfileRetention.RefreshHorizon - TimeSpan.FromDays(1)),
                TestContext.Current.CancellationToken);
        }

        profiles.Describes(
            ActorId,
            "Alex Doe",
            images: [new RemoteActorImage("image-1", "https://example.invalid/two.jpg", 1, "Thumbnail")]);

        await using (var scope = store.Scope())
        {
            await scope.ServiceProvider
                .GetRequiredService<ActorProfileRetention>()
                .RetainAsync("installation-key", TestContext.Current.CancellationToken);
        }

        await using var verification = store.Scope();
        var image = Assert.Single(await verification.ServiceProvider
            .GetRequiredService<ViewerDbContext>()
            .ActorImages
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal("https://example.invalid/two.jpg", image.SourceUrl);

        // The bytes on disk are still there, but they are no longer the picture prdb offers.
        Assert.Equal(ActorImageState.Pending, image.State);
    }

    [Fact]
    public async Task An_outage_while_asking_about_actors_leaves_them_pending_and_raises_nothing()
    {
        var profiles = new FixtureActorProfileClient
        {
            Status = ActorProfileFetchStatus.Unavailable,
        };
        await using var store = await CreateAsync(profiles);
        await IdentifiedAsync(store);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var actor = Assert.Single(await ActorsAsync(scope));
        Assert.Equal(ActorProfileState.Pending, actor.ProfileState);
        Assert.Null(actor.FetchedAt);

        // Identification succeeded and the library is browsable. A profile that has not arrived is
        // a paragraph the page does not print, not an Administrator's attention.
        Assert.Empty(await database.WorkIssues
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Every_picture_prdb_offers_is_held_and_served_from_here()
    {
        var profiles = new FixtureActorProfileClient().Describes(
            ActorId,
            "Alex Doe",
            images:
            [
                new RemoteActorImage("image-1", "https://example.invalid/thumb.jpg", 1, "Thumbnail"),
                new RemoteActorImage("image-2", "https://example.invalid/poster.jpg", 2, "Poster"),
            ]);
        var pictures = new FixtureRetainedImageClient();
        await using var store = await CreateAsync(profiles, pictures);
        await IdentifiedAsync(store);

        await using var scope = store.Scope();
        var actor = Assert.Single(await ActorsAsync(scope));
        Assert.All(actor.Images, image =>
        {
            Assert.Equal(ActorImageState.Retained, image.State);
            Assert.NotNull(image.PublicImageId);
            Assert.NotNull(image.RelativePath);
            Assert.Equal("image/png", image.ContentType);
        });

        // Served from this installation's own origin, by the random identifier rather than by a
        // path, a database key, or the address prdb offers.
        var portrait = actor.Images.OrderBy(image => image.Kind).First();
        var delivered = await scope.ServiceProvider
            .GetRequiredService<PreviewDeliveryService>()
            .OpenActorImageAsync(portrait.PublicImageId!.Value, TestContext.Current.CancellationToken);
        Assert.NotNull(delivered);
        await using (delivered.Content)
        {
            Assert.Equal("image/png", delivered.ContentType);
        }
    }

    [Fact]
    public async Task A_picture_that_did_not_arrive_is_tried_again_at_the_next_refresh()
    {
        var profiles = new FixtureActorProfileClient().Describes(
            ActorId,
            "Alex Doe",
            images: [new RemoteActorImage("image-1", "https://example.invalid/thumb.jpg", 1, "Thumbnail")]);
        var pictures = new FixtureRetainedImageClient { Answers = false };
        await using var store = await CreateAsync(profiles, pictures);
        await IdentifiedAsync(store);

        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            var missing = Assert.Single(await database.ActorImages
                .ToListAsync(TestContext.Current.CancellationToken));

            // The gallery says a picture has not arrived. Nothing else happens: identification
            // succeeded and the library is browsable, so this is not an Administrator's concern.
            Assert.Equal(ActorImageState.Unavailable, missing.State);
            Assert.Empty(await database.WorkIssues
                .ToListAsync(TestContext.Current.CancellationToken));

            await database.Actors.ExecuteUpdateAsync(
                update => update.SetProperty(
                    actor => actor.FetchedAt,
                    DateTime.UtcNow - ActorProfileRetention.RefreshHorizon - TimeSpan.FromDays(1)),
                TestContext.Current.CancellationToken);
        }

        pictures.Answers = true;

        await using (var scope = store.Scope())
        {
            await scope.ServiceProvider
                .GetRequiredService<ActorProfileRetention>()
                .RetainAsync("installation-key", TestContext.Current.CancellationToken);
            await scope.ServiceProvider
                .GetRequiredService<ActorImageRetention>()
                .RetainAsync(TestContext.Current.CancellationToken);
        }

        // A brief outage costs an Actor their gallery for a while rather than for good.
        await using var verification = store.Scope();
        var image = Assert.Single(await verification.ServiceProvider
            .GetRequiredService<ViewerDbContext>()
            .ActorImages
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(ActorImageState.Retained, image.State);
    }

    [Fact]
    public async Task More_pictures_than_are_held_are_counted_rather_than_hidden()
    {
        var offered = Enumerable.Range(0, ActorProfileRetention.MaximumImages + 5)
            .Select(index => new RemoteActorImage(
                $"image-{index}",
                $"https://example.invalid/{index}.jpg",
                1,
                "Thumbnail"))
            .ToArray();
        var profiles = new FixtureActorProfileClient().Describes(ActorId, "Alex Doe", images: offered);
        await using var store = await CreateAsync(profiles, new FixtureRetainedImageClient());
        await IdentifiedAsync(store);

        await using var scope = store.Scope();
        var actor = Assert.Single(await ActorsAsync(scope));
        Assert.Equal(ActorProfileRetention.MaximumImages, actor.Images.Count);

        // One Actor with an unexpected three hundred pictures must not fill the data directory,
        // and a capped gallery presented as the whole of one is a lie about the catalogue.
        Assert.Equal(offered.Length, actor.OfferedImageCount);
    }

    private static async Task<IReadOnlyList<ActorRow>> ActorsAsync(AsyncServiceScope scope) =>
        await scope.ServiceProvider
            .GetRequiredService<ViewerDbContext>()
            .Actors
            .Include(actor => actor.Aliases)
            .Include(actor => actor.Images)
            .OrderBy(actor => actor.NormalizedName)
            .ToListAsync(TestContext.Current.CancellationToken);

    private static async Task<Guid> IdentifiedAsync(TestDatabase store)
    {
        var source = Path.Combine(store.LibraryMountRoot.Path, "source");
        Directory.CreateDirectory(source);
        await File.WriteAllBytesAsync(
            Path.Combine(source, "first.mp4"),
            [1, 2, 3, 4],
            TestContext.Current.CancellationToken);
        var directoryId = await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");
        await LibraryPipeline.DrainAsync(store);
        return directoryId;
    }

    private static Task<TestDatabase> CreateAsync(
        FixtureActorProfileClient profiles,
        FixtureRetainedImageClient? pictures = null) =>
        TestDatabase.CreateAsync(
            mediaProbe: new FixtureProbe(),
            hasher: new FixtureHasher(),
            previewGenerator: new FixturePreviewGenerator(),
            identificationClient: new FixtureIdentificationClient()
                .Conclusive("first.mp4", WorkId, "A Known Work", new RemoteSite("s1", "Example Site", null)),
            actorProfileClient: profiles,
            retainedImageClient: pictures);
}
