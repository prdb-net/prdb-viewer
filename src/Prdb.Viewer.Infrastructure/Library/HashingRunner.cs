using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// Computes the OS hash and perceptual hash of admitted Video Files so that prdb can identify
/// them by content rather than by name. The lane reads source media and writes nothing back to it.
/// </summary>
public sealed class HashingRunner(
    ViewerDbContext database,
    IVideoFileHasher hasher,
    WorkIssueRecorder issues,
    TimeProvider timeProvider) : VideoFileWorkRunner(database, issues, timeProvider)
{
    protected override BackgroundWorkCategory Category => BackgroundWorkCategory.Hashing;

    protected override string Phase => BackgroundWorkPhases.Hashing;

    protected override IQueryable<VideoFileRow> Outstanding(Guid libraryDirectoryId) =>
        Database.VideoFiles.Where(file =>
            file.LibraryDirectoryId == libraryDirectoryId &&
            file.Availability == VideoFileAvailability.Available &&
            (file.HashedSha256 == null || file.HashedSha256 != file.Sha256));

    protected override Task RetryEarlierFailuresAsync(
        Guid libraryDirectoryId,
        CancellationToken cancellationToken) =>
        Database.VideoFiles
            .Where(file => file.LibraryDirectoryId == libraryDirectoryId &&
                           file.Availability == VideoFileAvailability.Available &&
                           file.HashState == VideoFileHashState.Failed)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(file => file.HashState, VideoFileHashState.Pending)
                    .SetProperty(file => file.HashedSha256, (string?)null),
                cancellationToken);

    protected override async Task AdvanceAsync(
        BackgroundWorkRow work,
        IReadOnlyList<VideoFileRow> files,
        CancellationToken cancellationToken)
    {
        var file = files[0];
        var tracked = await Database.VideoFiles
            .AsTracking()
            .SingleAsync(row => row.Id == file.Id, cancellationToken);
        var path = SourceFile.Resolve(work.LibraryDirectory.ContainerPath, file.RelativePath);
        var hashes = path is not null && IsStable(path, file)
            ? await hasher.ComputeAsync(path, cancellationToken)
            : new VideoFileHashes(null, null, "The Video File is not readable as inspected.");

        if (path is not null && !IsStable(path, file))
        {
            hashes = new VideoFileHashes(null, null, "The Video File changed while it was hashed.");
        }

        tracked.OsHash = hashes.OsHash;
        tracked.PerceptualHash = hashes.PerceptualHash;
        tracked.HashedSha256 = file.Sha256;
        tracked.HashedAt = Now();
        tracked.HashFailureReason = hashes.FailureReason;
        tracked.HashState = (hashes.OsHash, hashes.PerceptualHash) switch
        {
            (not null, not null) => VideoFileHashState.Computed,
            (null, null) => VideoFileHashState.Failed,
            _ => VideoFileHashState.Incomplete,
        };

        if (tracked.HashState == VideoFileHashState.Failed)
        {
            var changing = path is not null && !IsStable(path, file);
            var cause = path is null
                ? WorkIssueCause.SourceAccess
                : changing
                    ? WorkIssueCause.ChangingSource
                    : WorkIssueCause.InvalidContent;
            await ReportAsync(
                work,
                new WorkIssueReport(
                    cause,
                    WorkIssueSeverity.ScopedIssue,
                    changing
                        ? WorkIssueRetryDisposition.AutomaticRetryScheduled
                        : WorkIssueRetryDisposition.NoAutomaticRetry,
                    file.RelativePath,
                    work.LibraryDirectory.Name,
                    Phase,
                    changing
                        ? WorkIssueMessages.FileIsStillChanging(Path.GetFileName(file.RelativePath))
                        : WorkIssueMessages.CannotHash(Path.GetFileName(file.RelativePath)),
                    changing
                        ? "Stable content could not be observed, so no replacement or identity " +
                          "decision was made. Browsing and playback of this Video File continue."
                        : "The identification hashes could not be calculated from the inspected " +
                          "content. Browsing and otherwise available playback remain possible, " +
                          "while automatic identification for this file is delayed. Unrelated " +
                          "files continue to be hashed.",
                    "This Video File can currently only be identified by its name.",
                    changing
                        ? "No action is required while the source is still being written."
                        : "Repair or replace the source file, then use Retry now.",
                    hashes.FailureReason ?? "The hashing library returned no usable result.",
                    "A stable read followed by a successfully computed identification hash.")
                {
                    ContainerPath = path,
                    VideoId = file.VideoId,
                    VideoFileId = file.Id,
                },
                cancellationToken);
        }
        else
        {
            foreach (var cause in new[]
            {
                WorkIssueCause.SourceAccess,
                WorkIssueCause.ChangingSource,
                WorkIssueCause.InvalidContent,
            })
            {
                await ResolveItemAsync(
                    work,
                    cause,
                    file.RelativePath,
                    "An identification hash was computed from stable content.",
                    cancellationToken);
            }
        }

        work.CompletedItemCount++;
    }

    protected override Task CompleteAsync(
        BackgroundWorkRow work,
        CancellationToken cancellationToken) =>
        DerivedWorkQueue.QueueAsync(
            Database,
            work.LibraryDirectoryId,
            work.ConfigurationGeneration,
            BackgroundWorkCategory.Identification,
            BackgroundWorkTrigger.FollowUpWork,
            Now(),
            cancellationToken);
}
