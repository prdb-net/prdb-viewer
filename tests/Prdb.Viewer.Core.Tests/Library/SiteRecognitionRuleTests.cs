using Prdb.Viewer.Core.Library;

using Xunit;

namespace Prdb.Viewer.Core.Tests.Library;

public sealed class SiteRecognitionRuleTests
{
    private static readonly SiteVocabularyEntry NightOwl =
        new("site-night-owl", "Night Owl", "https://www.nightowl.example/");

    private static readonly SiteVocabularyEntry Harbour =
        new("site-harbour", "Harbour", "https://harbour.example");

    private static readonly SiteVocabularyEntry HarbourNights =
        new("site-harbour-nights", "Harbour Nights", null);

    private static readonly SiteVocabularyEntry Tiny = new("site-bay", "Bay", null);

    private static SiteVocabularyEntry Creator(string key, string platform, string handle, string title) =>
        new(key, title, $"https://{platform}.example/{handle}", key, platform, platform, handle);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_shared_platform_alone_is_never_account_evidence(int count)
    {
        var sites = new[] { Creator("a", "platform", "alice", "Alice"), Creator("b", "platform", "bob", "Bob") };
        Assert.Equal(IdentificationEvidenceClass.Insufficient,
            Vocabulary(sites.Take(count).ToArray()).Recognise("platform/unknown.mp4").Evidence);
        Assert.Equal(IdentificationEvidenceClass.Insufficient,
            Vocabulary(sites.Take(count).ToArray()).Recognise("alice.mp4").Evidence);
        Assert.Equal("a", Assert.Single(Vocabulary(sites.Take(count).ToArray())
            .Recognise("platform/folder/alice.mp4").Matches).Site.Key);
    }

    [Fact]
    public void Equal_handles_are_qualified_by_platform_and_distinct_titles_remain_usable()
    {
        var a = Creator("a", "first", "alex", "Alex Studio");
        var b = Creator("b", "second", "alex", "Alex Other Studio");
        var vocabulary = Vocabulary(a, b);
        Assert.Equal("a", Assert.Single(vocabulary.Recognise("first/alex/scene.mp4").Matches).Site.Key);
        Assert.Equal(IdentificationEvidenceClass.Insufficient, vocabulary.Recognise("alex/scene.mp4").Evidence);
        Assert.Equal(IdentificationEvidenceClass.Conclusive, vocabulary.Recognise("Alex Studio/scene.mp4").Evidence);
        Assert.Equal(IdentificationEvidenceClass.Suggestive, vocabulary.Recognise("first/second/alex.mp4").Evidence);
    }

    [Fact]
    public void A_generic_title_or_a_legacy_shared_domain_never_names_an_individual_creator()
    {
        Assert.Empty(Vocabulary(Creator("a", "platform", "alice", "platform"))
            .Recognise("platform.mp4").Matches);
        Assert.Empty(Vocabulary(new SiteVocabularyEntry("a", "Alice", "https://onlyfans.com/alice"))
            .Recognise("onlyfans.mp4").Matches);
    }

    private static SiteVocabulary Vocabulary(params SiteVocabularyEntry[] entries) =>
        SiteVocabulary.From(entries);

    [Theory]
    [InlineData("Night Owl/scene one.mp4")]
    [InlineData("NightOwl.22.01.01.Scene.mp4")]
    [InlineData("downloads/night-owl - scene one.mkv")]
    [InlineData("nightowl22.mp4")]
    public void A_path_that_names_exactly_one_known_site_recognises_it_conclusively(string path)
    {
        var recognition = Vocabulary(NightOwl, Harbour).Recognise(path);

        Assert.Equal(IdentificationEvidenceClass.Conclusive, recognition.Evidence);
        Assert.Equal(NightOwl.Key, Assert.Single(recognition.Matches).Site.Key);
    }

    [Fact]
    public void A_site_is_recognised_by_the_distinctive_label_of_its_web_address()
    {
        var recognition = Vocabulary(new SiteVocabularyEntry(
            "site-studio-pass",
            "Studio Pass Network",
            "https://www.studiopass.example/tour")).Recognise("studiopass/scene.mp4");

        Assert.Equal(IdentificationEvidenceClass.Conclusive, recognition.Evidence);
        Assert.Equal("studiopass", Assert.Single(recognition.Matches).Alias);
    }

    [Fact]
    public void A_path_that_names_two_known_sites_proposes_both_without_establishing_either()
    {
        var recognition = Vocabulary(NightOwl, Harbour)
            .Recognise("Harbour/night owl crossover.mp4");

        Assert.Equal(IdentificationEvidenceClass.Suggestive, recognition.Evidence);
        Assert.Equal(2, recognition.Matches.Count);
    }

    [Fact]
    public void The_longest_name_a_path_gives_wins_over_the_shorter_one_inside_it()
    {
        var recognition = Vocabulary(Harbour, HarbourNights)
            .Recognise("Harbour Nights/scene.mp4");

        Assert.Equal(IdentificationEvidenceClass.Conclusive, recognition.Evidence);
        Assert.Equal(HarbourNights.Key, Assert.Single(recognition.Matches).Site.Key);
    }

    [Fact]
    public void A_name_short_enough_to_be_an_ordinary_word_is_only_suggestive()
    {
        var recognition = Vocabulary(Tiny).Recognise("clips/bay scene.mp4");

        Assert.Equal(IdentificationEvidenceClass.Suggestive, recognition.Evidence);
        Assert.Equal(Tiny.Key, Assert.Single(recognition.Matches).Site.Key);
    }

    [Theory]
    [InlineData("midnightowl/scene.mp4")]
    [InlineData("holiday videos/beach.mp4")]
    [InlineData("nightowl2x.mp4")]
    public void A_path_that_does_not_name_a_known_site_establishes_and_proposes_nothing(string path)
    {
        var recognition = Vocabulary(NightOwl, Harbour).Recognise(path);

        Assert.Equal(IdentificationEvidenceClass.Insufficient, recognition.Evidence);
        Assert.Empty(recognition.Matches);
    }

    [Fact]
    public void Two_sites_sharing_one_name_never_establish_a_claim_from_it()
    {
        var recognition = Vocabulary(
                Harbour,
                new SiteVocabularyEntry("site-harbour-two", "Harbour", null))
            .Recognise("Harbour/scene.mp4");

        Assert.Equal(IdentificationEvidenceClass.Suggestive, recognition.Evidence);
        Assert.Equal(2, recognition.Matches.Count);
    }

    [Fact]
    public void An_empty_site_directory_recognises_nothing() =>
        Assert.Equal(
            IdentificationEvidenceClass.Insufficient,
            SiteVocabulary.Empty.Recognise("Harbour/scene.mp4").Evidence);
}
