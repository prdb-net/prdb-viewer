namespace Prdb.Viewer.Core.Library;

/// <summary>
/// One site the installation knows by name, as local Site Recognition may recognise it. The key is
/// the site's prdb identity, so a locally recognised site and a prdb-established one name the same
/// thing and never split the library into two spellings of one site.
/// </summary>
public sealed record SiteVocabularyEntry(string Key, string Title, string? Url);

/// <summary>One site a Video File's own path names, and the alias that named it.</summary>
public sealed record LocalSiteMatch(SiteVocabularyEntry Site, string Alias);

/// <summary>
/// What a Video File's path says about its originating site. The matches are the sites the path
/// names; the evidence class says what may be done with them.
/// </summary>
public sealed record LocalSiteRecognition(
    IReadOnlyList<LocalSiteMatch> Matches,
    IdentificationEvidenceClass Evidence)
{
    public static readonly LocalSiteRecognition None =
        new([], IdentificationEvidenceClass.Insufficient);
}

/// <summary>
/// Recognises the site a Video File comes from out of the file's own path, against the Site
/// Directory the installation retains.
///
/// The match is deliberately deterministic rather than clever: an alias has to appear as whole
/// words of the path, so a site is recognised because the path names it and not because one name
/// happens to contain another. Nothing here reads file content, contacts a service, or scores a
/// similarity — a path that names exactly one known site maps uniquely to that site, and anything
/// less certain stays a proposal for an Administrator.
/// </summary>
public sealed class SiteVocabulary
{
    /// <summary>
    /// An alias this short is a word before it is a site name, so a match on it is offered for
    /// review rather than established. Measured without the spaces between its words.
    /// </summary>
    public const int ConclusiveAliasLength = 5;

    private readonly Dictionary<string, List<SiteVocabularyEntry>> byAlias;
    private readonly int longestAliasWords;

    private SiteVocabulary(
        Dictionary<string, List<SiteVocabularyEntry>> byAlias,
        int longestAliasWords)
    {
        this.byAlias = byAlias;
        this.longestAliasWords = longestAliasWords;
    }

    public static readonly SiteVocabulary Empty = new([], 0);

    public bool IsEmpty => byAlias.Count == 0;

    /// <summary>
    /// Builds the lookup once for a whole batch of files. Two sites that share an alias keep it:
    /// the alias then names both, which is exactly the ambiguity that must not establish anything.
    /// </summary>
    public static SiteVocabulary From(IEnumerable<SiteVocabularyEntry> entries)
    {
        var byAlias = new Dictionary<string, List<SiteVocabularyEntry>>(StringComparer.Ordinal);
        var longest = 0;

        foreach (var entry in entries)
        {
            foreach (var alias in AliasesOf(entry))
            {
                if (!byAlias.TryGetValue(alias, out var sites))
                {
                    sites = [];
                    byAlias.Add(alias, sites);
                }

                if (!sites.Any(site =>
                        string.Equals(site.Key, entry.Key, StringComparison.OrdinalIgnoreCase)))
                {
                    sites.Add(entry);
                }

                longest = Math.Max(longest, WordCount(alias));
            }
        }

        return new SiteVocabulary(byAlias, longest);
    }

    /// <summary>
    /// The aliases one site answers to: its title as words, the same title written as one word,
    /// and the distinctive label of its own web address. Everything is normalised the way search
    /// normalises, so `Night-Owl`, `night owl` and `NightOwl` are one alias apiece.
    /// </summary>
    public static IReadOnlyList<string> AliasesOf(SiteVocabularyEntry entry)
    {
        var aliases = new List<string>();

        foreach (var candidate in new[] { LibrarySearchRule.Normalize(entry.Title), HostLabel(entry.Url) })
        {
            Add(aliases, candidate);
            Add(aliases, candidate.Replace(" ", string.Empty));
        }

        return aliases;
    }

    /// <summary>
    /// The sites this path names. A path is read as words: its directories and its file name
    /// without the extension, normalised and split, so folder structure and file name are the same
    /// kind of evidence.
    /// </summary>
    public LocalSiteRecognition Recognise(string relativePath)
    {
        if (IsEmpty)
        {
            return LocalSiteRecognition.None;
        }

        var words = WordsOf(relativePath);
        var matches = new List<LocalSiteMatch>();

        for (var start = 0; start < words.Length;)
        {
            var length = Longest(words, start, out var alias, out var sites);

            if (length == 0)
            {
                start++;
                continue;
            }

            foreach (var site in sites)
            {
                if (!matches.Any(match =>
                        string.Equals(
                            match.Site.Key,
                            site.Key,
                            StringComparison.OrdinalIgnoreCase) &&
                        match.Alias == alias))
                {
                    matches.Add(new LocalSiteMatch(site, alias));
                }
            }

            start += length;
        }

        if (matches.Count == 0)
        {
            return LocalSiteRecognition.None;
        }

        var distinctSites = matches
            .Select(match => match.Site.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var longestAlias = matches.Max(match => match.Alias.Replace(" ", string.Empty).Length);

        return new LocalSiteRecognition(
            matches,
            IdentificationEvidenceRule.ClassifyLocalSiteRecognition(distinctSites, longestAlias));
    }

    /// <summary>
    /// The longest alias the path names starting at one word, because the more words a site's name
    /// takes the more precisely it is named: a path that says `harbour nights` names that site
    /// rather than ambiguously naming it and `harbour`.
    /// </summary>
    private int Longest(
        string[] words,
        int start,
        out string alias,
        out IReadOnlyList<SiteVocabularyEntry> sites)
    {
        for (var length = Math.Min(longestAliasWords, words.Length - start); length >= 1; length--)
        {
            alias = string.Join(' ', words, start, length);

            if (byAlias.TryGetValue(alias, out var matched))
            {
                sites = matched;
                return length;
            }

            if (length == 1 &&
                byAlias.TryGetValue(WithoutTrailingDigits(alias), out matched))
            {
                alias = WithoutTrailingDigits(alias);
                sites = matched;
                return length;
            }
        }

        alias = string.Empty;
        sites = [];
        return 0;
    }

    /// <summary>
    /// A single word that is a site's compact alias followed only by digits, as a dated release
    /// name writes it. `nightowl22` names the site; `midnightowl` does not, and never matches
    /// because nothing here looks inside a word.
    /// </summary>
    private static string WithoutTrailingDigits(string word)
    {
        var end = word.Length;

        while (end > 0 && char.IsAsciiDigit(word[end - 1]))
        {
            end--;
        }

        return end == word.Length ? word : word[..end];
    }

    private static string[] WordsOf(string relativePath)
    {
        var directories = Path.GetDirectoryName(relativePath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(relativePath);

        return LibrarySearchRule
            .Normalize($"{directories} {name}")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// The distinctive label of a web address: `https://www.nightowl.example/tour` becomes
    /// `nightowl`.
    /// The scheme, a leading `www`, the public suffix and any path are not what a file names.
    /// </summary>
    private static string HostLabel(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            return string.Empty;
        }

        var labels = parsed.Host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var candidate = labels
            .Where(label => !string.Equals(label, "www", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        return labels.Length < 2 ? string.Empty : LibrarySearchRule.Normalize(candidate);
    }

    private static void Add(List<string> aliases, string alias)
    {
        if (alias.Length > 0 && !aliases.Contains(alias, StringComparer.Ordinal))
        {
            aliases.Add(alias);
        }
    }

    private static int WordCount(string alias) =>
        alias.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
}
