using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

public sealed class LibraryScanRunner(ViewerDbContext database, TimeProvider timeProvider)
{
    private const int DirectoriesPerSlice = 8;

    public async Task<bool> RunNextSliceAsync(CancellationToken cancellationToken = default)
    {
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
            return false;
        }

        if (work.LibraryDirectory.State != LibraryDirectoryState.Active ||
            work.LibraryDirectory.ConfigurationGeneration != work.ConfigurationGeneration)
        {
            work.State = BackgroundWorkState.Cancelled;
            work.FinishedAt = Now();
            work.UpdatedAt = work.FinishedAt.Value;
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }

        var now = Now();
        work.CompletedItemCount = work.DiscoveredCandidateCount;
        work.State = BackgroundWorkState.Running;
        work.StartedAt ??= now;
        work.UpdatedAt = now;
        var pending = Deserialize(work.PendingDirectoriesJson);
        var processed = 0;

        while (pending.Count > 0 && processed++ < DirectoriesPerSlice)
        {
            var relativeDirectory = pending[0];
            pending.RemoveAt(0);
            ScanDirectory(work, relativeDirectory, pending);
        }

        work.PendingDirectoriesJson = JsonSerializer.Serialize(pending);

        if (pending.Count == 0)
        {
            await CompleteTraversalAsync(work, cancellationToken);
        }

        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    private void ScanDirectory(
        BackgroundWorkRow work,
        string relativeDirectory,
        List<string> pending)
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
            AddIssue(work, relativeDirectory, WorkIssueCause.SourceAccess,
                "A directory could not be traversed safely.",
                "Restore the mount or permissions, then request another Library Scan.");
            work.CoverageComplete = false;
            return;
        }

        if (!IsWithin(root, path) || linked)
        {
            AddIssue(work, relativeDirectory, WorkIssueCause.SourceAccess,
                "A directory could not be traversed safely.",
                "Ensure every mounted directory resolves beneath the configured Library Directory.");
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
            AddIssue(work, relativeDirectory, WorkIssueCause.SourceAccess,
                "This part of the Library Directory could not be read.",
                "Restore the mount or permissions, then request another Library Scan.");
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
                        AddIssue(work, relative, WorkIssueCause.SourceAccess,
                            "A linked Video File Candidate was skipped.",
                            "Mount the source beneath the Library Directory instead of linking outside it.");
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
                AddIssue(work, ToStoredPath(Path.GetRelativePath(root, entry)),
                    WorkIssueCause.SourceAccess,
                    "A filesystem entry could not be read.",
                    "Restore the mount or permissions, then request another Library Scan.");
            }
        }
    }

    private async Task CompleteTraversalAsync(
        BackgroundWorkRow work,
        CancellationToken cancellationToken)
    {
        if (work.CoverageComplete)
        {
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
        }

        var now = Now();
        work.State = work.IssueCount == 0
            ? BackgroundWorkState.Completed
            : BackgroundWorkState.CompletedWithIssues;
        work.FinishedAt = now;
        work.UpdatedAt = now;
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
                LibraryDirectoryId = work.LibraryDirectoryId,
                ConfigurationGeneration = work.ConfigurationGeneration,
                PendingDirectoriesJson = JsonSerializer.Serialize(new[] { string.Empty }),
                CoverageComplete = true,
                RequestedAt = now,
                UpdatedAt = now,
            });
        }
    }

    private void AddIssue(
        BackgroundWorkRow work,
        string scope,
        WorkIssueCause cause,
        string impact,
        string requiredAction)
    {
        work.IssueCount++;
        database.WorkIssues.Add(new WorkIssueRow
        {
            Id = Guid.CreateVersion7(),
            BackgroundWorkId = work.Id,
            Severity = string.IsNullOrEmpty(scope)
                ? WorkIssueSeverity.OperationalBlocker
                : WorkIssueSeverity.ScopedIssue,
            Cause = cause,
            RemediationOwner = RemediationOwner.InstallationOperator,
            AffectedScope = string.IsNullOrEmpty(scope) ? "." : scope,
            Impact = impact,
            RequiredAction = requiredAction,
            CreatedAt = Now(),
        });
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
