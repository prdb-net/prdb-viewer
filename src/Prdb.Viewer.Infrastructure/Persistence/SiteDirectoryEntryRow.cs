namespace Prdb.Viewer.Infrastructure.Persistence;

/// <summary>
/// One site of the retained Site Directory: the vocabulary local Site Recognition reads a Video
/// File's path against. The whole directory is a regenerable copy of what prdb publishes, so it is
/// replaced wholesale on every refresh and is deliberately absent from a Backup Archive.
/// </summary>
public sealed class SiteDirectoryEntryRow
{
    public required string SiteKey { get; set; }

    public required string Title { get; set; }

    public bool IsDeleted { get; set; }

    public string? PlatformId { get; set; }
    public string? PlatformKey { get; set; }
    public string? PlatformTitle { get; set; }
    public string? AccountHandle { get; set; }
    public string? LinksJson { get; set; }

    public string? Url { get; set; }
}
