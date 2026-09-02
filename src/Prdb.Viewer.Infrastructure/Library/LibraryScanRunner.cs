using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

public sealed class LibraryScanRunner(
    ViewerDbContext database,
    LibraryWorkScheduler scheduler,
    WorkIssueRecorder issues,
    VideoProjection projection,
    TimeProvider timeProvider)
{
    private const int DirectoriesPerSlice = 8;

    public async Task<bool> RunNextSliceAsync(CancellationToken cancellationToken = default)
    {
        if (await BackgroundWorkGate.IsPausedAsync(database, cancellationToken))
        {
            return await BackgroundWorkGate.ParkAsync(
                database,
                BackgroundWorkCategory.LibraryScan,
                Now(),
                cancellationToken);
        }

        var work = await database.BackgroundWork
            .AsTracking()
            .Include(row => row.LibraryDirectory)
            .Where(row => row.Category == BackgroundWorkCategory.LibraryScan &&
                          (row.State == BackgroundWorkState.Queued ||
                           row.State == BackgroundWorkState.Running))
            .OrderBy(row => row.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (work is null)
        {
            // An empty lane is the only moment a Library Directory that has fallen due can be
            // given its periodic Scan: nothing is in flight for it to coalesce into, and the
            // Administrator's own request is never made to wait behind one nobody asked for.
            return await scheduler.QueueDueScansAsync(cancellationToken);
        }

        // A cancelled or superseded scan is not a complete observation of its unvisited scope, so
        // it stops here and never reconciles absences.
        if (work.CancellationRequested ||
            work.LibraryDirectory.State != LibraryDirectoryState.Active ||
            work.LibraryDirectory.ConfigurationGeneration != work.ConfigurationGeneration)
        {
            work.State = BackgroundWorkState.Cancelled;
            work.Phase = BackgroundWorkPhases.Settled;
            work.CoverageComplete = false;
            work.FinishedAt = Now();
            work.UpdatedAt = work.FinishedAt.Value;
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }

        var now = Now();
        work.State = BackgroundWorkState.Running;
        work.Phase = BackgroundWorkPhases.Traversing;
        work.StartedAt ??= now;
        work.LastActivityAt = now;
        work.UpdatedAt = now;
        var pending = Deserialize(work.PendingDirectoriesJson);
        var reports = new List<WorkIssueReport>();
        var processed = 0;

        while (pending.Count > 0 && processed++ < DirectoriesPerSlice)
        {
            var relativeDirectory = pending[0];
            pending.RemoveAt(0);
            ScanDirectory(work, relativeDirectory, pending, reports);
        }

        work.PendingDirectoriesJson = JsonSerializer.Serialize(pending);

        // A traversal has no step between finding a candidate and being done with it: the candidate
        // row is written as it is found. Counted before the slice rather than after it, the tally
        // trailed by one slice and a scan that finished in a single slice settled at none of the
        // candidates it had just recorded.
        work.CompletedItemCount = work.DiscoveredCandidateCount;

        foreach (var report in reports)
        {
            await issues.RecordAsync(work, report, cancellationToken);
        }

        if (pending.Count == 0)
        {
            await CompleteTraversalAsync(work, cancellationToken);
        }

        // Reconciling an absence changes Video File Availability, which is a projected fact.
        await projection.RefreshTrackedAsync(cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    private void ScanDirectory(
        BackgroundWorkRow work,
        string relativeDirectory,
        List<string> pending,
        List<WorkIssueReport> reports)
    {
        var root = Path.TrimEndingDirectorySeparator(work.LibraryDirectory.ContainerPath);
        string path;
        bool linked;

        try
        {
            path = Path.GetFullPath(Path.Combine(root, FromStoredPath(relativeDirectory)));
            linked = relativeDirectory.Length > 0 && IsLink(path);
        }
        catch (Exception exception) when (exception is ArgumentException or UnauthorizedAccessException or IOException)
        {
            reports.Add(ScopeReport(
                work,
                relativeDirectory,
                root,
                "The directory path could not be resolved beneath the Library Directory."));
            work.CoverageComplete = false;
            return;
        }

        if (!IsWithin(root, path) || linked)
        {
            reports.Add(ScopeReport(
                work,
                relativeDirectory,
                root,
                "The directory resolves outside the configured Library Directory."));
            work.CoverageComplete = false;
            return;
        }

        string[] entries;

        try
        {
            entries = Directory.EnumerateFileSystemEntries(path)
                .Order(StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            reports.Add(ScopeReport(work, relativeDirectory, root, SafeAccessCause(exception)));
            work.CoverageComplete = false;
            return;
        }

        foreach (var entry in entries)
        {
            try
            {
                var attributes = File.GetAttributes(entry);
                var relative = ToStoredPath(Path.GetRelativePath(root, entry));

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    if (VideoFileCandidatePolicy.Recognizes(Path.GetExtension(entry)))
                    {
                        reports.Add(ItemReport(
                            work,
                            relative,
                            entry,
                            "The entry is a link that leaves the Library Directory.",
                            "Mount the source beneath the Library Directory instead of linking " +
                            "outside it, then use Check again."));
                    }

                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Add(relative);
                    continue;
                }

                if (!VideoFileCandidatePolicy.Recognizes(Path.GetExtension(entry)))
                {
                    continue;
                }

                var file = new FileInfo(entry);
                database.VideoFileCandidates.Add(new VideoFileCandidateRow
                {
                    Id = Guid.CreateVersion7(),
                    LibraryScanId = work.LibraryScanId!.Value,
                    LibraryDirectoryId = work.LibraryDirectoryId,
                    RelativePath = relative,
                    ObservedSize = file.Length,
                    ObservedLastWriteTimeUtc = file.LastWriteTimeUtc,
                    State = VideoFileCandidateState.Pending,
                });
                work.DiscoveredCandidateCount++;
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                work.CoverageComplete = false;
                reports.Add(ItemReport(
                    work,
                    ToStoredPath(Path.GetRelativePath(root, entry)),
                    entry,
                    SafeAccessCause(exception),
                    "Restore the mount or permissions, then use Check again."));
            }
        }
    }

    /// <summary>
    /// A root or subtree that cannot be read is a shared scope, so it blocks a meaningful work
    /// area rather than describing one item.
    /// </summary>
    private static WorkIssueReport ScopeReport(
        BackgroundWorkRow work,
        string relativeDirectory,
        string root,
        string safeCause)
    {
        var isRoot = relativeDirectory.Length == 0;
        var name = work.LibraryDirectory.Name;

        return new WorkIssueReport(
            WorkIssueCause.SourceAccess,
            WorkIssueSeverity.OperationalBlocker,
            WorkIssueRetryDisposition.RetriesExhausted,
            isRoot ? name : relativeDirectory,
            $"{name}:{(isRoot ? "root" : "partial")}",
            BackgroundWorkPhases.Traversing,
            isRoot
                ? WorkIssueMessages.DirectoryCannotBeScanned(name)
                : WorkIssueMessages.PartOfDirectoryCannotBeScanned(name),
            "The Library Scan could not observe this part of the directory. Because the " +
            "observation is incomplete, no Video File is advanced towards Missing and healthy " +
            "sibling directories continue to be scanned.",
            isRoot
                ? "Nothing in this Library Directory can be discovered or reconciled."
                : "Content beneath this path cannot be discovered or reconciled.",
            "Ask the Installation Operator to restore the mount or its permissions, then use " +
            "Check again.",
            safeCause,
            "Trustworthy access to the path followed by a Library Scan that completes its " +
            "traversal.")
        {
            ContainerPath = isRoot
                ? root
                : Path.Combine(root, relativeDirectory.Replace('/', Path.DirectorySeparatorChar)),
            AggregatesItems = false,
        };
    }

    /// <summary>
    /// One unreadable entry is independent: it stays a Scoped Issue and never establishes
    /// Operational Attention by itself, however many equivalent entries aggregate with it.
    /// </summary>
    private static WorkIssueReport ItemReport(
        BackgroundWorkRow work,
        string relativePath,
        string containerPath,
        string safeCause,
        string requiredAction) =>
        new(WorkIssueCause.SourceAccess,
            WorkIssueSeverity.ScopedIssue,
            WorkIssueRetryDisposition.RetriesExhausted,
            relativePath,
            $"{work.LibraryDirectory.Name}:items",
            BackgroundWorkPhases.Traversing,
            WorkIssueMessages.CannotReadFile(Path.GetFileName(relativePath)),
            "The entry could not be read where the library expects it. Unobserved content is not " +
            "treated as Missing or Removed, and unrelated files continue to be discovered.",
            "This file is not admitted to technical inspection.",
            requiredAction,
            safeCause,
            "A readable entry at the same path followed by successful technical inspection.")
        {
            ContainerPath = containerPath,
        };

    /// <summary>The access class an operator may see, never a stack trace or host path.</summary>
    private static string SafeAccessCause(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "The container is not permitted to read the path.",
        DirectoryNotFoundException => "The path does not exist in the container.",
        FileNotFoundException => "The path does not exist in the container.",
        _ => "The path could not be read from the mounted filesystem.",
    };

    private async Task CompleteTraversalAsync(
        BackgroundWorkRow work,
        CancellationToken cancellationToken)
    {
        if (work.CoverageComplete)
        {
            work.Phase = BackgroundWorkPhases.Reconciling;
            var observedPaths = await database.VideoFileCandidates
                .Where(candidate => candidate.LibraryScanId == work.LibraryScanId)
                .Select(candidate => candidate.RelativePath)
                .ToHashSetAsync(StringComparer.Ordinal, cancellationToken);
            var absent = await database.VideoFiles
                .AsTracking()
                .Where(file => file.LibraryDirectoryId == work.LibraryDirectoryId &&
                               file.Availability != VideoFileAvailability.Removed &&
                               file.Availability != VideoFileAvailability.Replaced)
                .ToListAsync(cancellationToken);

            foreach (var file in absent.Where(file => !observedPaths.Contains(file.RelativePath)))
            {
                file.ConsecutiveCompleteAbsences++;
                file.Availability = file.ConsecutiveCompleteAbsences >= 2
                    ? VideoFileAvailability.Missing
                    : VideoFileAvailability.Unreachable;
            }

            await issues.ResolveAsync(
                work.LibraryDirectoryId,
                BackgroundWorkCategory.LibraryScan,
                WorkIssueCause.SourceAccess,
                "A Library Scan completed its traversal of the whole Library Directory.",
                cancellationToken);
        }

        var now = Now();
        var unresolved = await database.WorkIssues.AnyAsync(
            issue => issue.LibraryDirectoryId == work.LibraryDirectoryId &&
                     issue.Category == BackgroundWorkCategory.LibraryScan &&
                     issue.ResolvedAt == null,
            cancellationToken);
        work.State = unresolved
            ? BackgroundWorkState.CompletedWithIssues
            : BackgroundWorkState.Completed;
        work.Phase = BackgroundWorkPhases.Settled;
        work.FinishedAt = now;
        work.UpdatedAt = now;
        // The period runs from the observation that just finished, so a Library Directory that
        // took an hour to walk is not immediately due again.
        work.LibraryDirectory.NextScanDueAt = LibraryScanSchedule.NextDueAfter(now);
        work.LibraryDirectory.Health = work.CoverageComplete
            ? LibraryDirectoryHealth.Healthy
            : work.DiscoveredCandidateCount == 0
                ? LibraryDirectoryHealth.Unreachable
                : LibraryDirectoryHealth.PartiallyUnreachable;

        database.BackgroundWork.Add(new BackgroundWorkRow
        {
            Id = Guid.CreateVersion7(),
            Category = BackgroundWorkCategory.TechnicalInspection,
            State = BackgroundWorkState.Queued,
            Trigger = BackgroundWorkTrigger.FollowUpWork,
            Phase = BackgroundWorkPhases.Queued,
            LibraryDirectoryId = work.LibraryDirectoryId,
            ConfigurationGeneration = work.ConfigurationGeneration,
            LibraryScanId = work.LibraryScanId,
            DiscoveredCandidateCount = work.DiscoveredCandidateCount,
            RequestedAt = now,
            UpdatedAt = now,
        });

        if (work.FollowUpRequested)
        {
            var followUp = Guid.CreateVersion7();
            database.BackgroundWork.Add(new BackgroundWorkRow
            {
                Id = followUp,
                LibraryScanId = followUp,
                Category = BackgroundWorkCategory.LibraryScan,
                State = BackgroundWorkState.Queued,
                Trigger = BackgroundWorkTrigger.FollowUpWork,
                Phase = BackgroundWorkPhases.Queued,
                LibraryDirectoryId = work.LibraryDirectoryId,
                ConfigurationGeneration = work.ConfigurationGeneration,
                PendingDirectoriesJson = JsonSerializer.Serialize(new[] { string.Empty }),
                CoverageComplete = true,
                RequestedAt = now,
                UpdatedAt = now,
            });
        }
    }

    private static List<string> Deserialize(string? json) =>
        JsonSerializer.Deserialize<List<string>>(json ?? "[]") ?? [];

    private static string ToStoredPath(string path) => path.Replace('\\', '/');

    private static string FromStoredPath(string path) =>
        path.Replace('/', Path.DirectorySeparatorChar);

    private static bool IsWithin(string root, string candidate) =>
        candidate.Equals(root, Comparison) ||
        candidate.StartsWith($"{root}{Path.DirectorySeparatorChar}", Comparison);

    private static StringComparison Comparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static bool IsLink(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
}
