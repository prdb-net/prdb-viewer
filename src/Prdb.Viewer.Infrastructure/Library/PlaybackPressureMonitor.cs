namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// Keeps interactive playback ahead of Background Work. Video delivery notes activity here and the
/// lanes reduce their own pressure while it lasts, which slows throughput without losing a single
/// committed result.
/// </summary>
public sealed class PlaybackPressureMonitor(TimeProvider timeProvider)
{
    /// <summary>
    /// How long after the last delivered byte range playback still counts as active. It comfortably
    /// covers the gaps between the range requests a browser makes while a Video plays.
    /// </summary>
    public static readonly TimeSpan ActiveWindow = TimeSpan.FromSeconds(30);

    private long lastActivityTicks;

    public void NoteDelivery() =>
        Interlocked.Exchange(ref lastActivityTicks, timeProvider.GetUtcNow().UtcTicks);

    public bool PlaybackIsActive
    {
        get
        {
            var ticks = Interlocked.Read(ref lastActivityTicks);

            return ticks != 0 &&
                   timeProvider.GetUtcNow().UtcTicks - ticks < ActiveWindow.Ticks;
        }
    }
}
