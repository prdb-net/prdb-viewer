using System.Reflection;

namespace Prdb.Viewer.Infrastructure;

/// <summary>
/// The version and commit this executable was built from. Diagnostics, Operator Handoffs, and the
/// Backup Archive envelope all name the same build, so a report can be traced to exact source
/// without anyone reading container logs.
/// </summary>
public static class ProductBuild
{
    public static string Version { get; } =
        typeof(ProductBuild).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";

    public static string Commit { get; } =
        typeof(ProductBuild).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "Commit")
            ?.Value ?? "unknown";

    public static string Description { get; } = $"{Version} ({Commit})";
}
