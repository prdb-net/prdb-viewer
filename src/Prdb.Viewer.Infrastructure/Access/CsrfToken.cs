using System.Security.Cryptography;
using System.Text;

namespace Prdb.Viewer.Infrastructure.Access;

/// The Cross-Site Request Forgery token belonging to a Session.
///
/// It is derived from the Session Token rather than stored beside it, so it is a property of the
/// Session rather than state of its own. Every client holding the Session computes the same token,
/// and asking who you are cannot invalidate the token another client already holds — which is
/// exactly what a stored, rotated token did to a second tab. A Session that ends takes its token
/// with it, because the Session Token it was derived from is gone.
///
/// The derivation keys an HMAC with the Session Token, which only a client that already holds the
/// Session cookie can know. A cross-site caller can neither read that cookie (HttpOnly, SameSite
/// Strict) nor compute the header from what it can see, so the protection is the one a random
/// per-Session token gave. The derivation is one-way, so handing the token to the page's script —
/// which is the point of it — reveals nothing about the Session Token.
public static class CsrfToken
{
    /// Domain separation: this HMAC is the CSRF token and nothing else derived from a Session
    /// Token could collide with it.
    private static readonly byte[] Purpose = "prdb-viewer:csrf"u8.ToArray();

    public static string For(string sessionToken) =>
        Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(sessionToken), Purpose))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    public static bool Matches(string? sessionToken, string? presented) =>
        !string.IsNullOrEmpty(sessionToken) &&
        !string.IsNullOrEmpty(presented) &&
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(For(sessionToken)),
            Encoding.UTF8.GetBytes(presented));
}
