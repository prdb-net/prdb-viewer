using System.Security.Cryptography;
using System.Text;

namespace Prdb.Viewer.Host.Library;

/// <summary>
/// The client context a request speaks for: which browser, on which device, this Account is using.
///
/// Client Playback Assessments and Observed Playback Outcomes belong to one such context, and
/// expire when it materially changes. The client names its own context, because only it knows what
/// it is; the server neither derives one from request headers nor keeps anything identifying about
/// it — the value is stored as the opaque key it arrives as, and a client that offers nothing gets
/// the shared unqualified context, where only the conservative baseline is ready.
/// </summary>
public static class ClientContext
{
    public const string HeaderName = "X-Client-Context";

    /// <summary>
    /// The context of an unqualified client: one that has not said which browser it is, so nothing
    /// has been assessed for it and only Baseline Candidates are ready.
    /// </summary>
    public const string Unqualified = "unqualified";

    private const int MaximumLength = 128;

    public static string ClientContextKey(this HttpContext http)
    {
        var offered = http.Request.Headers[HeaderName].ToString();

        if (string.IsNullOrWhiteSpace(offered))
        {
            return Unqualified;
        }

        // The key is a stored identifier, so it is reduced to a fixed, harmless shape rather than
        // trusted as text: a client cannot make it collide with another context by padding it, and
        // it cannot smuggle anything into the column.
        var trimmed = offered.Trim();
        var normalised = trimmed.Length <= MaximumLength &&
                         trimmed.All(character => char.IsAsciiLetterOrDigit(character) ||
                                                  character is '-' or '_' or '.')
            ? trimmed
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(trimmed)))
                .ToLowerInvariant();

        return normalised;
    }
}
