using System.Security.Cryptography;

using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

public sealed class TechnicalInspectionRunner(
    ViewerDbContext database,
    IMediaProbe mediaProbe,
    TimeProvider timeProvider)
{
    public async Task<bool> RunNextSliceAsync(CancellationToken cancellationToken = default)
    {
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

        if (work.LibraryDirectory.State != LibraryDirectoryState.Active ||
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
            await FinishAsync(
                work,
                work.IssueCount == 0
                    ? BackgroundWorkState.Completed
                    : BackgroundWorkState.CompletedWithIssues,
                cancellationToken);
            return true;
        }

        var now = Now();
        work.State = BackgroundWorkState.Running;
        work.StartedAt ??= now;
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
            Reject(work, candidate, WorkIssueCause.ChangingSource,
                "The Video File Candidate changed or became unreadable during inspection.",
                RemediationOwner.AutomaticRecovery,
                "Request another Library Scan after the source has stabilised.");
        }
        else if (facts is null)
        {
            await MarkReplacedIfDifferentAsync(work, candidate, observation.Sha256, cancellationToken);
            Reject(work, candidate, WorkIssueCause.InvalidContent,
                "Technical inspection did not establish audiovisual content.",
                RemediationOwner.Administrator,
                "Review the file and remove or replace invalid content if appropriate.");
        }
        else
        {
            await AcceptAsync(work, candidate, observation, facts, cancellationToken);
        }

        work.CompletedItemCount++;
        work.UpdatedAt = Now();
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

    private void Reject(
        BackgroundWorkRow work,
        VideoFileCandidateRow candidate,
        WorkIssueCause cause,
        string impact,
        RemediationOwner owner,
        string requiredAction)
    {
        candidate.State = VideoFileCandidateState.Rejected;
        work.IssueCount++;
        database.WorkIssues.Add(new WorkIssueRow
        {
            Id = Guid.CreateVersion7(),
            BackgroundWorkId = work.Id,
            Severity = WorkIssueSeverity.ScopedIssue,
            Cause = cause,
            RemediationOwner = owner,
            AffectedScope = candidate.RelativePath,
            Impact = impact,
            RequiredAction = requiredAction,
            CreatedAt = Now(),
        });
    }

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
