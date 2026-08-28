using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// The application-owned location of regenerable artefacts. Previews live beside the database in
/// the application data directory and never beneath a Library Directory, so source media stays
/// untouched and a lost artefact can simply be generated again.
/// </summary>
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
}
