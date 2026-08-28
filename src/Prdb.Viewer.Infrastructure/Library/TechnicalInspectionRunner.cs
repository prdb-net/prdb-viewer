using System.Security.Cryptography;

using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

public sealed class TechnicalInspectionRunner(
    ViewerDbContext database,
    IMediaProbe mediaProbe,
    WorkIssueRecorder issues,
    TimeProvider timeProvider)
{
    public async Task<bool> RunNextSliceAsync(CancellationToken cancellationToken = default)
    {
        if (await BackgroundWorkGate.IsPausedAsync(database, cancellationToken))
        {
            return await BackgroundWorkGate.ParkAsync(
                database,
                BackgroundWorkCategory.TechnicalInspection,
                Now(),
                cancellationToken);
        }

        var work = await database.BackgroundWork
            .AsTracking()
            .Include(row => row.LibraryDirectory)
            .Where(row => row.Category == BackgroundWorkCategory.TechnicalInspection &&
                          (row.State == BackgroundWorkState.Queued ||
                           row.State == BackgroundWorkState.Running))
            .OrderBy(row => row.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (work is null)
        {
            return false;
        }

        if (work.CancellationRequested ||
            work.LibraryDirectory.State != LibraryDirectoryState.Active ||
            work.LibraryDirectory.ConfigurationGeneration != work.ConfigurationGeneration)
        {
            await FinishAsync(work, BackgroundWorkState.Cancelled, cancellationToken);
            return true;
        }

        await database.VideoFileCandidates
            .Where(candidate => candidate.LibraryScanId == work.LibraryScanId &&
                                candidate.State == VideoFileCandidateState.Inspecting)
            .ExecuteUpdateAsync(update => update
                .SetProperty(candidate => candidate.State, VideoFileCandidateState.Pending),
                cancellationToken);
        var candidate = await database.VideoFileCandidates
            .AsTracking()
            .Where(row => row.LibraryScanId == work.LibraryScanId &&
                          row.State == VideoFileCandidateState.Pending)
            .OrderBy(row => row.RelativePath)
            .FirstOrDefaultAsync(cancellationToken);

        if (candidate is null)
        {
            await database.VideoFileCandidates
                .Where(row => row.LibraryScanId == work.LibraryScanId)
                .ExecuteDeleteAsync(cancellationToken);
            await QueueDerivedWorkAsync(work, cancellationToken);
            var unresolved = await database.WorkIssues.AnyAsync(
                issue => issue.LibraryDirectoryId == work.LibraryDirectoryId &&
                         issue.Category == BackgroundWorkCategory.TechnicalInspection &&
                         issue.ResolvedAt == null,
                cancellationToken);
            await FinishAsync(
                work,
                unresolved
                    ? BackgroundWorkState.CompletedWithIssues
                    : BackgroundWorkState.Completed,
                cancellationToken);
            return true;
        }

        var now = Now();
        work.State = BackgroundWorkState.Running;
        work.Phase = BackgroundWorkPhases.Inspecting;
        work.StartedAt ??= now;
        work.LastActivityAt = now;
        work.UpdatedAt = now;
        candidate.State = VideoFileCandidateState.Inspecting;
        candidate.AttemptCount++;
        await database.SaveChangesAsync(cancellationToken);

        var path = SourceFile.Resolve(work.LibraryDirectory.ContainerPath, candidate.RelativePath);
        FileObservation? observation = null;
        MediaProbeFacts? facts = null;

        if (path is not null)
        {
            try
            {
                observation = await ObserveAsync(path, cancellationToken);
                facts = await mediaProbe.InspectAsync(path, cancellationToken);
                var after = new FileInfo(path);

                if (after.Length != observation.Size ||
                    after.LastWriteTimeUtc != observation.LastWriteTimeUtc ||
                    observation.Size != candidate.ObservedSize ||
                    observation.LastWriteTimeUtc != candidate.ObservedLastWriteTimeUtc)
                {
                    observation = null;
                    facts = null;
                }
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                observation = null;
                facts = null;
            }
        }

        database.ChangeTracker.Clear();
        work = await database.BackgroundWork
            .AsTracking()
            .Include(row => row.LibraryDirectory)
            .SingleAsync(row => row.Id == work.Id, cancellationToken);
        candidate = await database.VideoFileCandidates
            .AsTracking()
            .SingleAsync(row => row.Id == candidate.Id, cancellationToken);

        if (work.LibraryDirectory.ConfigurationGeneration != work.ConfigurationGeneration)
        {
            candidate.State = VideoFileCandidateState.Rejected;
            await FinishAsync(work, BackgroundWorkState.Cancelled, cancellationToken);
            return true;
        }

        if (observation is null)
        {
            await RejectAsync(work, candidate, path, Changing(work, candidate, path), cancellationToken);
        }
        else if (facts is null)
        {
            await MarkReplacedIfDifferentAsync(work, candidate, observation.Sha256, cancellationToken);
            await RejectAsync(work, candidate, path, Invalid(work, candidate, path), cancellationToken);
        }
        else
        {
            await AcceptAsync(work, candidate, observation, facts, cancellationToken);
        }

        work.CompletedItemCount++;
        work.LastActivityAt = Now();
        work.UpdatedAt = work.LastActivityAt.Value;
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task AcceptAsync(
        BackgroundWorkRow work,
        VideoFileCandidateRow candidate,
        FileObservation observation,
        MediaProbeFacts facts,
        CancellationToken cancellationToken)
    {
        var current = await database.VideoFiles
            .AsTracking()
            .Where(file => file.LibraryDirectoryId == work.LibraryDirectoryId &&
                           file.RelativePath == candidate.RelativePath &&
                           file.Availability != VideoFileAvailability.Replaced &&
                           file.Availability != VideoFileAvailability.Removed)
            .OrderByDescending(file => file.InspectedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (current is not null && current.Sha256 != observation.Sha256)
        {
            current.Availability = VideoFileAvailability.Replaced;
            current = null;
        }

        if (current is null)
        {
            var renameMatches = await database.VideoFiles
                .AsTracking()
                .Where(file => file.LibraryDirectoryId == work.LibraryDirectoryId &&
                               file.Sha256 == observation.Sha256 &&
                               file.Size == observation.Size &&
                               (file.Availability == VideoFileAvailability.Unreachable ||
                                file.Availability == VideoFileAvailability.Missing))
                .Take(2)
                .ToListAsync(cancellationToken);
            current = renameMatches.Count == 1 ? renameMatches[0] : null;
        }

        if (current is null)
        {
            var video = new VideoRow
            {
                Id = Guid.CreateVersion7(),
                DiscoveryDate = Now(),
            };
            current = new VideoFileRow
            {
                Id = Guid.CreateVersion7(),
                Video = video,
                LibraryDirectoryId = work.LibraryDirectoryId,
                RelativePath = candidate.RelativePath,
                Sha256 = observation.Sha256,
                PublicDeliveryId = Guid.NewGuid(),
                ContainerFormat = facts.ContainerFormat,
                VideoCodec = facts.VideoCodec,
            };
            database.Videos.Add(video);
            database.VideoFiles.Add(current);
        }

        current.RelativePath = candidate.RelativePath;
        current.Size = observation.Size;
        current.LastWriteTimeUtc = observation.LastWriteTimeUtc;
        current.Sha256 = observation.Sha256;
        current.ContainerFormat = facts.ContainerFormat;
        current.VideoCodec = facts.VideoCodec;
        current.AudioCodec = facts.AudioCodec;
        current.DurationMilliseconds = facts.DurationMilliseconds;
        current.Width = facts.Width;
        current.Height = facts.Height;
        current.DirectPlayClassification = DirectPlayClassificationRule.Classify(
            facts.ContainerFormat,
            facts.VideoCodec,
            facts.AudioCodec);
        current.Availability = VideoFileAvailability.Available;
        current.LastObservedScanId = candidate.LibraryScanId;
        current.ConsecutiveCompleteAbsences = 0;
        current.InspectedAt = Now();
        candidate.State = VideoFileCandidateState.Accepted;

        foreach (var cause in new[]
        {
            WorkIssueCause.SourceAccess,
            WorkIssueCause.ChangingSource,
            WorkIssueCause.InvalidContent,
        })
        {
            await issues.ResolveItemAsync(
                work.LibraryDirectoryId,
                BackgroundWorkCategory.TechnicalInspection,
                cause,
                candidate.RelativePath,
                "Technical inspection established audiovisual content from stable source data.",
                cancellationToken);
        }

        if (current.DirectPlayClassification == DirectPlayClassification.BaselineCandidate)
        {
            var configuration = await database.InstallationConfigurations
                .AsTracking()
                .SingleAsync(cancellationToken);
            configuration.FirstPlayableVideoReachedAt ??= Now();
        }
    }

    private async Task MarkReplacedIfDifferentAsync(
        BackgroundWorkRow work,
        VideoFileCandidateRow candidate,
        string sha256,
        CancellationToken cancellationToken)
    {
        var current = await database.VideoFiles
            .AsTracking()
            .Where(file => file.LibraryDirectoryId == work.LibraryDirectoryId &&
                           file.RelativePath == candidate.RelativePath &&
                           file.Availability != VideoFileAvailability.Replaced &&
                           file.Availability != VideoFileAvailability.Removed)
            .OrderByDescending(file => file.InspectedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (current is not null && current.Sha256 != sha256)
        {
            current.Availability = VideoFileAvailability.Replaced;
        }
    }

    private async Task RejectAsync(
        BackgroundWorkRow work,
        VideoFileCandidateRow candidate,
        string? path,
        WorkIssueReport report,
        CancellationToken cancellationToken)
    {
        candidate.State = VideoFileCandidateState.Rejected;
        await issues.RecordAsync(work, report with { ContainerPath = path }, cancellationToken);
    }

    /// <summary>
    /// A candidate that is still being written receives bounded backoff and no identity decision:
    /// stable content was never observed, so nothing may be concluded about a replacement.
    /// </summary>
    private static WorkIssueReport Changing(
        BackgroundWorkRow work,
        VideoFileCandidateRow candidate,
        string? path) =>
        new(path is null ? WorkIssueCause.SourceAccess : WorkIssueCause.ChangingSource,
            WorkIssueSeverity.ScopedIssue,
            WorkIssueRetryDisposition.AutomaticRetryScheduled,
            candidate.RelativePath,
            $"{work.LibraryDirectory.Name}:inspection",
            BackgroundWorkPhases.Inspecting,
            path is null
                ? WorkIssueMessages.CannotInspect(Path.GetFileName(candidate.RelativePath))
                : WorkIssueMessages.FileIsStillChanging(Path.GetFileName(candidate.RelativePath)),
            "Stable content could not be observed, so no replacement or identity decision was " +
            "made. No speculative Video was created and unrelated candidates continue to be " +
            "inspected.",
            "This candidate has not been admitted to the library yet.",
            "No action is required while the source is still being written; the next Library Scan " +
            "inspects it again.",
            path is null
                ? "The container path could not be resolved beneath the Library Directory."
                : "The observed size or modification time changed during inspection.",
            "A stable observation followed by successful technical inspection.");

    /// <summary>
    /// Recognisable content that cannot be parsed is a deterministic outcome, so it settles as a
    /// visible Scoped Issue without a time-based retry.
    /// </summary>
    private static WorkIssueReport Invalid(
        BackgroundWorkRow work,
        VideoFileCandidateRow candidate,
        string? path) =>
        new(WorkIssueCause.InvalidContent,
            WorkIssueSeverity.ScopedIssue,
            WorkIssueRetryDisposition.NoAutomaticRetry,
            candidate.RelativePath,
            $"{work.LibraryDirectory.Name}:inspection",
            BackgroundWorkPhases.Inspecting,
            WorkIssueMessages.CannotInspect(Path.GetFileName(candidate.RelativePath)),
            "Technical inspection did not establish audiovisual content, so no Video was created " +
            "for it. Independent files continue to be inspected.",
            "This candidate is not part of the library.",
            "Repair, replace, or externally remove the source file, then use Retry now.",
            "The media inspector reported no audiovisual stream.",
            "A successful technical inspection of the same path, or proof that the candidate no " +
            "longer applies.");

    /// <summary>
    /// Hands the admitted Video Files to the lanes that derive knowledge from them. Hashing and
    /// preview generation are independent of each other, and identification follows hashing so
    /// that content evidence is offered before a name is.
    /// </summary>
    private async Task QueueDerivedWorkAsync(
        BackgroundWorkRow work,
        CancellationToken cancellationToken)
    {
        var now = Now();

        foreach (var category in new[]
        {
            BackgroundWorkCategory.Hashing,
            BackgroundWorkCategory.PreviewGeneration,
        })
        {
            await DerivedWorkQueue.QueueAsync(
                database,
                work.LibraryDirectoryId,
                work.ConfigurationGeneration,
                category,
                BackgroundWorkTrigger.FollowUpWork,
                now,
                cancellationToken);
        }
    }

    private async Task FinishAsync(
        BackgroundWorkRow work,
        BackgroundWorkState state,
        CancellationToken cancellationToken)
    {
        work.State = state;
        work.Phase = BackgroundWorkPhases.Settled;
        work.UpdatedAt = Now();
        work.FinishedAt = work.UpdatedAt;
        await database.SaveChangesAsync(cancellationToken);
    }

    private static async Task<FileObservation> ObserveAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        var size = file.Length;
        var modified = file.LastWriteTimeUtc;
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            1024 * 128,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return new FileObservation(size, modified, Convert.ToHexString(hash));
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    private sealed record FileObservation(long Size, DateTime LastWriteTimeUtc, string Sha256);
}
