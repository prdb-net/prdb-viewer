using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// Generates the durable local preview image every Video carries, identified or not. Previews are
/// regenerable artefacts in the application's own data directory and never identity-bearing.
/// </summary>
public sealed class PreviewGenerationRunner(
    ViewerDbContext database,
    IPreviewImageGenerator generator,
    DerivedArtifactStore artifacts,
    TimeProvider timeProvider) : VideoFileWorkRunner(database, timeProvider)
{
    protected override BackgroundWorkCategory Category => BackgroundWorkCategory.PreviewGeneration;

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
        }
        else
        {
            AddIssue(
                work,
                file.RelativePath,
                path is null ? WorkIssueCause.SourceAccess : WorkIssueCause.InvalidContent,
                WorkIssueSeverity.ScopedIssue,
                RemediationOwner.Administrator,
                "No preview image could be generated, so this Video is presented without one.",
                "Check the file, then request another Library Scan to retry preview generation.");
        }

        work.CompletedItemCount++;
    }
}
