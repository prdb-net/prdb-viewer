using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// Finds the Video Files of this installation that look like each other, by comparing the
/// Perceptual Hashes already computed for them. It reads no media and contacts no service, so it
/// keeps working while prdb is unreachable — which is the point, because the files it matters most
/// for are the ones prdb had no answer about.
///
/// It works through a backlog rather than comparing everything on every run. A file is compared
/// once for the hash value it currently has, against every other file that has one; a file hashed
/// again to a different value is compared again, and the neighbourhoods computed from its old
/// value are dropped first. A library of tens of thousands of Video Files therefore pays for each
/// file once instead of paying for every pair on every pass, and a restart resumes where it
/// stopped because the backlog is durable state rather than a position in a loop.
/// </summary>
public sealed class PerceptualNeighbourhoodRunner(
    ViewerDbContext database,
    WorkIssueRecorder issues,
    TimeProvider timeProvider) : VideoFileWorkRunner(database, issues, timeProvider)
{
    /// <summary>
    /// How many files of the rest of the library are held in memory at once while a batch is
    /// compared against them. One pass over the library per batch, in pieces small enough that the
    /// pass costs the same on a library of two hundred thousand files as on one of two thousand.
    /// </summary>
    private const int CorpusPageSize = 10_000;

    /// <summary>One file as the comparison reads it: an identity, a hash, and a running time.</summary>
    private sealed record Compared(Guid Id, string PerceptualHash, long DurationMilliseconds);

    protected override BackgroundWorkCategory Category =>
        BackgroundWorkCategory.PerceptualNeighbourhood;

    protected override string Phase => BackgroundWorkPhases.Comparing;

    /// <summary>
    /// Each batch costs one pass over the library, so the batch is what that pass is amortised
    /// over. Sixty-four files is small enough to commit often — a cancelled or restarted run keeps
    /// everything it has established — and large enough that the pass is read once for many files
    /// rather than once for each.
    /// </summary>
    protected override int BatchSize => 64;

    /// <summary>
    /// The Available occurrences of this Library Directory that carry a Perceptual Hash nothing has
    /// been compared against yet. A file whose container ffmpeg could not sample has no hash and is
    /// not outstanding: that silence is the same absence the remote ladder already lives with
    /// rather than an obstacle to report.
    /// </summary>
    protected override IQueryable<VideoFileRow> Outstanding(Guid libraryDirectoryId) =>
        Database.VideoFiles.Where(file =>
            file.LibraryDirectoryId == libraryDirectoryId &&
            file.Availability == VideoFileAvailability.Available &&
            file.PerceptualHash != null &&
            (file.NeighbourhoodComparedHash == null ||
             file.NeighbourhoodComparedHash != file.PerceptualHash));

    /// <summary>
    /// Nothing here can fail in a way a later run could do better at. A comparison is arithmetic
    /// over two values the Hashing lane already established, so a file either has a hash and is
    /// compared or has none and is not; there is no failed state to retry.
    /// </summary>
    protected override Task RetryEarlierFailuresAsync(
        Guid libraryDirectoryId,
        CancellationToken cancellationToken) => Task.CompletedTask;

    protected override async Task AdvanceAsync(
        BackgroundWorkRow work,
        IReadOnlyList<VideoFileRow> files,
        CancellationToken cancellationToken)
    {
        var batch = files
            .Where(file => file.PerceptualHash is not null)
            .Select(file => new Compared(
                file.Id,
                file.PerceptualHash!,
                file.DurationMilliseconds))
            .ToArray();
        var subjects = batch.Select(file => file.Id).ToArray();

        // Whatever these files were found to resemble was computed from the hash values they used
        // to carry. Those values are gone, so the conclusions drawn from them go with them rather
        // than lingering as facts about content nobody has compared.
        await Database.PerceptualNeighbourhoods
            .Where(row => subjects.Contains(row.LeftVideoFileId) ||
                          subjects.Contains(row.RightVideoFileId))
            .ExecuteDeleteAsync(cancellationToken);

        var found = new Dictionary<(Guid Left, Guid Right), PerceptualNeighbourhoodRow>();
        var now = Now();

        for (var skipped = 0; ; skipped += CorpusPageSize)
        {
            var page = await Database.VideoFiles
                .AsNoTracking()
                .Where(file => file.PerceptualHash != null &&
                               file.Availability != VideoFileAvailability.Removed)
                .OrderBy(file => file.Id)
                .Skip(skipped)
                .Take(CorpusPageSize)
                .Select(file => new Compared(
                    file.Id,
                    file.PerceptualHash!,
                    file.DurationMilliseconds))
                .ToListAsync(cancellationToken);

            foreach (var other in page)
            {
                foreach (var subject in batch)
                {
                    if (other.Id == subject.Id)
                    {
                        continue;
                    }

                    var distance = PerceptualNeighbourhoodRule.Distance(
                        subject.PerceptualHash,
                        other.PerceptualHash);

                    if (distance is not { } bits || bits > PerceptualNeighbourhoodRule.NeighbourhoodDistance)
                    {
                        continue;
                    }

                    // Two files of the batch resemble each other from both sides of the pass. The
                    // pair is one fact, so it is keyed by the pair rather than by who found it.
                    var pair = Pair(subject, other);
                    found.TryAdd(
                        (pair.Left.Id, pair.Right.Id),
                        new PerceptualNeighbourhoodRow
                        {
                            Id = Guid.CreateVersion7(),
                            LeftVideoFileId = pair.Left.Id,
                            RightVideoFileId = pair.Right.Id,
                            Distance = bits,
                            LeftPerceptualHash = pair.Left.PerceptualHash,
                            RightPerceptualHash = pair.Right.PerceptualHash,
                            LeftDurationMilliseconds = pair.Left.DurationMilliseconds,
                            RightDurationMilliseconds = pair.Right.DurationMilliseconds,
                            DurationsAgree = PerceptualNeighbourhoodRule.DurationsAgree(
                                pair.Left.DurationMilliseconds,
                                pair.Right.DurationMilliseconds),
                            EstablishedAt = now,
                        });
                }
            }

            if (page.Count < CorpusPageSize)
            {
                break;
            }
        }

        Database.PerceptualNeighbourhoods.AddRange(found.Values);

        var compared = await Database.VideoFiles
            .AsTracking()
            .Where(file => subjects.Contains(file.Id))
            .ToListAsync(cancellationToken);

        foreach (var file in compared)
        {
            file.NeighbourhoodComparedHash = file.PerceptualHash;
            file.NeighbourhoodComparedAt = now;
        }

        work.CompletedItemCount += files.Count;
    }

    /// <summary>
    /// Puts a pair in the one order it is held in, so the same two files are the same row whichever
    /// of them the search happened to reach first.
    /// </summary>
    private static (Compared Left, Compared Right) Pair(Compared one, Compared other) =>
        one.Id.CompareTo(other.Id) <= 0 ? (one, other) : (other, one);
}
