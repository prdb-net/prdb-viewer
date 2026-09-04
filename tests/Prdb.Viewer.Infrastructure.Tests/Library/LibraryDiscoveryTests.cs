using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Viewer.Core.Access;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Core.Personal;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

public sealed class LibraryDiscoveryTests
{
    private const string WorkId = "6f1a2c34-0000-4000-8000-000000000001";

    [Fact]
    public async Task Ordinary_discovery_shows_ready_videos_newest_first_and_counts_what_it_hides()
    {
        await using var store = await CreateAsync(new FixtureIdentificationClient()
            .Conclusive("second.mp4", WorkId, "A Known Work", new RemoteSite("s1", "Example Site", null)));
        var accountId = await AccountAsync(store);
        await SourceAsync(store, ("first.mp4", "mp4"), ("second.mp4", "mp4"), ("third.mkv", "matroska"));
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");
        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        var page = await Discovery(scope).GetAsync(
            accountId,
            LibraryPipeline.ClientContext,
            new LibraryDiscoveryRequest(),
            TestContext.Current.CancellationToken);

        // The matroska file is Undetermined, so it is not ready and stays out until asked for.
        Assert.Equal(2, page.TotalMatches);
        Assert.Equal(1, page.HiddenNotReadyForDirectPlay);
        Assert.Equal(0, page.HiddenUnavailable);
        Assert.False(page.IncludesNotReadyForDirectPlay);
        Assert.False(page.HasMore);

        // Newest first: the later Discovery Date leads.
        Assert.Equal(
            ["A Known Work", "first"],
            page.Videos.Select(video => video.DisplayTitle));
    }

    [Fact]
    public async Task The_personal_preference_widens_results_and_an_explicit_filter_overrides_it()
    {
        await using var store = await CreateAsync();
        var accountId = await AccountAsync(store);
        await SourceAsync(store, ("ready.mp4", "mp4"), ("uncertain.mkv", "matroska"));
        await LibraryPipeline.DrainAsync(store);

        await using (var scope = store.Scope())
        {
            await scope.ServiceProvider
                .GetRequiredService<LibraryPreferences>()
                .SetIncludesNotReadyForDirectPlayAsync(
                    accountId,
                    true,
                    TestContext.Current.CancellationToken);
        }

        await using var verification = store.Scope();
        var widened = await Discovery(verification).GetAsync(
            accountId,
            LibraryPipeline.ClientContext,
            new LibraryDiscoveryRequest(),
            TestContext.Current.CancellationToken);
        Assert.Equal(2, widened.TotalMatches);
        Assert.True(widened.IncludesNotReadyForDirectPlay);
        Assert.Equal(0, widened.HiddenNotReadyForDirectPlay);

        // The explicit filter decides for this view even though the preference is on.
        var filtered = await Discovery(verification).GetAsync(
            accountId,
            LibraryPipeline.ClientContext,
            new LibraryDiscoveryRequest
            {
                Playability = [ClientVideoPlayability.NotDirectlyPlayable],
            },
            TestContext.Current.CancellationToken);
        Assert.Equal("uncertain", Assert.Single(filtered.Videos).DisplayTitle);
    }

    [Fact]
    public async Task Search_finds_established_and_local_facts_and_ignores_how_they_are_written()
    {
        await using var store = await CreateAsync(new FixtureIdentificationClient()
            .Conclusive("known.mp4", WorkId, "A Känown Work", new RemoteSite("s1", "Example Site", null)));
        var accountId = await AccountAsync(store);
        await SourceAsync(store, ("known.mp4", "mp4"), ("Beach Day 2019.mp4", "mp4"));
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");
        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();

        // Diacritics and case do not matter, and the Established Actor is searchable.
        Assert.Equal("A Känown Work", Assert.Single(
            (await Search(scope, accountId, "kanown")).Videos).DisplayTitle);
        Assert.Equal("A Känown Work", Assert.Single(
            (await Search(scope, accountId, "alex doe")).Videos).DisplayTitle);
        Assert.Equal("A Känown Work", Assert.Single(
            (await Search(scope, accountId, "example site")).Videos).DisplayTitle);

        // An Unknown Video is found by its local label.
        Assert.Equal("Beach Day 2019", Assert.Single(
            (await Search(scope, accountId, "beach 2019")).Videos).DisplayTitle);

        // Every term has to match somewhere.
        Assert.Empty((await Search(scope, accountId, "beach nonsense")).Videos);
    }

    [Fact]
    public async Task Facets_combine_with_or_inside_and_and_across()
    {
        await using var store = await CreateAsync(new FixtureIdentificationClient()
            .Conclusive("known.mp4", WorkId, "A Known Work", new RemoteSite("s1", "Example Site", null)));
        var accountId = await AccountAsync(store);
        await SourceAsync(store, ("known.mp4", "mp4"), ("unknown.mp4", "mp4"));
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");
        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        var established = await Discovery(scope).GetAsync(
            accountId,
            LibraryPipeline.ClientContext,
            new LibraryDiscoveryRequest
            {
                WorkIdentification = [IdentificationResolution.Established],
            },
            TestContext.Current.CancellationToken);
        Assert.Equal("A Known Work", Assert.Single(established.Videos).DisplayTitle);

        var unknown = await Discovery(scope).GetAsync(
            accountId,
            LibraryPipeline.ClientContext,
            new LibraryDiscoveryRequest { UnknownSite = true },
            TestContext.Current.CancellationToken);
        Assert.Equal("unknown", Assert.Single(unknown.Videos).DisplayTitle);

        // Site AND Established work: both hold for one Video only.
        var both = await Discovery(scope).GetAsync(
            accountId,
            LibraryPipeline.ClientContext,
            new LibraryDiscoveryRequest
            {
                Sites = ["Example Site"],
                WorkIdentification = [IdentificationResolution.Established],
            },
            TestContext.Current.CancellationToken);
        Assert.Single(both.Videos);

        // Site AND Unknown work: nothing satisfies both.
        var neither = await Discovery(scope).GetAsync(
            accountId,
            LibraryPipeline.ClientContext,
            new LibraryDiscoveryRequest
            {
                Sites = ["Example Site"],
                WorkIdentification = [IdentificationResolution.Unknown],
            },
            TestContext.Current.CancellationToken);
        Assert.Empty(neither.Videos);

        var facets = await Discovery(scope).GetFacetsAsync(
            accountId,
            new LibraryDiscoveryRequest(),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("Example Site", Assert.Single(facets.Sites).Value);
        Assert.Equal("Alex Doe", Assert.Single(facets.Actors).Value);
    }

    [Fact]
    public async Task Video_quality_narrows_the_library_orders_it_and_offers_the_bands_it_holds()
    {
        await using var store = await TestDatabase.CreateAsync(
            mediaProbe: new DimensionAwareProbe(),
            hasher: new FixtureHasher(),
            previewGenerator: new FixturePreviewGenerator(),
            identificationClient: new FixtureIdentificationClient());
        var accountId = await AccountAsync(store);
        await SourceAsync(store, ("uhd.mp4", "mp4"), ("fullhd.mp4", "mp4"), ("sd.mp4", "mp4"));
        await LibraryPipeline.DrainAsync(store);

        // A 4K picture is past the conservative baseline whatever it is encoded as, so this
        // Account asks to see what its browser has not confirmed. The test is about Video Quality,
        // and admission is a separate question with its own tests.
        await using (var preference = store.Scope())
        {
            await preference.ServiceProvider
                .GetRequiredService<LibraryPreferences>()
                .SetIncludesNotReadyForDirectPlayAsync(
                    accountId,
                    true,
                    TestContext.Current.CancellationToken);
        }

        await using var scope = store.Scope();

        var uhd = await Discovery(scope).GetAsync(
            accountId,
            LibraryPipeline.ClientContext,
            new LibraryDiscoveryRequest { Quality = [VideoQualityBand.Uhd2160] },
            TestContext.Current.CancellationToken);
        Assert.Equal("uhd", Assert.Single(uhd.Videos).DisplayTitle);

        // Values inside one facet combine with OR, as everywhere else.
        var either = await Discovery(scope).GetAsync(
            accountId,
            LibraryPipeline.ClientContext,
            new LibraryDiscoveryRequest
            {
                Quality = [VideoQualityBand.Uhd2160, VideoQualityBand.StandardDefinition],
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(["sd", "uhd"], either.Videos.Select(video => video.DisplayTitle).Order());

        var ordered = await Discovery(scope).GetAsync(
            accountId,
            LibraryPipeline.ClientContext,
            new LibraryDiscoveryRequest { Sort = LibrarySortOrder.QualityDescending },
            TestContext.Current.CancellationToken);
        Assert.Equal(
            ["uhd", "fullhd", "sd"],
            ordered.Videos.Select(video => video.DisplayTitle));

        // The facet offers the bands the library holds, best first, with what each one holds.
        var facets = await Discovery(scope).GetFacetsAsync(
            accountId,
            new LibraryDiscoveryRequest(),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(
            [
                (VideoQualityBand.Uhd2160, 1),
                (VideoQualityBand.FullHd1080, 1),
                (VideoQualityBand.StandardDefinition, 1),
            ],
            facets.Quality.Select(band => (band.Value, band.Count)));

        // What a card names is the band the filter admitted it by, not a second opinion.
        Assert.Equal(
            VideoQualityBand.Uhd2160,
            Assert.Single(Assert.Single(uhd.Videos).VideoFiles).QualityBand);
    }

    [Fact]
    public async Task Facets_count_what_the_current_narrowing_leaves_except_their_own()
    {
        const string otherWork = "6f1a2c34-0000-4000-8000-000000000002";
        await using var store = await CreateAsync(new FixtureIdentificationClient()
            .Conclusive("one.mp4", WorkId, "Work One", new RemoteSite("s1", "Site One", null))
            .Conclusive("two.mp4", otherWork, "Work Two", new RemoteSite("s2", "Site Two", null)));
        var accountId = await AccountAsync(store);
        await SourceAsync(store, ("one.mp4", "mp4"), ("two.mp4", "mp4"), ("unknown.mp4", "mp4"));
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");
        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();

        // Nothing chosen: every Site the library holds, and the Actor both Videos share.
        var open = await Discovery(scope).GetFacetsAsync(
            accountId,
            new LibraryDiscoveryRequest(),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(
            [("Site One", 1), ("Site Two", 1)],
            open.Sites.Select(site => (site.Value, site.Count)));
        Assert.Equal(("Alex Doe", 2), Assert.Single(open.Actors.Select(a => (a.Value, a.Count))));

        // A Site chosen narrows what the Actors count, but not the Sites on offer: a second Site
        // would widen the set, so the first one chosen must not hide it.
        var narrowed = await Discovery(scope).GetFacetsAsync(
            accountId,
            new LibraryDiscoveryRequest { Sites = ["Site One"] },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(
            [("Site One", 1), ("Site Two", 1)],
            narrowed.Sites.Select(site => (site.Value, site.Count)));
        Assert.Equal(("Alex Doe", 1), Assert.Single(narrowed.Actors.Select(a => (a.Value, a.Count))));

        // The search narrows every facet, because it is not one of them.
        var searched = await Discovery(scope).GetFacetsAsync(
            accountId,
            new LibraryDiscoveryRequest { Query = "two" },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(("Site Two", 1), Assert.Single(searched.Sites.Select(s => (s.Value, s.Count))));
        Assert.Equal(("Alex Doe", 1), Assert.Single(searched.Actors.Select(a => (a.Value, a.Count))));
    }

    /// <summary>
    /// One conclusive answer with a Site and an Actor of its own. `Conclusive` names every Video
    /// the same Actor, which is what a test about counting wants and what a test about telling two
    /// values apart cannot use.
    /// </summary>
    private static RemoteIdentification Work(
        Guid videoFileId,
        string prdbVideoId,
        string title,
        string site,
        string actor)
    {
        var remoteSite = new RemoteSite(site.ToLowerInvariant(), site, null);

        return new RemoteIdentification(
            videoFileId,
            RemoteMatchKind.OsHash,
            RemoteMatchConfidence.Exact,
            prdbVideoId,
            [],
            remoteSite,
            new RemoteWork(prdbVideoId, title, remoteSite, [actor], null, null, 12_345));
    }

    [Fact]
    public async Task Looking_inside_a_facet_narrows_its_own_values_and_nothing_else()
    {
        const string otherWork = "6f1a2c34-0000-4000-8000-000000000003";
        // A Site whose name carries a diacritic and an apostrophe, because the promise is that
        // looking for a facet value answers the way the Library's own search does.
        await using var store = await CreateAsync(new FixtureIdentificationClient()
            .Answer("one.mp4", id => Work(id, WorkId, "Work One", "Café O'Neill", "Alex Doe"))
            .Answer("two.mp4", id => Work(id, otherWork, "Work Two", "Second Studio", "Sam Roe")));
        var accountId = await AccountAsync(store);
        await SourceAsync(store, ("one.mp4", "mp4"), ("two.mp4", "mp4"));
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");
        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();

        // Case, diacritics and punctuation are folded, so the name as it is written is not the
        // only way to ask for it.
        var accented = await Discovery(scope).GetFacetsAsync(
            accountId,
            new LibraryDiscoveryRequest(),
            new LibraryFacetSearch { Sites = "cafe oneill" },
            TestContext.Current.CancellationToken);
        Assert.Equal("Café O'Neill", Assert.Single(accented.Sites).Value);

        // The term narrows the facet it was typed into and leaves the others whole: it says which
        // values are offered, not which Videos match.
        Assert.Equal(
            ["Alex Doe", "Sam Roe"],
            accented.Actors.Select(actor => actor.Value).Order());
        Assert.False(accented.MoreSites);
        Assert.False(accented.MoreActors);

        var actor = await Discovery(scope).GetFacetsAsync(
            accountId,
            new LibraryDiscoveryRequest(),
            new LibraryFacetSearch { Actors = "roe" },
            TestContext.Current.CancellationToken);
        Assert.Equal("Sam Roe", Assert.Single(actor.Actors).Value);
        Assert.Equal(2, actor.Sites.Count);

        // A term nothing matches is an empty facet rather than the unnarrowed list: offering every
        // Site to someone who asked for one that is not there would answer a question they did not
        // ask.
        var absent = await Discovery(scope).GetFacetsAsync(
            accountId,
            new LibraryDiscoveryRequest(),
            new LibraryFacetSearch { Sites = "nothing here" },
            TestContext.Current.CancellationToken);
        Assert.Empty(absent.Sites);

        // The narrowing still applies alongside it: what is looked for cannot reach a value the
        // current narrowing leaves nothing of.
        var narrowed = await Discovery(scope).GetFacetsAsync(
            accountId,
            new LibraryDiscoveryRequest { Query = "two" },
            new LibraryFacetSearch { Sites = "cafe" },
            TestContext.Current.CancellationToken);
        Assert.Empty(narrowed.Sites);
    }

    [Fact]
    public async Task The_library_orders_by_runtime_recent_play_and_personal_rating()
    {
        await using var store = await TestDatabase.CreateAsync(
            mediaProbe: new RuntimeAwareProbe(),
            hasher: new FixtureHasher(),
            previewGenerator: new FixturePreviewGenerator(),
            identificationClient: new FixtureIdentificationClient());
        var accountId = await AccountAsync(store);
        await SourceAsync(store, ("short.mp4", "mp4"), ("long.mp4", "mp4"), ("middle.mp4", "mp4"));
        await LibraryPipeline.DrainAsync(store);

        await using (var personal = store.Scope())
        {
            var database = personal.ServiceProvider.GetRequiredService<ViewerDbContext>();
            var videos = await database.Videos
                .ToDictionaryAsync(video => video.DisplayLabel, TestContext.Current.CancellationToken);
            var now = DateTime.SpecifyKind(new DateTime(2026, 9, 1, 12, 0, 0), DateTimeKind.Utc);

            // The short one was played most recently and rated lowest; the middle one was played
            // earlier and rated highest; the long one has no Personal State at all.
            database.PersonalVideoStates.AddRange(
                new PersonalVideoStateRow
                {
                    AccountId = accountId,
                    VideoId = videos["short"].Id,
                    PlayState = PersonalPlayState.InProgress,
                    PlayStateChangedAt = now,
                    PersonalRating = 2,
                    UpdatedAt = now,
                },
                new PersonalVideoStateRow
                {
                    AccountId = accountId,
                    VideoId = videos["middle"].Id,
                    PlayState = PersonalPlayState.Completed,
                    PlayStateChangedAt = now.AddDays(-1),
                    PersonalRating = 5,
                    UpdatedAt = now,
                });
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var scope = store.Scope();

        Assert.Equal(
            ["long", "middle", "short"],
            await OrderedAsync(scope, accountId, LibrarySortOrder.LongestFirst));

        // What was never played comes after everything that was.
        Assert.Equal(
            ["short", "middle", "long"],
            await OrderedAsync(scope, accountId, LibrarySortOrder.RecentlyPlayed));

        // What was never rated comes after everything that was.
        Assert.Equal(
            ["middle", "short", "long"],
            await OrderedAsync(scope, accountId, LibrarySortOrder.BestRated));
    }

    private static async Task<IEnumerable<string>> OrderedAsync(
        AsyncServiceScope scope,
        Guid accountId,
        LibrarySortOrder sort)
    {
        var page = await Discovery(scope).GetAsync(
            accountId,
            LibraryPipeline.ClientContext,
            new LibraryDiscoveryRequest { Sort = sort },
            TestContext.Current.CancellationToken);

        return page.Videos.Select(video => video.DisplayTitle);
    }

    [Fact]
    public async Task A_page_costs_a_page_and_says_whether_more_follows()
    {
        await using var store = await CreateAsync();
        var accountId = await AccountAsync(store);
        await SourceAsync(store, Enumerable.Range(0, 7)
            .Select(index => ($"video-{index:00}.mp4", "mp4"))
            .ToArray());
        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        var first = await Discovery(scope).GetAsync(
            accountId,
            LibraryPipeline.ClientContext,
            new LibraryDiscoveryRequest { Take = 3, Sort = LibrarySortOrder.TitleAscending },
            TestContext.Current.CancellationToken);

        Assert.Equal(7, first.TotalMatches);
        Assert.True(first.HasMore);
        Assert.Equal(["video-00", "video-01", "video-02"], first.Videos.Select(v => v.DisplayTitle));

        var last = await Discovery(scope).GetAsync(
            accountId,
            LibraryPipeline.ClientContext,
            new LibraryDiscoveryRequest
            {
                Take = 3,
                Skip = 6,
                Sort = LibrarySortOrder.TitleAscending,
            },
            TestContext.Current.CancellationToken);
        Assert.False(last.HasMore);
        Assert.Equal(["video-06"], last.Videos.Select(v => v.DisplayTitle));
    }

    [Fact]
    public async Task An_unavailable_video_leaves_ordinary_results_and_is_counted_and_findable()
    {
        await using var store = await CreateAsync();
        var accountId = await AccountAsync(store);
        var source = await SourceAsync(store, ("kept.mp4", "mp4"), ("lost.mp4", "mp4"));
        await LibraryPipeline.DrainAsync(store);
        Guid directoryId;

        await using (var setup = store.Scope())
        {
            directoryId = await setup.ServiceProvider
                .GetRequiredService<ViewerDbContext>()
                .LibraryDirectories
                .Select(directory => directory.Id)
                .SingleAsync(TestContext.Current.CancellationToken);
        }

        File.Delete(Path.Combine(source, "lost.mp4"));
        await LibraryPipeline.RescanAsync(store, directoryId);

        await using var scope = store.Scope();
        var ordinary = await Discovery(scope).GetAsync(
            accountId,
            LibraryPipeline.ClientContext,
            new LibraryDiscoveryRequest(),
            TestContext.Current.CancellationToken);
        Assert.Equal("kept", Assert.Single(ordinary.Videos).DisplayTitle);
        Assert.Equal(1, ordinary.HiddenUnavailable);

        var unavailable = await Discovery(scope).GetAsync(
            accountId,
            LibraryPipeline.ClientContext,
            new LibraryDiscoveryRequest
            {
                Availability = [VideoAvailability.Unavailable],
                Playability =
                [
                    ClientVideoPlayability.ReadyForDirectPlay,
                    ClientVideoPlayability.CompatibilityUncertain,
                    ClientVideoPlayability.NotDirectlyPlayable,
                ],
            },
            TestContext.Current.CancellationToken);
        Assert.Equal("lost", Assert.Single(unavailable.Videos).DisplayTitle);
    }

    [Fact]
    public async Task An_unsupported_video_carries_its_title_and_preview_when_it_is_asked_for()
    {
        await using var store = await CreateAsync();
        var accountId = await AccountAsync(store);
        await SourceAsync(store, ("Beach Day 2019.mkv", "matroska"));
        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        var filtered = await Discovery(scope).GetAsync(
            accountId,
            LibraryPipeline.ClientContext,
            new LibraryDiscoveryRequest
            {
                Playability = [ClientVideoPlayability.NotDirectlyPlayable],
            },
            TestContext.Current.CancellationToken);

        // An Unsupported Video is understandable rather than merely absent: it keeps its local
        // label, its generated preview, and the file facts that say why it cannot be played.
        var video = Assert.Single(filtered.Videos);
        Assert.Equal("Beach Day 2019", video.DisplayTitle);
        Assert.StartsWith("/media/previews/", video.PreviewUrl);
        var file = Assert.Single(video.VideoFiles);
        Assert.Equal("matroska", file.ContainerFormat);
        Assert.NotEqual(DirectPlayClassification.BaselineCandidate, file.DirectPlayClassification);
    }

    [Fact]
    public async Task A_direct_address_answers_a_video_ordinary_discovery_keeps_out()
    {
        await using var store = await CreateAsync();
        var accountId = await AccountAsync(store);
        await SourceAsync(store, ("ready.mp4", "mp4"), ("uncertain.mkv", "matroska"));
        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var kept = await database.Videos
            .SingleAsync(
                video => video.DisplayLabel == "uncertain",
                TestContext.Current.CancellationToken);

        // Ordinary Discovery does not offer it, because this client is not ready to play it.
        var page = await Discovery(scope).GetAsync(
            accountId,
            LibraryPipeline.ClientContext,
            new LibraryDiscoveryRequest(),
            TestContext.Current.CancellationToken);
        Assert.DoesNotContain(page.Videos, video => video.Id == kept.Id);

        // Addressing it directly still answers it: the link is the User's own decision to look.
        var detail = await Discovery(scope).GetVideoAsync(
            accountId,
            LibraryPipeline.ClientContext,
            kept.Id,
            TestContext.Current.CancellationToken);

        Assert.NotNull(detail);
        Assert.Equal(kept.Id, detail.Video.Id);
        Assert.Null(detail.SupersededVideoId);
        Assert.NotEqual(ClientVideoPlayability.ReadyForDirectPlay, detail.Video.Playability);
    }

    [Fact]
    public async Task A_direct_address_follows_a_merge_and_refuses_a_removed_video()
    {
        await using var store = await CreateAsync();
        var accountId = await AccountAsync(store);
        await SourceAsync(store, ("survivor.mp4", "mp4"), ("merged.mp4", "mp4"), ("gone.mp4", "mp4"));
        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var survivor = await database.Videos.AsTracking().SingleAsync(
            video => video.DisplayLabel == "survivor",
            TestContext.Current.CancellationToken);
        var merged = await database.Videos.AsTracking().SingleAsync(
            video => video.DisplayLabel == "merged",
            TestContext.Current.CancellationToken);
        var removed = await database.Videos.AsTracking().SingleAsync(
            video => video.DisplayLabel == "gone",
            TestContext.Current.CancellationToken);
        merged.SurvivingVideoId = survivor.Id;
        removed.Availability = VideoAvailability.Removed;
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);

        // A link taken before the merge keeps leading somewhere true, and says where it led.
        var followed = await Discovery(scope).GetVideoAsync(
            accountId,
            LibraryPipeline.ClientContext,
            merged.Id,
            TestContext.Current.CancellationToken);

        Assert.NotNull(followed);
        Assert.Equal(survivor.Id, followed.Video.Id);
        Assert.Equal(merged.Id, followed.SupersededVideoId);

        // What has left the active Library is refused rather than answered.
        Assert.Null(await Discovery(scope).GetVideoAsync(
            accountId,
            LibraryPipeline.ClientContext,
            removed.Id,
            TestContext.Current.CancellationToken));
        Assert.Null(await Discovery(scope).GetVideoAsync(
            accountId,
            LibraryPipeline.ClientContext,
            Guid.CreateVersion7(),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_personal_shelf_narrows_the_library_and_keeps_its_own_order_and_admission()
    {
        await using var store = await TestDatabase.CreateAsync(
            mediaProbe: new ShelfProbe(),
            hasher: new FixtureHasher(),
            previewGenerator: new FixturePreviewGenerator(),
            identificationClient: new FixtureIdentificationClient());
        var accountId = await AccountAsync(store);
        var otherAccountId = await AccountAsync(store, "other");
        await SourceAsync(
            store,
            ("favourite.mp4", "mp4"),
            ("queued-first.mp4", "mp4"),
            ("queued-second.mkv", "matroska"),
            ("watching.mp4", "mp4"),
            ("dismissed.mp4", "mp4"),
            ("nearly-done.mp4", "mp4"),
            ("plain.mp4", "mp4"));
        await LibraryPipeline.DrainAsync(store);

        await using (var personal = store.Scope())
        {
            var database = personal.ServiceProvider.GetRequiredService<ViewerDbContext>();
            var videos = await database.Videos
                .Include(video => video.VideoFiles)
                .ToDictionaryAsync(video => video.DisplayLabel, TestContext.Current.CancellationToken);
            var now = DateTime.SpecifyKind(new DateTime(2026, 9, 2, 12, 0, 0), DateTimeKind.Utc);

            database.PersonalVideoStates.AddRange(
                new PersonalVideoStateRow
                {
                    AccountId = accountId,
                    VideoId = videos["favourite"].Id,
                    FavouriteAddedAt = now.AddDays(-2),
                    UpdatedAt = now,
                },
                // Added to Watch Later first, and to Favourites last of all: the queue leads with
                // it and the Favourites lead with it too, for opposite reasons.
                new PersonalVideoStateRow
                {
                    AccountId = accountId,
                    VideoId = videos["queued-first"].Id,
                    WatchLaterAddedAt = now.AddDays(-3),
                    FavouriteAddedAt = now,
                    UpdatedAt = now,
                },
                // Not ready for direct play here, and on the shelf all the same.
                new PersonalVideoStateRow
                {
                    AccountId = accountId,
                    VideoId = videos["queued-second"].Id,
                    WatchLaterAddedAt = now.AddDays(-1),
                    UpdatedAt = now,
                },
                Watching(accountId, videos["watching"], now.AddMinutes(-30), progress: 30_000),
                Watching(accountId, videos["dismissed"], now.AddHours(-1), progress: 30_000, dismissedAt: now),
                Watching(accountId, videos["nearly-done"], now, progress: 58_000),
                // Another Account's shelves are its own.
                new PersonalVideoStateRow
                {
                    AccountId = otherAccountId,
                    VideoId = videos["plain"].Id,
                    FavouriteAddedAt = now,
                    WatchLaterAddedAt = now,
                    UpdatedAt = now,
                });
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var scope = store.Scope();

        // Favourites lead with what entered them last, and nothing is hidden from a shelf.
        var favourites = await OnShelfAsync(scope, accountId, PersonalShelf.Favourites);
        Assert.Equal(["queued-first", "favourite"], favourites.Videos.Select(video => video.DisplayTitle));
        Assert.Equal(0, favourites.HiddenNotReadyForDirectPlay);
        Assert.Equal(0, favourites.HiddenUnavailable);

        // Watch Later is a queue, and a Video this client cannot play is still in it.
        var watchLater = await OnShelfAsync(scope, accountId, PersonalShelf.WatchLater);
        Assert.Equal(["queued-first", "queued-second"], watchLater.Videos.Select(video => video.DisplayTitle));
        Assert.NotEqual(ClientVideoPlayability.ReadyForDirectPlay, watchLater.Videos[1].Playability);

        // Continue Watching is the same rule the summary applies: in progress, not dismissed since,
        // and short of the Completion End Zone.
        var watching = await OnShelfAsync(scope, accountId, PersonalShelf.ContinueWatching);
        Assert.Equal("watching", Assert.Single(watching.Videos).DisplayTitle);
        Assert.True(watching.Videos[0].PersonalState.ContinueWatching);

        // The shelf takes the same narrowing the Library does: a search, and an explicit
        // playability filter that still decides for this view.
        var searched = await Discovery(scope).GetAsync(
            accountId,
            LibraryPipeline.ClientContext,
            new LibraryDiscoveryRequest { Shelf = [PersonalShelf.WatchLater], Query = "second" },
            TestContext.Current.CancellationToken);
        Assert.Equal("queued-second", Assert.Single(searched.Videos).DisplayTitle);
        var playable = await Discovery(scope).GetAsync(
            accountId,
            LibraryPipeline.ClientContext,
            new LibraryDiscoveryRequest
            {
                Shelf = [PersonalShelf.WatchLater],
                Playability = [ClientVideoPlayability.ReadyForDirectPlay],
            },
            TestContext.Current.CancellationToken);
        Assert.Equal("queued-first", Assert.Single(playable.Videos).DisplayTitle);

        // Values inside the facet combine with OR, and the facets are counted inside the shelf.
        var both = await Discovery(scope).GetAsync(
            accountId,
            LibraryPipeline.ClientContext,
            new LibraryDiscoveryRequest
            {
                Shelf = [PersonalShelf.Favourites, PersonalShelf.ContinueWatching],
                Sort = LibrarySortOrder.ShelfOrder,
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(
            ["queued-first", "watching", "favourite"],
            both.Videos.Select(video => video.DisplayTitle));
        var facets = await Discovery(scope).GetFacetsAsync(
            accountId,
            new LibraryDiscoveryRequest { Shelf = [PersonalShelf.WatchLater] },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, facets.Quality.Sum(band => band.Count));

        // The other Account's shelves are empty of this Account's references and vice versa.
        Assert.Equal(
            "plain",
            Assert.Single((await OnShelfAsync(scope, otherAccountId, PersonalShelf.Favourites)).Videos).DisplayTitle);
    }

    private static PersonalVideoStateRow Watching(
        Guid accountId,
        VideoRow video,
        DateTime activityAt,
        long progress,
        DateTime? dismissedAt = null) =>
        new()
        {
            AccountId = accountId,
            VideoId = video.Id,
            PlayState = PersonalPlayState.InProgress,
            PlayStateChangedAt = activityAt,
            LastQualifiedActivityAt = activityAt,
            PlaybackProgressMilliseconds = progress,
            ProgressVideoFileId = video.VideoFiles.Single().Id,
            ContinueWatchingDismissedAt = dismissedAt,
            UpdatedAt = activityAt,
        };

    private static Task<LibraryPage> OnShelfAsync(
        AsyncServiceScope scope,
        Guid accountId,
        PersonalShelf shelf) =>
        Discovery(scope).GetAsync(
            accountId,
            LibraryPipeline.ClientContext,
            new LibraryDiscoveryRequest { Shelf = [shelf], Sort = LibrarySortOrder.ShelfOrder },
            TestContext.Current.CancellationToken);

    private static LibraryDiscovery Discovery(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<LibraryDiscovery>();

    private static Task<LibraryPage> Search(AsyncServiceScope scope, Guid accountId, string query) =>
        Discovery(scope).GetAsync(
            accountId,
            LibraryPipeline.ClientContext,
            new LibraryDiscoveryRequest { Query = query },
            TestContext.Current.CancellationToken);

    private static async Task<Guid> AccountAsync(TestDatabase store, string username = "viewer")
    {
        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var account = new AccountRow
        {
            Id = Guid.CreateVersion7(),
            Username = username,
            NormalizedUsername = username,
            PasswordHash = new string('h', 84),
            Authority = AccountAuthority.User,
            State = AccountState.Approved,
            RegisteredAt = DateTime.SpecifyKind(new DateTime(2026, 8, 1), DateTimeKind.Utc),
        };
        database.Accounts.Add(account);
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        return account.Id;
    }

    private static async Task<string> SourceAsync(
        TestDatabase store,
        params (string Name, string Container)[] files)
    {
        var source = Path.Combine(store.LibraryMountRoot.Path, "source");
        Directory.CreateDirectory(source);

        foreach (var file in files)
        {
            await File.WriteAllBytesAsync(
                Path.Combine(source, file.Name),
                [(byte)file.Name.Length, 1, 2, 3],
                TestContext.Current.CancellationToken);
        }

        await LibraryPipeline.ActivateAsync(store, source);
        return source;
    }

    private static Task<TestDatabase> CreateAsync(FixtureIdentificationClient? prdb = null) =>
        TestDatabase.CreateAsync(
            mediaProbe: new ContainerAwareProbe(),
            hasher: new FixtureHasher(),
            previewGenerator: new FixturePreviewGenerator(),
            identificationClient: prdb ?? new FixtureIdentificationClient());
}

/// <summary>
/// A probe that reports the container the file name implies, so a test can produce a Video that
/// is genuinely not ready for direct play instead of asserting against a forced value.
/// </summary>
internal sealed class ContainerAwareProbe : IMediaProbe
{
    public Task<MediaProbeFacts?> InspectAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<MediaProbeFacts?>(Path.GetExtension(path) == ".mkv"
            ? new MediaProbeFacts(
                FixtureProbe.Baseline with { ContainerFormat = "matroska", VideoCodec = "h264" },
                12_345)
            : new MediaProbeFacts(FixtureProbe.Baseline, 12_345));
}

/// <summary>
/// A probe that reports the dimensions the file name implies, so that a test about Video Quality
/// produces genuinely different bands instead of asserting against a forced column.
/// </summary>
internal sealed class DimensionAwareProbe : IMediaProbe
{
    public Task<MediaProbeFacts?> InspectAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var (width, height) = Path.GetFileNameWithoutExtension(path) switch
        {
            "uhd" => (3840, 2160),
            "sd" => (720, 404),
            _ => (1920, 1080),
        };

        return Task.FromResult<MediaProbeFacts?>(new MediaProbeFacts(
            FixtureProbe.Baseline with { Width = width, Height = height },
            12_345));
    }
}

/// <summary>
/// A probe that reports the runtime the file name implies, so a test about order by runtime has
/// three Videos that genuinely differ in it.
/// </summary>
/// <summary>
/// A minute of ready video in every file but the matroska one, so a shelf can hold a Video this
/// client cannot play, and the Completion End Zone is a known six seconds from the end.
/// </summary>
internal sealed class ShelfProbe : IMediaProbe
{
    public Task<MediaProbeFacts?> InspectAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<MediaProbeFacts?>(Path.GetExtension(path) == ".mkv"
            ? new MediaProbeFacts(FixtureProbe.Baseline with { ContainerFormat = "matroska", VideoCodec = "h264" }, 60_000)
            : new MediaProbeFacts(FixtureProbe.Baseline, 60_000));
}

internal sealed class RuntimeAwareProbe : IMediaProbe
{
    public Task<MediaProbeFacts?> InspectAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var duration = Path.GetFileNameWithoutExtension(path) switch
        {
            "long" => 3_600_000,
            "middle" => 600_000,
            _ => 60_000,
        };

        return Task.FromResult<MediaProbeFacts?>(new MediaProbeFacts(FixtureProbe.Baseline, duration));
    }
}
