namespace Prdb.Viewer.Infrastructure.Configuration;

public sealed class LibraryDirectoryInspector(LibraryMountRoot mountRoot)
{
    public LibraryDirectoryInspection Inspect(string requestedPath)
    {
        // A path is pasted far more often than it is typed, so surrounding whitespace says
        // nothing about what the Administrator meant and is removed rather than rejected.
        var requested = requestedPath?.Trim() ?? string.Empty;

        if (requested.Length == 0 || !Path.IsPathFullyQualified(requested))
        {
            return new LibraryDirectoryInspection(LibraryDirectoryStageVerdict.InvalidPath);
        }

        string candidate;

        try
        {
            candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(requested));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new LibraryDirectoryInspection(LibraryDirectoryStageVerdict.InvalidPath);
        }

        var root = Path.TrimEndingDirectorySeparator(mountRoot.Path);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var relative = Path.GetRelativePath(root, candidate);

        if (relative == "." ||
            Path.IsPathFullyQualified(relative) ||
            relative == ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", comparison))
        {
            return new LibraryDirectoryInspection(LibraryDirectoryStageVerdict.OutsideMountArea);
        }

        if (!Directory.Exists(root) || !Directory.Exists(candidate))
        {
            return new LibraryDirectoryInspection(LibraryDirectoryStageVerdict.Missing);
        }

        try
        {
            var physicalRoot = ResolveDirectory(root);
            var physicalCandidate = physicalRoot;

            foreach (var segment in relative.Split(
                         Path.DirectorySeparatorChar,
                         StringSplitOptions.RemoveEmptyEntries))
            {
                physicalCandidate = ResolveDirectory(Path.Combine(physicalCandidate, segment));

                if (!IsWithin(physicalRoot, physicalCandidate, comparison))
                {
                    return new LibraryDirectoryInspection(LibraryDirectoryStageVerdict.OutsideMountArea);
                }
            }

            _ = Directory.EnumerateFileSystemEntries(candidate).Take(1).ToArray();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return new LibraryDirectoryInspection(LibraryDirectoryStageVerdict.Unreadable);
        }

        return new LibraryDirectoryInspection(LibraryDirectoryStageVerdict.Staged, candidate);
    }

    public IReadOnlyList<string> Discover()
    {
        if (!Directory.Exists(mountRoot.Path))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateDirectories(mountRoot.Path)
                .Select(path => Inspect(path))
                .Where(result => result.Verdict == LibraryDirectoryStageVerdict.Staged)
                .Select(result => result.ContainerPath!)
                .Order(StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    private static string ResolveDirectory(string path)
    {
        var directory = new DirectoryInfo(path);
        var target = directory.ResolveLinkTarget(returnFinalTarget: true);
        return Path.TrimEndingDirectorySeparator(target?.FullName ?? directory.FullName);
    }

    private static bool IsWithin(string root, string candidate, StringComparison comparison) =>
        candidate.Equals(root, comparison) ||
        candidate.StartsWith($"{root}{Path.DirectorySeparatorChar}", comparison);
}

public sealed record LibraryDirectoryInspection(
    LibraryDirectoryStageVerdict Verdict,
    string? ContainerPath = null);
