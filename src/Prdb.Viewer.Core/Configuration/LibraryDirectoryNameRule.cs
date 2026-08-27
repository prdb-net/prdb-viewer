namespace Prdb.Viewer.Core.Configuration;

public static class LibraryDirectoryNameRule
{
    public const int MaximumLength = 80;

    public static bool IsValid(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name.Trim().Length <= MaximumLength &&
        !name.Any(char.IsControl);

    public static string Normalize(string name) => name.Trim();
}
