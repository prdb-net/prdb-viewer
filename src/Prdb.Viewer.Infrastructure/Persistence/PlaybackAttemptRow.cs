using Prdb.Viewer.Core.Personal;

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

    /// <summary>
    /// The longest Uninterrupted Run this Viewing Session reached. It is a summary of the session
    /// rather than a list of its runs: the recommender asks how long the longest one was, and
    /// keeping every run would be keeping a clickstream to answer a question about one number.
    /// </summary>
    public long LongestUninterruptedRunMilliseconds { get; set; }

    /// <summary>The run currently being measured, and where it has reached.</summary>
    public long CurrentRunMilliseconds { get; set; }

    public Guid? CurrentRunVideoFileId { get; set; }

    public long? CurrentRunEndPositionMilliseconds { get; set; }

    /// <summary>
    /// How this session ended, where anything was observed about it. Unknown is the honest default
    /// and is never read as evidence about the Video.
    /// </summary>
    public PlaybackDeparture Departure { get; set; }

    public bool Qualified { get; set; }

    public bool CompletionRecorded { get; set; }

    public ICollection<PlaybackReportRow> Reports { get; set; } = [];

    public ICollection<PlaybackAttemptVideoFileRow> VideoFiles { get; set; } = [];
}
