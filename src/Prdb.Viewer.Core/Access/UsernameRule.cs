using System.Text.RegularExpressions;

namespace Prdb.Viewer.Core.Access;

public static partial class UsernameRule
{
    public const int MinimumLength = 3;
    public const int MaximumLength = 64;

    public static bool IsValid(string? username) =>
        username is not null && ValidUsername().IsMatch(username);

    public static string Normalize(string username) => username.ToUpperInvariant();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{1,62}[A-Za-z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidUsername();
}
