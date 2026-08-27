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
}
