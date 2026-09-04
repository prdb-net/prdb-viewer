using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

public sealed record PreviewDelivery(Stream Content, string ContentType, DateTimeOffset LastModified);

/// <summary>
/// Serves the pictures this installation holds: the previews it generated from the files it has,
/// and the pictures prdb offers for works, proposals and Actors. Each is addressed by a random,
/// non-enumerable identifier rather than by a path or a database key, and each is served from this
/// installation's own origin so that a browser is never sent to prdb for one.
/// </summary>
public sealed class PreviewDeliveryService(
    ViewerDbContext database,
    DerivedArtifactStore artifacts)
{
    /// <summary>What one retained picture is, once its row has been found.</summary>
    private sealed record RetainedFile(string RelativePath, string? ContentType, DateTime? Written);

    public async Task<PreviewDelivery?> OpenAsync(
        Guid publicPreviewId,
        CancellationToken cancellationToken = default) =>
        Open(
            await database.VideoFiles
                .AsNoTracking()
                .Where(file => file.PublicPreviewId == publicPreviewId &&
                               file.PreviewState == VideoFilePreviewState.Generated &&
                               file.PreviewRelativePath != null)
                .Select(file => new RetainedFile(
                    file.PreviewRelativePath!,
                    // A preview is this installation's own JPEG rather than bytes that arrived.
                    null,
                    file.PreviewGeneratedAt))
                .SingleOrDefaultAsync(cancellationToken),
            artifacts.PreviewsRoot,
            artifacts.PreviewFullPath);

    /// <summary>
    /// Serves one retained Actor Image. Addressed the same way a preview is, by a random
    /// identifier rather than a remote URL, so opening an Actor's page never puts a User's browser
    /// in touch with prdb.
    /// </summary>
    public async Task<PreviewDelivery?> OpenActorImageAsync(
        Guid publicImageId,
        CancellationToken cancellationToken = default) =>
        Open(
            await database.ActorImages
                .AsNoTracking()
                .Where(row => row.PublicImageId == publicImageId &&
                              row.State == ActorImageState.Retained &&
                              row.RelativePath != null)
                .Select(row => new RetainedFile(row.RelativePath!, row.ContentType, row.RetainedAt))
                .SingleOrDefaultAsync(cancellationToken),
            artifacts.ActorImagesRoot,
            artifacts.ActorImageFullPath);

    /// <summary>
    /// Serves one retained picture of an Established Work — prdb's picture of it, as distinct from
    /// the preview this installation generated from the file it holds.
    /// </summary>
    public async Task<PreviewDelivery?> OpenWorkImageAsync(
        Guid publicImageId,
        CancellationToken cancellationToken = default) =>
        Open(
            await database.VideoImages
                .AsNoTracking()
                .Where(row => row.PublicImageId == publicImageId &&
                              row.State == ActorImageState.Retained &&
                              row.RelativePath != null)
                .Select(row => new RetainedFile(row.RelativePath!, row.ContentType, row.RetainedAt))
                .SingleOrDefaultAsync(cancellationToken),
            artifacts.WorkImagesRoot,
            artifacts.WorkImageFullPath);

    /// <summary>
    /// Serves the retained picture of a proposed work. It is addressed the same way a preview is,
    /// by a random identifier rather than a remote URL, so that opening a review case never puts
    /// an Administrator's browser in touch with prdb.
    /// </summary>
    public async Task<PreviewDelivery?> OpenProposedWorkArtworkAsync(
        Guid publicArtworkId,
        CancellationToken cancellationToken = default) =>
        Open(
            await database.ProposedWorks
                .AsNoTracking()
                .Where(work => work.PublicArtworkId == publicArtworkId &&
                               work.ArtworkState == ProposedWorkArtworkState.Retained &&
                               work.ArtworkRelativePath != null)
                .Select(work => new RetainedFile(
                    work.ArtworkRelativePath!,
                    work.ArtworkContentType,
                    work.ArtworkRetainedAt))
                .SingleOrDefaultAsync(cancellationToken),
            artifacts.ArtworkRoot,
            artifacts.ArtworkFullPath);

    /// <summary>
    /// Opens one retained picture, whatever kind it is.
    ///
    /// The containment check lives here rather than at each caller: every stored path is
    /// application-generated, and re-checking that the resolved file stays beneath the directory it
    /// belongs to is the kind of proof that is worth having in exactly one place, where a new kind
    /// of picture inherits it rather than having to remember it.
    /// </summary>
    private static PreviewDelivery? Open(
        RetainedFile? file,
        string root,
        Func<string, string> fullPath)
    {
        if (file is null)
        {
            return null;
        }

        var path = Path.GetFullPath(fullPath(file.RelativePath));
        var contained = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

        if (!path.StartsWith($"{contained}{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return new PreviewDelivery(
                new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    1024 * 32,
                    FileOptions.Asynchronous | FileOptions.SequentialScan),
                file.ContentType ?? "image/jpeg",
                VideoPresentation.AsOffset(file.Written) ?? DateTimeOffset.MinValue);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
