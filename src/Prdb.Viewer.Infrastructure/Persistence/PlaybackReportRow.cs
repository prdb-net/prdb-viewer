namespace Prdb.Viewer.Infrastructure.Persistence;

public sealed class PlaybackReportRow
{
    public Guid Id { get; set; }

    public Guid PlaybackAttemptId { get; set; }

    public PlaybackAttemptRow PlaybackAttempt { get; set; } = null!;

    public int Sequence { get; set; }

    public long PositionMilliseconds { get; set; }

    public long ActiveWatchingMilliseconds { get; set; }

    public DateTime? ActivityStartedAt { get; set; }

    public DateTime? ActivityEndedAt { get; set; }

    public DateTime ReceivedAt { get; set; }
}
