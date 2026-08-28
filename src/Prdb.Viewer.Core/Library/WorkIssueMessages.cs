namespace Prdb.Viewer.Core.Library;

/// <summary>
/// The canonical Work Issue summaries. A summary states what cannot currently happen in one short
/// human sentence, so it never begins with an exception name, a numeric code, or generic
/// `Something went wrong` text.
/// </summary>
public static class WorkIssueMessages
{
    public static string CannotReadFile(string file) => $"Cannot read “{file}”";

    public static string CannotAccessScope(string scope) => $"Cannot access “{scope}”";

    public static string DirectoryCannotBeScanned(string name) =>
        $"Library directory “{name}” cannot be scanned";

    public static string PartOfDirectoryCannotBeScanned(string name) =>
        $"Part of “{name}” could not be scanned";

    public static string FileIsStillChanging(string file) => $"“{file}” is still changing";

    public static string CannotInspect(string file) => $"Cannot inspect “{file}”";

    public static string CannotClassify(string file) => $"Cannot classify “{file}”";

    public static string CannotHash(string file) =>
        $"Cannot calculate an identification hash for “{file}”";

    public static string PreviewFailed(string video) =>
        $"A preview could not be generated for “{video}”";

    public static string StorageCannotAcceptData() => "Application storage cannot accept new data";

    public static string IdentificationWaiting() => "prdb identification is waiting";

    public static string IdentificationBlocked() => "prdb identification is blocked";

    public static string PrdbUnavailable() => "prdb.net is temporarily unavailable";

    public static string PrdbDelaying() => "prdb.net is delaying requests";

    public static string PrdbRejected() => "The prdb connection was rejected";

    public static string NeedsConfiguration() => "Background work needs configuration";

    public static string StoppedToProtectLibraryState() =>
        "Background work stopped to protect library state";

    private static readonly string[] Unusable =
    [
        "something went wrong",
        "exception",
        "error:",
        "unhandled",
        "failed with",
    ];

    /// <summary>
    /// Whether a summary is usable as the one statement an Administrator reads first. It exists so
    /// no lane can quietly reintroduce a raw technical failure string.
    /// </summary>
    public static bool IsUsableSummary(string summary)
    {
        if (string.IsNullOrWhiteSpace(summary) || summary.Length > 120)
        {
            return false;
        }

        var trimmed = summary.Trim();

        // A summary opens with a word or a quoted name, never with a numeric code.
        if (!char.IsLetter(trimmed[0]) && trimmed[0] != '“')
        {
            return false;
        }

        return !Unusable.Any(fragment =>
            trimmed.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }
}
