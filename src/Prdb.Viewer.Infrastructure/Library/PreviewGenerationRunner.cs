using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// Generates the durable local preview image every Video carries, identified or not. Previews are
/// regenerable artefacts in the application's own data directory and never identity-bearing, so a
/// failure costs a card image and never playback.
/// </summary>
public sealed class PreviewGenerationRunner(
    ViewerDbContext database,
    IPreviewImageGenerator generator,
    DerivedArtifactStore artifacts,
    WorkIssueRecorder issues,
    TimeProvider timeProvider) : VideoFileWorkRunner(database, issues, timeProvider)
{
    protected override BackgroundWorkCategory Category => BackgroundWorkCategory.PreviewGeneration;

    protected override string Phase => BackgroundWorkPhases.GeneratingPreviews;

    protected override IQueryable<VideoFileRow> Outstanding(Guid libraryDirectoryId) =>
        Database.VideoFiles.Where(file =>
            file.LibraryDirectoryId == libraryDirectoryId &&
            file.Availability == VideoFileAvailability.Available &&
            (file.PreviewSha256 == null || file.PreviewSha256 != file.Sha256));

    protected override Task RetryEarlierFailuresAsync(
        Guid libraryDirectoryId,
        CancellationToken cancellationToken) =>
        Database.VideoFiles
            .Where(file => file.LibraryDirectoryId == libraryDirectoryId &&
                           file.Availability == VideoFileAvailability.Available &&
                           file.PreviewState == VideoFilePreviewState.Failed)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(file => file.PreviewState, VideoFilePreviewState.Pending)
                    .SetProperty(file => file.PreviewSha256, (string?)null),
                cancellationToken);

    protected override async Task AdvanceAsync(
        BackgroundWorkRow work,
        IReadOnlyList<VideoFileRow> files,
        CancellationToken cancellationToken)
    {
        var file = files[0];
        var storage = artifacts.CheckWritable();

        if (!storage.Succeeded)
        {
            await StopForCapacityAsync(work, storage, cancellationToken);
            return;
        }

        var tracked = await Database.VideoFiles
            .AsTracking()
            .SingleAsync(row => row.Id == file.Id, cancellationToken);
        var relativePath = DerivedArtifactStore.PreviewRelativePath(file.Id);
        var path = SourceFile.Resolve(work.LibraryDirectory.ContainerPath, file.RelativePath);
        var sample = PreviewFrameRule.SampleSeconds(file.DurationMilliseconds);
        var generated = false;

        if (path is not null && sample is not null && IsStable(path, file))
        {
            artifacts.EnsurePreviewDirectory(relativePath);
            generated = await generator.TryGenerateAsync(
                path,
                sample.Value,
                PreviewFrameRule.PreviewWidth,
                artifacts.PreviewFullPath(relativePath),
                cancellationToken);
        }

        tracked.PreviewSha256 = file.Sha256;
        tracked.PreviewGeneratedAt = Now();
        tracked.PreviewState = generated
            ? VideoFilePreviewState.Generated
            : VideoFilePreviewState.Failed;

        if (generated)
        {
            tracked.PreviewRelativePath = relativePath;
            tracked.PublicPreviewId ??= Guid.NewGuid();
            await ResolveItemAsync(
                work,
                WorkIssueCause.InvalidContent,
                file.RelativePath,
                "A preview image was generated from this Video File.",
                cancellationToken);
            await ResolveItemAsync(
                work,
                WorkIssueCause.SourceAccess,
                file.RelativePath,
                "A preview image was generated from this Video File.",
                cancellationToken);
        }
        else
        {
            await ReportFailureAsync(work, file, path, cancellationToken);
        }

        await ResolveAsync(
            work,
            WorkIssueCause.Capacity,
            "Application storage accepted a written and synchronised preview.",
            cancellationToken);
        work.CompletedItemCount++;
    }

    /// <summary>
    /// Reports a failed preview only when the Video has no other Available Video File that already
    /// carries one. A successful sibling settles the attempt, because the Video is presented with
    /// its image and nothing is left for an Administrator to act on.
    /// </summary>
    private async Task ReportFailureAsync(
        BackgroundWorkRow work,
        VideoFileRow file,
        string? path,
        CancellationToken cancellationToken)
    {
        var covered = await Database.VideoFiles.AnyAsync(
            sibling => sibling.VideoId == file.VideoId &&
                       sibling.Id != file.Id &&
                       sibling.Availability == VideoFileAvailability.Available &&
                       sibling.PreviewState == VideoFilePreviewState.Generated,
            cancellationToken);

        if (covered)
        {
            return;
        }

        await ReportAsync(
            work,
            new WorkIssueReport(
                path is null ? WorkIssueCause.SourceAccess : WorkIssueCause.InvalidContent,
                WorkIssueSeverity.ScopedIssue,
                WorkIssueRetryDisposition.NoAutomaticRetry,
                file.RelativePath,
                work.LibraryDirectory.Name,
                Phase,
                WorkIssueMessages.PreviewFailed(Path.GetFileName(file.RelativePath)),
                path is null
                    ? "The Video File could not be read where the library expects it, so no frame " +
                      "could be sampled. Normal presentation uses a neutral placeholder and " +
                      "playback remains independent of the preview."
                    : "No frame could be decoded from the inspected content. Normal presentation " +
                      "uses a neutral placeholder, playback remains independent of the preview, " +
                      "and other Videos continue to receive theirs.",
                "This Video is presented with a neutral placeholder instead of a preview image.",
                path is null
                    ? "Restore the mount or permissions, then use Check again."
                    : "Repair or replace the source file, then use Retry now.",
                path is null
                    ? "The container path could not be resolved beneath the Library Directory."
                    : "No decodable frame was produced at the sampled position.",
                "A generated preview image for this Video, or for another Available Video File of " +
                "the same Video.")
            {
                ContainerPath = path,
                VideoId = file.VideoId,
                VideoFileId = file.Id,
            },
            cancellationToken);
    }

    /// <summary>
    /// Unwritable application storage is a Safety Stop: the lane stops rather than probing an
    /// unsafe destination in a loop, and everything already committed stays safe.
    /// </summary>
    private async Task StopForCapacityAsync(
        BackgroundWorkRow work,
        StorageWriteCheck storage,
        CancellationToken cancellationToken)
    {
        await ReportAsync(
            work,
            new WorkIssueReport(
                WorkIssueCause.Capacity,
                WorkIssueSeverity.SafetyStop,
                WorkIssueRetryDisposition.NoAutomaticRetry,
                artifacts.PreviewsRoot,
                artifacts.PreviewsRoot,
                Phase,
                WorkIssueMessages.StorageCannotAcceptData(),
                "New preview files stopped being written. Everything already committed remains " +
                "safe, browsing and playback continue, and no automatic write loop probes the " +
                "destination again.",
                "No further preview images are produced until application storage accepts writes.",
                "Ask the Installation Operator to free or repair the application data volume, " +
                "then use Check again.",
                storage.SafeCause ?? "The application data directory refused a write.",
                "A successful write-and-synchronisation check followed by a generated preview.")
            {
                ContainerPath = artifacts.PreviewsRoot,
                AggregatesItems = false,
            },
            cancellationToken);
        await HoldAsync(
            work,
            "Application storage must accept writes again before previews can be generated.",
            cancellationToken);
    }
}
