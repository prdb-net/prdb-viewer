namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// Resolves a stored relative path against its Library Directory. Every read of source media goes
/// through here, so a path that escapes the configured root, no longer exists, or arrives through
/// a link is refused rather than opened.
/// </summary>
internal static class SourceFile
{
    public static string? Resolve(string root, string relativePath)
    {
        try
        {
            var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            var path = Path.GetFullPath(Path.Combine(
                normalizedRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            return path.StartsWith($"{normalizedRoot}{Path.DirectorySeparatorChar}", comparison) &&
                   File.Exists(path) &&
                   (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0
                ? path
                : null;
        }
        catch (Exception exception) when (exception is ArgumentException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
