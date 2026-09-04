using Microsoft.Extensions.DependencyInjection;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Library;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

/// <summary>
/// What the API answers for an Actor and for an index of them.
///
/// The state to hold on to is the incomplete one: an Actor exists the moment a credit resolves to
/// them, so "no profile, no pictures, one Video" is not an edge case but what a fresh installation
/// looks like for as long as the lane takes.
/// </summary>
public sealed class ActorDiscoveryTests
{
    private static string ActorIdOf(string name) => FixtureIdentificationClient.Actor(name).Id!;

    [Fact]
    public async Task An_actor_with_no_profile_still_answers_with_their_videos()
    {
        await using var store = await CreateAsync(new FixtureActorProfileClient());
        var accountId = await LibraryOfAsync(store);

        var actor = await ActorAsync(store, ActorIdOf("Alex Doe"), accountId);

        Assert.NotNull(actor);

        // Named from the credit, because prdb has said nothing about them at all.
        Assert.Equal("Alex Doe", actor.Name);
        Assert.Equal(ActorProfileState.Unavailable, actor.ProfileState);
        Assert.Null(actor.GenderLabel);
        Assert.Empty(actor.Images);

        // And the half that is always there: the Videos this library holds them in, as the
        // Library's own cards, with this Account's Personal State on them.
        Assert.Equal(2, actor.TotalVideos);
        Assert.Equal(
            ["A Second Work", "A Known Work"],
            actor.Videos.Select(video => video.DisplayTitle));
        Assert.All(actor.Videos, video => Assert.NotNull(video.PersonalState));
    }

    [Fact]
    public async Task An_actors_page_carries_what_prdb_says_and_where_a_link_leads()
    {
        var profiles = new FixtureActorProfileClient().Describes(
            ActorIdOf("Alex Doe"),
            "Alexandra Doe",
            images: [new RemoteActorImage("i1", "https://example.invalid/one.jpg", 1, "Thumbnail")],
            aliases: [new RemoteActorAlias("Alex D", null)]);
        await using var store = await CreateAsync(profiles);
        var accountId = await LibraryOfAsync(store);

        var actor = await ActorAsync(store, ActorIdOf("Alex Doe"), accountId);

        Assert.NotNull(actor);
        Assert.Equal("Alexandra Doe", actor.Name);
        Assert.Equal("Female", actor.GenderLabel);
        Assert.Equal(170, actor.HeightCentimetres);
        Assert.Equal(["Alex D"], actor.Aliases);
        Assert.Equal(["Twitter"], actor.Links.Select(link => link.SiteLabel));
        Assert.Single(actor.Bios);

        // Held here and addressed by a random identifier, so the page is one origin and prdb never
        // sees who looked at whom.
        var image = Assert.Single(actor.Images);
        Assert.StartsWith("/media/actors/", image.Url);
        Assert.Equal("Thumbnail", image.KindLabel);

        // prdb leads with a name no Video here uses, and the Library's facet is keyed by the name
        // the Videos use, so a link into the Library has to carry that one.
        Assert.Equal(["Alex Doe"], actor.CreditedNames);
    }

    [Fact]
    public async Task The_index_offers_the_actors_this_library_credits_and_says_who_is_waiting()
    {
        await using var store = await CreateAsync(new FixtureActorProfileClient()
            .Describes(ActorIdOf("Alex Doe"), "Alex Doe"));
        await LibraryOfAsync(store);

        var index = await IndexAsync(store, new ActorIndexRequest());

        Assert.Equal(2, index.TotalMatches);
        Assert.Equal(["Alex Doe", "Sam Roe"], index.Actors.Select(actor => actor.Name));

        // Alex Doe is in both Videos, Sam Roe in one, and the count is the only number on that
        // screen that means anything to the person reading it.
        Assert.Equal([2, 1], index.Actors.Select(actor => actor.VideoCount));

        // An index of names with no pictures is a plausible grid of grey rectangles, so it says
        // how many are still waiting rather than leaving it to be guessed.
        Assert.Equal(0, index.AwaitingProfiles);
        Assert.False(index.HasMore);
    }

    [Fact]
    public async Task The_index_finds_an_actor_by_a_name_they_are_also_credited_under()
    {
        var profiles = new FixtureActorProfileClient().Describes(
            ActorIdOf("Alex Doe"),
            "Alexandra Doe",
            aliases: [new RemoteActorAlias("Sasha Q", null)]);
        await using var store = await CreateAsync(profiles);
        await LibraryOfAsync(store);

        var found = await IndexAsync(store, new ActorIndexRequest { Query = "sasha" });

        // Somebody types the name they know, which is not always the one prdb leads with.
        Assert.Equal(["Alexandra Doe"], found.Actors.Select(actor => actor.Name));
    }

    [Fact]
    public async Task The_index_can_be_ordered_by_how_many_videos_an_actor_has_here()
    {
        await using var store = await CreateAsync(new FixtureActorProfileClient());
        await LibraryOfAsync(store);

        var index = await IndexAsync(
            store,
            new ActorIndexRequest { Sort = ActorSortOrder.MostHere });

        Assert.Equal(["Alex Doe", "Sam Roe"], index.Actors.Select(actor => actor.Name));
    }

    [Fact]
    public async Task An_actor_this_library_does_not_credit_has_no_page_and_no_row()
    {
        await using var store = await CreateAsync(new FixtureActorProfileClient());
        var accountId = await LibraryOfAsync(store);

        // A credit that resolves to nobody is the ordinary state of a library identified before
        // Actors had an identity. It is reached through the Library's facet, not through an
        // address of its own, because there is nothing behind it to open.
        Assert.Null(await ActorAsync(
            store,
            "6f1a2c34-0000-4000-8000-0000000000ff",
            accountId));
    }

    private static async Task<ActorDetail?> ActorAsync(
        TestDatabase store,
        string actorId,
        Guid accountId)
    {
        await using var scope = store.Scope();

        return await scope.ServiceProvider
            .GetRequiredService<ActorDiscovery>()
            .GetAsync(
                actorId,
                accountId,
                LibraryPipeline.ClientContext,
                TestContext.Current.CancellationToken);
    }

    private static async Task<ActorIndexPage> IndexAsync(
        TestDatabase store,
        ActorIndexRequest request)
    {
        await using var scope = store.Scope();

        return await scope.ServiceProvider
            .GetRequiredService<ActorDiscovery>()
            .IndexAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Two identified Videos: one crediting two Actors, one crediting the first of them again. It
    /// is the smallest library in which an index has an order and a count worth reading.
    /// </summary>
    private static async Task<Guid> LibraryOfAsync(TestDatabase store)
    {
        var source = Path.Combine(store.LibraryMountRoot.Path, "source");
        Directory.CreateDirectory(source);

        foreach (var name in new[] { "first.mp4", "second.mp4" })
        {
            await File.WriteAllBytesAsync(
                Path.Combine(source, name),
                [1, 2, 3, (byte)name.Length],
                TestContext.Current.CancellationToken);
        }

        await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");
        await LibraryPipeline.DrainAsync(store);
        return await LibraryPipeline.AccountAsync(store);
    }

    private static Task<TestDatabase> CreateAsync(FixtureActorProfileClient profiles)
    {
        var identification = new FixtureIdentificationClient()
            .Credits(
                "first.mp4",
                "6f1a2c34-0000-4000-8000-000000000001",
                "A Known Work",
                ["Alex Doe", "Sam Roe"])
            .Credits(
                "second.mp4",
                "6f1a2c34-0000-4000-8000-000000000002",
                "A Second Work",
                ["Alex Doe"]);

        return TestDatabase.CreateAsync(
            mediaProbe: new FixtureProbe(),
            hasher: new FixtureHasher(),
            previewGenerator: new FixturePreviewGenerator(),
            identificationClient: identification,
            actorProfileClient: profiles);
    }
}
