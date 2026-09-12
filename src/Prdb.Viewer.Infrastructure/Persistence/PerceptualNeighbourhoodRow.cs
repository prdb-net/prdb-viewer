namespace Prdb.Viewer.Infrastructure.Persistence;

/// <summary>
/// The durable fact that two Video Files of this installation look alike: the pair, how far apart
/// their Perceptual Hashes are, whether their running times agree, and when that was established.
///
/// It keeps what it was measured from rather than only the verdict, because a later rung is read
/// as evidence rather than as a number somebody has to trust: an Administrator looking at a
/// proposal derived from this pair can see the two hashes, the two running times and the distance
/// that came out of them. The pair is held once, with the smaller identifier on the left, so a
/// neighbourhood is one row whichever of the two files is asked about.
/// </summary>
public sealed class PerceptualNeighbourhoodRow
{
    public Guid Id { get; set; }

    public Guid LeftVideoFileId { get; set; }

    public VideoFileRow LeftVideoFile { get; set; } = null!;

    public Guid RightVideoFileId { get; set; }

    public VideoFileRow RightVideoFile { get; set; } = null!;

    /// <summary>The Hamming distance between the two Perceptual Hashes, in bits of 64.</summary>
    public int Distance { get; set; }

    /// <summary>
    /// The two hashes the distance was measured between. A file hashed again to a different value
    /// invalidates what was computed from the old one, so these are what says which value that was.
    /// </summary>
    public required string LeftPerceptualHash { get; set; }

    public required string RightPerceptualHash { get; set; }

    public long LeftDurationMilliseconds { get; set; }

    public long RightDurationMilliseconds { get; set; }

    /// <summary>
    /// Whether the two running times agree closely enough for the distance to mean what it appears
    /// to mean. It is decided once, here, by the rule that owns the tolerance — a reader of this
    /// row is reading a conclusion rather than recomputing one.
    /// </summary>
    public bool DurationsAgree { get; set; }

    public DateTime EstablishedAt { get; set; }
}
