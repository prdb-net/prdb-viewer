namespace Prdb.Viewer.Infrastructure.Persistence;

public sealed class PlaybackAttemptVideoFileRow
{
    public Guid PlaybackAttemptId { get; set; }

    public PlaybackAttemptRow PlaybackAttempt { get; set; } = null!;

    public Guid VideoFileId { get; set; }

    public VideoFileRow VideoFile { get; set; } = null!;
}
