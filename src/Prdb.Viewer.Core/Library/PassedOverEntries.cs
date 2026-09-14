using System.Globalization;

namespace Prdb.Viewer.Core.Library;

/// <summary>
/// What a Library Scan walked past without admitting it: how many entries, and which extensions
/// led among them. A traversal that admits nothing is otherwise indistinguishable from one that
/// found an empty directory, and the two have entirely different answers.
/// </summary>
public static class PassedOverEntries
{
    /// <summary>
    /// How many distinct extensions are worth naming: enough to recognise a library by, few enough
    /// that the sentence stays a sentence. The rest are counted and left unnamed.
    /// </summary>
    public const int LeadingExtensions = 5;

    /// <summary>What an entry with no extension at all is counted under.</summary>
    public const string WithoutExtension = "no extension";

    /// <summary>
    /// The name one entry is counted under. Case is not a distinction a person makes between two
    /// files, so `.MP4` and `.mp4` are one heading.
    /// </summary>
    public static string Heading(string extension) =>
        string.IsNullOrEmpty(extension)
            ? WithoutExtension
            : extension.ToLowerInvariant();

    /// <summary>
    /// The leading extensions, largest first and then alphabetically so equal counts do not move
    /// between runs, as the clause a person reads: `.flv (280), .divx (132)`.
    /// </summary>
    public static string Describe(IReadOnlyDictionary<string, int> counts)
    {
        var leading = counts
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Take(LeadingExtensions)
            .Select(entry => string.Create(
                CultureInfo.InvariantCulture,
                $"{entry.Key} ({entry.Value})"));

        return string.Join(", ", leading);
    }
}
