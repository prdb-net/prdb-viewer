using Prdb.Viewer.Core.Library;

namespace Prdb.Viewer.Infrastructure.Persistence;

public sealed class VideoFileRow
{
    public Guid Id { get; set; }

    public Guid VideoId { get; set; }

    public VideoRow Video { get; set; } = null!;

    public Guid LibraryDirectoryId { get; set; }

    public LibraryDirectoryRow LibraryDirectory { get; set; } = null!;

    public required string RelativePath { get; set; }

    public long Size { get; set; }

    public DateTime LastWriteTimeUtc { get; set; }

    public required string Sha256 { get; set; }

    public Guid PublicDeliveryId { get; set; }

    public required string ContainerFormat { get; set; }

    public required string VideoCodec { get; set; }

    public string? AudioCodec { get; set; }

    public long DurationMilliseconds { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public VideoFileAvailability Availability { get; set; }

    public DirectPlayClassification DirectPlayClassification { get; set; }

    public Guid LastObservedScanId { get; set; }

    public int ConsecutiveCompleteAbsences { get; set; }

    public DateTime InspectedAt { get; set; }

    /// <summary>
    /// The Video identity this occurrence carried before a merge moved it. It lets a later split
    /// reactivate the historical identity instead of inventing a new one.
    /// </summary>
    public Guid? PreviousVideoId { get; set; }

    public string? OsHash { get; set; }

    public string? PerceptualHash { get; set; }

    /// <summary>
    /// The observed content the hashes belong to. Content that no longer matches is hashed again
    /// rather than identified against values taken from different bytes.
    /// </summary>
    public string? HashedSha256 { get; set; }

    public DateTime? HashedAt { get; set; }

    public VideoFileHashState HashState { get; set; }

    public string? HashFailureReason { get; set; }

    public Guid? PublicPreviewId { get; set; }

    public string? PreviewRelativePath { get; set; }

    public string? PreviewSha256 { get; set; }

    public DateTime? PreviewGeneratedAt { get; set; }

    public VideoFilePreviewState PreviewState { get; set; }

    public string? IdentifiedSha256 { get; set; }

    public DateTime? IdentifiedAt { get; set; }

    public ICollection<PlaybackAttemptVideoFileRow> PlaybackAttempts { get; set; } = [];
}
