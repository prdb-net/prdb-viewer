using Prdb.Sdk;

namespace Prdb.Viewer.Infrastructure.Configuration;

/// <summary>
/// Where prdb is. It is the published service unless an installation says otherwise, and saying
/// otherwise is how this product can be exercised against a catalogue that answers on demand.
///
/// The reason to want that is not convenience. prdb recognises content, so a library assembled for
/// a test is in no catalogue and every answer is the same one; and the failures that decide what an
/// Administrator is told — a refused credential, an outage, a rate limit — cannot be asked of the
/// real service at all. Pointing the installation elsewhere is the only way to see any of it.
///
/// The credential travels to whatever this names, so an installation that sets it is trusting that
/// address with its prdb key. The SDK requires https for a credentialed client, which keeps it off
/// the wire in the clear, and the default is the real service.
/// </summary>
public sealed class PrdbEndpoint(string? baseUrl = null)
{
    public string BaseUrl { get; } = string.IsNullOrWhiteSpace(baseUrl)
        ? PrdbClientFactory.DefaultBaseUrl
        : baseUrl;
}
