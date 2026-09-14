namespace Prdb.Viewer.Core.Library;

public static class VideoFileCandidatePolicy
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".avi", ".m2ts", ".m4v", ".mkv", ".mov", ".mp4", ".mpeg", ".mpg", ".ts", ".webm", ".wmv",
    };

    public static bool Recognizes(string extension) => Extensions.Contains(extension);

    /// <summary>
    /// The extensions this policy admits, in order, as a sentence can carry them. A library whose
    /// files are all passed over is a library whose extensions are not these, and the answer to
    /// that is unreachable while the list is only ever compiled into the product.
    /// </summary>
    public static string Recognised =>
        string.Join(", ", Extensions.OrderBy(extension => extension, StringComparer.Ordinal));
}
