using System.Text.RegularExpressions;
using System.Xml.Linq;

using Xunit;

namespace Prdb.Viewer.Core.Tests;

public sealed partial class ArchitectureTests
{
    [Fact]
    public void Production_projects_follow_the_declared_dependency_direction()
    {
        var core = Project("src/Prdb.Viewer.Core/Prdb.Viewer.Core.csproj");
        var infrastructure = Project("src/Prdb.Viewer.Infrastructure/Prdb.Viewer.Infrastructure.csproj");
        var host = Project("src/Prdb.Viewer.Host/Prdb.Viewer.Host.csproj");

        Assert.Empty(References(core, "PackageReference"));
        Assert.Empty(References(core, "ProjectReference"));
        Assert.Equal(
            ["../Prdb.Viewer.Core/Prdb.Viewer.Core.csproj"],
            References(infrastructure, "ProjectReference"));
        Assert.Equal(
            [
                "../Prdb.Viewer.Core/Prdb.Viewer.Core.csproj",
                "../Prdb.Viewer.Infrastructure/Prdb.Viewer.Infrastructure.csproj",
            ],
            References(host, "ProjectReference"));

        foreach (var project in SourceProjectsExcept("Prdb.Viewer.Host.csproj"))
        {
            Assert.DoesNotContain(
                References(XDocument.Load(project.FullName), "ProjectReference"),
                reference => reference.EndsWith("Prdb.Viewer.Host.csproj", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Core_reaches_no_io()
    {
        foreach (var file in SourceFilesUnder("src/Prdb.Viewer.Core"))
        {
            var code = CodeIn(file);

            Assert.DoesNotMatch(IoCall(), code);
            Assert.DoesNotContain("Microsoft.EntityFrameworkCore", code, StringComparison.Ordinal);
            Assert.DoesNotContain("System.Net", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Production_code_reads_no_clock_directly()
    {
        foreach (var file in SourceFilesUnder("src"))
        {
            Assert.DoesNotMatch(ClockCall(), CodeIn(file));
        }
    }

    private static string CodeIn(FileInfo file) =>
        Comment().Replace(File.ReadAllText(file.FullName), string.Empty);

    [GeneratedRegex(@"//[^\n]*|/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex(@"\b(File|Directory|FileInfo|DirectoryInfo|FileStream|DriveInfo)\.")]
    private static partial Regex IoCall();

    [GeneratedRegex(@"\b(DateTime|DateTimeOffset)\.(Now|UtcNow|Today)\b")]
    private static partial Regex ClockCall();

    private static string[] References(XDocument project, string kind) =>
        project.Descendants(kind)
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => include!.Replace('\\', '/'))
            .ToArray();

    private static XDocument Project(string relativePath) =>
        XDocument.Load(Path.Combine(RepositoryRoot().FullName, relativePath));

    private static IEnumerable<FileInfo> SourceProjectsExcept(string fileName) =>
        new DirectoryInfo(Path.Combine(RepositoryRoot().FullName, "src"))
            .EnumerateFiles("*.csproj", SearchOption.AllDirectories)
            .Where(project => project.Name != fileName);

    private static IEnumerable<FileInfo> SourceFilesUnder(string relativePath) =>
        new DirectoryInfo(Path.Combine(RepositoryRoot().FullName, relativePath))
            .EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Where(file => !file.FullName.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(file => !file.FullName.Contains(
                $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(file => !file.FullName.Contains(
                $"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal));

    private static DirectoryInfo RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "Prdb.Viewer.slnx")))
        {
            directory = directory.Parent;
        }

        return directory ?? throw new InvalidOperationException(
            $"No repository root above {AppContext.BaseDirectory}.");
    }
}
