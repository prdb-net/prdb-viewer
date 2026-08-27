namespace Prdb.Viewer.Infrastructure.Persistence;

public sealed class PlaybackAttemptRow
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public AccountRow Account { get; set; } = null!;

    public Guid VideoId { get; set; }

    public VideoRow Video { get; set; } = null!;

    public DateTime AttemptedAt { get; set; }

    public DateTime? ViewingSessionBeganAt { get; set; }

    public DateTime? LastActivityAt { get; set; }

    public DateTime? EndedAt { get; set; }

    public int LastReportSequence { get; set; } = -1;

    public long? LastPositionMilliseconds { get; set; }

    public long ActiveWatchDurationMilliseconds { get; set; }

    public bool Qualified { get; set; }

    public bool CompletionRecorded { get; set; }

    public ICollection<PlaybackReportRow> Reports { get; set; } = [];

    public ICollection<PlaybackAttemptVideoFileRow> VideoFiles { get; set; } = [];
}
