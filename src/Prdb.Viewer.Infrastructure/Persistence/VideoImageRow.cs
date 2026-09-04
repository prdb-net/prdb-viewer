namespace Prdb.Viewer.Infrastructure.Persistence;

using Prdb.Viewer.Core.Library;

/// <summary>
/// One picture prdb offers for a Video's Established Work: where prdb offers it, and where this
/// installation holds it.
/// </summary>
/// <remarks>
/// It is prdb's picture of the work, which is a different thing from the preview this installation
/// generates from the file it actually holds. A screen showing both has to say which is which.
/// Like every other retained picture it is regenerable, so a Backup Archive leaves it out, and it
/// is served from this installation's own origin by a random identifier.
/// </remarks>
public sealed class VideoImageRow
{
    public Guid Id { get; set; }

    public Guid VideoId { get; set; }

    public VideoRow Video { get; set; } = null!;

    public required string PrdbImageId { get; set; }

    /// <summary>Where prdb offers the picture. The browser is never sent here.</summary>
    public required string SourceUrl { get; set; }

    /// <summary>Where this picture stands among the work's, oldest first as prdb orders them.</summary>
    public int Position { get; set; }

    public ActorImageState State { get; set; } = ActorImageState.Pending;

    public Guid? PublicImageId { get; set; }

    public string? RelativePath { get; set; }

    public string? ContentType { get; set; }

    public DateTime? RetainedAt { get; set; }
}
