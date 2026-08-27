namespace Prdb.Viewer.Infrastructure.Configuration;

public sealed class LibraryMountRoot
{
    public const string DefaultPath = "/libraries";

    public LibraryMountRoot(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
    }

    public string Path { get; }
}
