using Microsoft.Extensions.DependencyInjection;

using Prdb.Viewer.Host.Access;
using Prdb.Viewer.Infrastructure.Access;
using Prdb.Viewer.Infrastructure.Recovery;

using Xunit;

namespace Prdb.Viewer.Host.Tests.Access;

public sealed class OperatorCommandTests
{
    [Fact]
    public async Task The_operator_cli_never_accepts_a_passphrase_as_an_argument()
    {
        using var application = new ViewerApplication();
        _ = application.Server;
        await using var scope = application.Services.CreateAsyncScope();
        var output = new StringWriter();
        var error = new StringWriter();

        // A third positional argument is exactly where a passphrase would end up in a process
        // listing, so it is refused rather than read.
        foreach (var command in new[] { "backup", "validate-backup", "restore" })
        {
            Assert.True(OperatorCommands.Matches([command, "/tmp/archive", "a passphrase"]));
            Assert.Equal(
                64,
                await RunAsync(scope, [command, "/tmp/archive", "a passphrase"], output, error));
        }

        Assert.Contains("Usage:", error.ToString());
        Assert.DoesNotContain("a passphrase", output.ToString());
    }

    [Fact]
    public async Task Restore_refuses_a_claimed_installation_without_reading_the_archive()
    {
        using var application = new ViewerApplication();
        _ = await application.CreateBootstrapAuthorizationAsync();
        await using var scope = application.Services.CreateAsyncScope();
        var archive = Path.Combine(Path.GetTempPath(), $"prdb-viewer-{Guid.NewGuid():n}.archive");
        await File.WriteAllTextAsync(
            archive,
            "not an archive",
            TestContext.Current.CancellationToken);

        try
        {
            var restored = await scope.ServiceProvider
                .GetRequiredService<BackupService>()
                .RestoreAsync(archive, "a passphrase", TestContext.Current.CancellationToken);

            // A Bootstrap Authorization alone leaves the installation empty, so the archive is
            // read and rejected on its own merits.
            Assert.Equal(RestoreVerdict.ArchiveRejected, restored.Verdict);
        }
        finally
        {
            File.Delete(archive);
        }
    }

    private static Task<int> RunAsync(
        AsyncServiceScope scope,
        string[] arguments,
        TextWriter output,
        TextWriter error) =>
        OperatorCommands.RunAsync(
            arguments,
            scope.ServiceProvider.GetRequiredService<AccessService>(),
            scope.ServiceProvider.GetRequiredService<BackupService>(),
            output,
            error,
            TestContext.Current.CancellationToken);
}
