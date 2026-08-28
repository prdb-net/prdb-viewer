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
    TimeProvider timeProvider) : VideoFileWorkRunner(database, timeProvider)
{
    protected override BackgroundWorkCategory Category => BackgroundWorkCategory.Hashing;

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
            AddIssue(
                work,
                file.RelativePath,
                path is null ? WorkIssueCause.SourceAccess : WorkIssueCause.InvalidContent,
                WorkIssueSeverity.ScopedIssue,
                RemediationOwner.Administrator,
                $"No content hash could be computed, so this Video File can only be identified by " +
                $"its name. {hashes.FailureReason}",
                "Check the file, then request another Library Scan to retry hashing.");
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
            Now(),
            cancellationToken);
}
