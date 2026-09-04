using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// The application-owned location of regenerable artefacts. Previews live beside the database in
/// the application data directory and never beneath a Library Directory, so source media stays
/// untouched and a lost artefact can simply be generated again.
/// </summary>
/// <summary>
/// Whether application storage currently accepts a durable write, and the safe cause when it does
/// not.
/// </summary>
public sealed record StorageWriteCheck(bool Succeeded, string? SafeCause);

public sealed class DerivedArtifactStore(ViewerDatabaseLocation location)
{
    private const string PreviewDirectoryName = "previews";

    private const string ArtworkDirectoryName = "artwork";

    private const string ActorDirectoryName = "actors";

    public string PreviewsRoot { get; } = Path.Combine(location.DataDirectory, PreviewDirectoryName);

    /// <summary>
    /// Where the pictures prdb offers for proposed works are held. They are retained rather than
    /// generated, but they are regenerable all the same: a lost file is asked for again, and the
    /// Administrator's browser is never sent to prdb for one.
    /// </summary>
    public string ArtworkRoot { get; } = Path.Combine(location.DataDirectory, ArtworkDirectoryName);

    /// <summary>
    /// The stored, directory-relative location of a Video File's preview, sharded so that one
    /// directory never holds an unreasonable number of entries.
    /// </summary>
    public static string PreviewRelativePath(Guid videoFileId)
    {
        var name = videoFileId.ToString("n");

        return $"{name[..2]}/{name}.jpg";
    }

    public string PreviewFullPath(string relativePath) =>
        Path.Combine(PreviewsRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public void EnsurePreviewDirectory(string relativePath) =>
        Directory.CreateDirectory(Path.GetDirectoryName(PreviewFullPath(relativePath))!);

    /// <summary>
    /// The stored location of one proposed work's picture, sharded like a preview. The extension
    /// follows what arrived rather than what was hoped for: prdb offers JPEG, PNG and WebP, and a
    /// file named for the wrong one is a lie on disk.
    /// </summary>
    public static string ArtworkRelativePath(Guid proposedWorkId, string contentType)
    {
        var name = proposedWorkId.ToString("n");

        return $"{name[..2]}/{name}{ArtworkExtension(contentType)}";
    }

    private static string ArtworkExtension(string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        "image/avif" => ".avif",
        "image/bmp" => ".bmp",
        _ => ".jpg",
    };

    /// <summary>
    /// Where the pictures prdb offers for Actors are held. Retained rather than generated, and
    /// regenerable all the same: a lost file is asked for again, and a User's browser is never
    /// sent to prdb for one.
    /// </summary>
    public string ActorImagesRoot { get; } = Path.Combine(location.DataDirectory, ActorDirectoryName);

    /// <summary>
    /// The stored location of one Actor Image, sharded like a preview, with the extension
    /// following what arrived rather than what was hoped for.
    /// </summary>
    public static string ActorImageRelativePath(Guid imageId, string contentType)
    {
        var name = imageId.ToString("n");

        return $"{name[..2]}/{name}{ArtworkExtension(contentType)}";
    }

    public string ActorImageFullPath(string relativePath) =>
        Path.Combine(ActorImagesRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public void EnsureActorImageDirectory(string relativePath) =>
        Directory.CreateDirectory(Path.GetDirectoryName(ActorImageFullPath(relativePath))!);

    public string ArtworkFullPath(string relativePath) =>
        Path.Combine(ArtworkRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public void EnsureArtworkDirectory(string relativePath) =>
        Directory.CreateDirectory(Path.GetDirectoryName(ArtworkFullPath(relativePath))!);

    /// <summary>
    /// Writes and synchronises a probe file, which is the observation the Capacity rules require
    /// before an Administrator may consider unwritable application storage repaired. It never
    /// probes in a loop; a lane asks once and stops until someone requests another check.
    /// </summary>
    public StorageWriteCheck CheckWritable()
    {
        var probe = Path.Combine(PreviewsRoot, ".write-check");

        try
        {
            Directory.CreateDirectory(PreviewsRoot);

            using (var stream = new FileStream(
                probe,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                stream.Write([0x70, 0x72, 0x64, 0x62]);
                stream.Flush(flushToDisk: true);
            }

            File.Delete(probe);
            return new StorageWriteCheck(true, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new StorageWriteCheck(false, SafeCause(exception));
        }
    }

    /// <summary>
    /// The failure class an operator may see. It names the condition without exposing a stack
    /// trace or more of the host filesystem than the container path already discloses.
    /// </summary>
    private static string SafeCause(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "The application data directory is not writable.",
        IOException io when io.Message.Contains("space", StringComparison.OrdinalIgnoreCase) =>
            "The application data directory has no free space.",
        _ => "The application data directory refused a write.",
    };
}
