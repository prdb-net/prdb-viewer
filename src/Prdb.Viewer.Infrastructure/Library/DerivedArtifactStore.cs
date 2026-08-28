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

    public string PreviewsRoot { get; } = Path.Combine(location.DataDirectory, PreviewDirectoryName);

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
