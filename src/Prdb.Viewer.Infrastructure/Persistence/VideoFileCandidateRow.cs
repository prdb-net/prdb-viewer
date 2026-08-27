namespace Prdb.Viewer.Infrastructure.Persistence;

public enum VideoFileCandidateState
{
    Pending,
    Inspecting,
    Accepted,
    Rejected,
}

public sealed class VideoFileCandidateRow
{
    public Guid Id { get; set; }

    public Guid LibraryScanId { get; set; }

    public Guid LibraryDirectoryId { get; set; }

    public required string RelativePath { get; set; }

    public long ObservedSize { get; set; }

    public DateTime ObservedLastWriteTimeUtc { get; set; }

    public VideoFileCandidateState State { get; set; }

    public int AttemptCount { get; set; }
}
