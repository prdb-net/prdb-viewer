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

    public string? Url { get; set; }
}
