using System.Globalization;
using System.Numerics;

namespace Prdb.Viewer.Core.Library;

/// <summary>
/// How closely two of this installation's own Video Files resemble each other, read from the
/// Perceptual Hashes already computed for them.
///
/// The hash is not a description of a picture but of a 5×5 montage of 25 frames sampled at
/// proportional offsets across the running time, so two facts follow from how it is built and both
/// belong here rather than in a remark somewhere:
///
/// It is robust to resolution, container and bitrate, which is exactly the difference between two
/// encodes of one work. It is <em>not</em> robust to a different running time, because the whole
/// sample grid is a function of the duration: two files whose durations differ sample moments that
/// drift apart and therefore describe different material. A small distance between files whose
/// durations disagree is weak evidence rather than strong evidence — it cannot have come from them
/// showing the same moments, because at different durations they do not.
///
/// The numbers are ADR 0021's, and they were measured rather than assumed. Nothing here decides
/// what is done with a neighbourhood; it says only how close two files are and whether their
/// durations agree.
/// </summary>
public static class PerceptualNeighbourhoodRule
{
    /// <summary>
    /// The number of hex characters a Perceptual Hash is written in. The value is 64 bits, and a
    /// string of any other length is not one — the comparison refuses it rather than padding it
    /// into a number that would then have a distance from everything.
    /// </summary>
    public const int HashLength = 16;

    /// <summary>
    /// The widest Hamming distance at which two Video Files are near-duplicates of each other.
    ///
    /// Six of 64 bits is where the same-work distances end. Measured over twelve excerpts of four
    /// freely licensed films in six encodes each, no pair of encodes of one work sat further than
    /// six apart and no pair of different works came closer than 22; the closest unrelated pair of
    /// deliberately degenerate material — dark, static, near-empty — was 18. Eight is the value
    /// Stash uses and the value a future reader will propose, and it is wrong here: across 5 187
    /// different-work pairs it admits one pair of unrelated near-black files whose durations
    /// happened to coincide, and it catches nothing true that six misses.
    /// </summary>
    public const int NeighbourhoodDistance = 6;

    /// <summary>
    /// How far two running times may differ and still be the same timeline, as a fraction of the
    /// longer one.
    ///
    /// It is not a second opinion bolted on beside the distance but the condition under which a
    /// small distance means what it appears to mean, and it is derived from the distance rather
    /// than guessed: shortening real film by 0.25 % and changing nothing else costs the hash at
    /// most six bits — the whole budget the threshold has to spend — while 0.5 % costs eight, so a
    /// tolerance there would admit pairs whose similarity the shift has already made meaningless.
    /// The two numbers are one number twice.
    /// </summary>
    public const double DurationAgreementTolerance = 0.0025;

    /// <summary>
    /// The Hamming distance between two Perceptual Hashes, or null where there is nothing to
    /// compare. A Video File whose container ffmpeg cannot sample simply has no hash, and that
    /// silence is an absence rather than a distance of zero from every other absence.
    /// </summary>
    public static int? Distance(string? left, string? right) =>
        Parse(left) is { } first && Parse(right) is { } second
            ? BitOperations.PopCount(first ^ second)
            : null;

    /// <summary>
    /// Whether two Video Files are perceptual neighbours: close enough that they may carry the
    /// same work. It says nothing about whether they may be associated without review — that
    /// needs their durations to agree as well.
    /// </summary>
    public static bool AreNeighbours(string? left, string? right) =>
        Distance(left, right) is { } distance && distance <= NeighbourhoodDistance;

    /// <summary>
    /// Whether two running times agree closely enough for the distance between the files to mean
    /// what it appears to mean. The tolerance is measured against the longer of the two, so the
    /// question is symmetric and a pair is never read differently depending on which side is asked
    /// about first. A file whose duration inspection never established cannot agree with anything.
    /// </summary>
    public static bool DurationsAgree(long leftMilliseconds, long rightMilliseconds)
    {
        if (leftMilliseconds <= 0 || rightMilliseconds <= 0)
        {
            return false;
        }

        var longer = Math.Max(leftMilliseconds, rightMilliseconds);
        var difference = Math.Abs(leftMilliseconds - rightMilliseconds);

        return difference <= longer * DurationAgreementTolerance;
    }

    private static ulong? Parse(string? hash) =>
        hash?.Length == HashLength &&
        ulong.TryParse(hash, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
