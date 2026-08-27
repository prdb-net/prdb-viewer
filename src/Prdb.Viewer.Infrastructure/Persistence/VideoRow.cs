namespace Prdb.Viewer.Infrastructure.Persistence;

public sealed class VideoRow
{
    public Guid Id { get; set; }

    public DateTime DiscoveryDate { get; set; }

    public ICollection<VideoFileRow> VideoFiles { get; set; } = [];

    public ICollection<PersonalVideoStateRow> PersonalStates { get; set; } = [];

    public ICollection<PlaybackAttemptRow> PlaybackAttempts { get; set; } = [];
}
