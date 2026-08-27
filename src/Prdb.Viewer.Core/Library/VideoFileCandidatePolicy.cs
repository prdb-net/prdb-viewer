namespace Prdb.Viewer.Core.Library;

public static class VideoFileCandidatePolicy
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".avi", ".m2ts", ".m4v", ".mkv", ".mov", ".mp4", ".mpeg", ".mpg", ".ts", ".webm", ".wmv",
    };

    public static bool Recognizes(string extension) => Extensions.Contains(extension);
}
