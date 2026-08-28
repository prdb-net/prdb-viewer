using Prdb.Viewer.Core.Library;

using Xunit;

namespace Prdb.Viewer.Core.Tests.Library;

public sealed class LibraryDiscoveryRuleTests
{
    [Fact]
    public void Ordinary_discovery_admits_only_ready_videos_until_the_account_asks_for_more()
    {
        Assert.True(LibraryAdmissionRule.IsOrdinarilyDiscoverable(
            ClientVideoPlayability.ReadyForDirectPlay,
            includesNotReadyForDirectPlay: false));
        Assert.False(LibraryAdmissionRule.IsOrdinarilyDiscoverable(
            ClientVideoPlayability.CompatibilityUncertain,
            includesNotReadyForDirectPlay: false));
        Assert.True(LibraryAdmissionRule.IsOrdinarilyDiscoverable(
            ClientVideoPlayability.CompatibilityUncertain,
            includesNotReadyForDirectPlay: true));
        Assert.True(LibraryAdmissionRule.IsOrdinarilyDiscoverable(
            ClientVideoPlayability.NotDirectlyPlayable,
            includesNotReadyForDirectPlay: true));
    }

    [Theory]
    [InlineData("Bell's-Ünnamed_clip", "bells unnamed clip")]
    [InlineData("  MIXED   Case  ", "mixed case")]
    [InlineData("Café — Zürich", "cafe zurich")]
    [InlineData("...", "")]
    public void Search_ignores_case_diacritics_and_ordinary_punctuation(
        string written,
        string normalized) =>
        Assert.Equal(normalized, LibrarySearchRule.Normalize(written));

    [Fact]
    public void Every_term_must_match_somewhere_though_not_in_the_same_fact()
    {
        var video = Searchable(
            label: "holiday-clip",
            title: "A Known Work",
            site: "Example Site",
            actors: ["Alex Doe"],
            names: ["holiday-clip.mp4"]);

        Assert.NotNull(LibrarySearchRule.Match(video, LibrarySearchRule.Terms("known alex example")));
        Assert.Null(LibrarySearchRule.Match(video, LibrarySearchRule.Terms("known missing")));
    }

    [Fact]
    public void Titles_and_labels_outrank_sites_actors_and_file_names()
    {
        var video = Searchable(
            label: "holiday-clip",
            title: "A Known Work",
            site: "Known Site",
            actors: ["Known Actor"],
            names: ["known-name.mp4"]);

        Assert.Equal(
            SearchMatchField.ExactTitle,
            LibrarySearchRule.Match(video, LibrarySearchRule.Terms("a known work")));
        Assert.Equal(
            SearchMatchField.Title,
            LibrarySearchRule.Match(video, LibrarySearchRule.Terms("work")));
        Assert.Equal(
            SearchMatchField.Site,
            LibrarySearchRule.Match(
                Searchable("label", null, "Known Site", [], ["file.mp4"]),
                LibrarySearchRule.Terms("known")));
        Assert.Equal(
            SearchMatchField.FileName,
            LibrarySearchRule.Match(
                Searchable("label", null, null, [], ["known-name.mp4"]),
                LibrarySearchRule.Terms("known")));
    }

    [Fact]
    public void An_unknown_video_is_searchable_by_its_local_label_and_file_names()
    {
        var unknown = Searchable(
            label: "beach day 2019",
            title: null,
            site: null,
            actors: [],
            names: ["beach day 2019.mkv"]);

        Assert.Equal(
            SearchMatchField.ExactTitle,
            LibrarySearchRule.Match(unknown, LibrarySearchRule.Terms("Beach Day 2019")));
        Assert.Equal(
            SearchMatchField.Title,
            LibrarySearchRule.Match(unknown, LibrarySearchRule.Terms("beach")));
    }

    [Fact]
    public void An_empty_query_matches_everything()
    {
        Assert.Empty(LibrarySearchRule.Terms("   "));
        Assert.NotNull(LibrarySearchRule.Match(
            Searchable("label", null, null, [], []),
            LibrarySearchRule.Terms(null)));
    }

    private static SearchableVideo Searchable(
        string label,
        string? title,
        string? site,
        string[] actors,
        string[] names) =>
        new(label, title, site, actors, names);
}
