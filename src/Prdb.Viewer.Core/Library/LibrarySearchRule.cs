using System.Globalization;
using System.Text;

namespace Prdb.Viewer.Core.Library;

/// <summary>
/// Which searchable fact a term matched. The order is the ranking order: an exact title or local
/// label first, then any other title match, then Sites and Actors, then file names.
/// </summary>
public enum SearchMatchField
{
    ExactTitle,
    Title,
    Site,
    Actor,
    FileName,
}

/// <summary>
/// The searchable facts of one Video. Only Established knowledge and local facts appear here:
/// a Pending Identification Candidate is never searchable, because searching for a guess would
/// present it as a fact.
/// </summary>
public sealed record SearchableVideo(
    string DisplayLabel,
    string? EstablishedTitle,
    string? EstablishedSite,
    IReadOnlyList<string> Actors,
    IReadOnlyList<string> FileNames);

/// <summary>
/// Free-text search over the library. It ignores case, diacritics, and ordinary punctuation, and
/// requires every term to match somewhere — possibly in different facts. It deliberately promises
/// no stemming, typo correction, synonyms, or semantic matching.
/// </summary>
public static class LibrarySearchRule
{
    /// <summary>
    /// Reduces a value to what search compares: lower case, no diacritics, and punctuation folded
    /// to single spaces, so `Bell's-Ünnamed_clip` and `bells unnamed clip` are the same query.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var text = new StringBuilder(decomposed.Length);
        var pendingSeparator = false;

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            // An apostrophe sits inside a word rather than between two, so it is dropped instead
            // of splitting one: a search for `bells` has to find `Bell's`.
            if (character is '\'' or '\u2019' or '`')
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                if (pendingSeparator && text.Length > 0)
                {
                    text.Append(' ');
                }

                pendingSeparator = false;
                text.Append(char.ToLowerInvariant(character));
                continue;
            }

            pendingSeparator = true;
        }

        return text.ToString().Normalize(NormalizationForm.FormC);
    }

    public static IReadOnlyList<string> Terms(string? query) =>
        Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Whether every term matches at least one searchable fact, and how strongly the query matched
    /// overall. Returns null when the Video is not a match at all.
    /// </summary>
    public static SearchMatchField? Match(SearchableVideo video, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0)
        {
            return SearchMatchField.Title;
        }

        var label = Normalize(video.DisplayLabel);
        var title = Normalize(video.EstablishedTitle);
        var site = Normalize(video.EstablishedSite);
        var actors = video.Actors.Select(Normalize).ToArray();
        var names = video.FileNames.Select(Normalize).ToArray();
        var whole = Normalize(string.Join(' ', terms));
        var best = SearchMatchField.FileName;

        foreach (var term in terms)
        {
            var field = FieldFor(term, label, title, site, actors, names);

            if (field is null)
            {
                return null;
            }

            if (field.Value < best)
            {
                best = field.Value;
            }
        }

        // The strongest rank is reserved for a query that is the whole title or label, so a
        // one-word query does not outrank a Video whose title it happens to begin.
        var exact = (title.Length > 0 && title == whole) || (label.Length > 0 && label == whole);

        return exact ? SearchMatchField.ExactTitle : best;
    }

    private static SearchMatchField? FieldFor(
        string term,
        string label,
        string title,
        string site,
        IReadOnlyList<string> actors,
        IReadOnlyList<string> names)
    {
        if (Contains(title, term) || Contains(label, term))
        {
            return SearchMatchField.Title;
        }

        if (Contains(site, term))
        {
            return SearchMatchField.Site;
        }

        if (actors.Any(actor => Contains(actor, term)))
        {
            return SearchMatchField.Actor;
        }

        return names.Any(name => Contains(name, term)) ? SearchMatchField.FileName : null;
    }

    private static bool Contains(string value, string term) =>
        value.Length > 0 && value.Contains(term, StringComparison.Ordinal);
}
