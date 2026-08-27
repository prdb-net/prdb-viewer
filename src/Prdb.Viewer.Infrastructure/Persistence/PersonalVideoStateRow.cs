using Prdb.Viewer.Core.Personal;

namespace Prdb.Viewer.Infrastructure.Persistence;

public sealed class PersonalVideoStateRow
{
    public Guid AccountId { get; set; }

    public AccountRow Account { get; set; } = null!;

    public Guid VideoId { get; set; }

    public VideoRow Video { get; set; } = null!;

    public long? PlaybackProgressMilliseconds { get; set; }

    public Guid? ProgressVideoFileId { get; set; }

    public long AccumulatedWatchDurationMilliseconds { get; set; }

    public int PlayCount { get; set; }

    public bool HasViewingCompletion { get; set; }

    public DateTime? LastCompletedAt { get; set; }

    public PersonalPlayState PlayState { get; set; }

    public DateTime? PlayStateChangedAt { get; set; }

    public DateTime? LastQualifiedActivityAt { get; set; }

    public DateTime? ContinueWatchingDismissedAt { get; set; }

    public DateTime? FavouriteAddedAt { get; set; }

    public DateTime? WatchLaterAddedAt { get; set; }

    public int? PersonalRating { get; set; }

    public DateTime UpdatedAt { get; set; }
}
