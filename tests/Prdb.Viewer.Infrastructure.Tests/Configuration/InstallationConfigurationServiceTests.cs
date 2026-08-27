using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Infrastructure.Access;
using Prdb.Viewer.Infrastructure.Configuration;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Configuration;

public sealed class InstallationConfigurationServiceTests
{
    [Fact]
    public async Task Verified_connection_and_validated_directory_are_staged_before_activation()
    {
        var verifier = new StubPrdbConnectionVerifier(PrdbVerificationOutcome.Verified);
        await using var database = await TestDatabase.CreateAsync(prdbConnectionVerifier: verifier);
        await using var scope = database.Scope();
        var access = scope.ServiceProvider.GetRequiredService<AccessService>();
        await ClaimAdministratorAsync(access);
        var configuration = scope.ServiceProvider.GetRequiredService<InstallationConfigurationService>();

        var initial = await configuration.GetAsync(TestContext.Current.CancellationToken);
        Assert.Equal(InstallationConfigurationStatus.ConfigurationRequired, initial.Status);
        Assert.Equal(PrdbConnectionStatus.Missing, initial.PrdbConnectionStatus);

        Assert.Equal(
            PrdbConnectionUpdateVerdict.Verified,
            (await configuration.VerifyCredentialAsync(
                "test-api-key",
                TestContext.Current.CancellationToken)).Verdict);
        Assert.Equal("test-api-key", Assert.Single(verifier.Credentials));

        Assert.Equal(
            LibraryDirectoryStageVerdict.OutsideMountArea,
            (await configuration.StageLibraryDirectoryAsync(
                "Outside",
                database.Directory,
                TestContext.Current.CancellationToken)).Verdict);

        var library = Path.Combine(database.LibraryMountRoot.Path, "main");
        Directory.CreateDirectory(library);
        var marker = Path.Combine(library, "source-marker.txt");
        await File.WriteAllTextAsync(
            marker,
            "source media",
            TestContext.Current.CancellationToken);
        var before = Directory.GetFileSystemEntries(library);

        var staged = await configuration.StageLibraryDirectoryAsync(
            "Main Library",
            library,
            TestContext.Current.CancellationToken);
        Assert.Equal(LibraryDirectoryStageVerdict.Staged, staged.Verdict);

        var pending = await configuration.GetAsync(TestContext.Current.CancellationToken);
        Assert.Equal(InstallationConfigurationStatus.ConfigurationPending, pending.Status);
        Assert.Empty(pending.LibraryDirectories);

        var activated = await configuration.ActivateLibraryDirectoryAsync(
            staged.StageId!.Value,
            TestContext.Current.CancellationToken);
        Assert.Equal(LibraryDirectoryActivationVerdict.Activated, activated.Verdict);
        Assert.Equal(library, activated.Directory!.ContainerPath);

        var current = await configuration.GetAsync(TestContext.Current.CancellationToken);
        Assert.Equal(PrdbConnectionStatus.Verified, current.PrdbConnectionStatus);
        Assert.True(current.HasPrdbCredential);
        Assert.False(current.CredentialReplacementPending);
        Assert.Equal(InstallationConfigurationStatus.Configured, current.Status);
        Assert.Single(current.LibraryDirectories);
        Assert.Equal(before, Directory.GetFileSystemEntries(library));
        Assert.Equal("source media", await File.ReadAllTextAsync(
            marker,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Unavailable_connection_stays_pending_and_rejected_replacement_keeps_verified_credential()
    {
        var verifier = new StubPrdbConnectionVerifier(
            PrdbVerificationOutcome.Unavailable,
            PrdbVerificationOutcome.Verified,
            PrdbVerificationOutcome.Rejected);
        await using var database = await TestDatabase.CreateAsync(prdbConnectionVerifier: verifier);
        await using var scope = database.Scope();
        var configuration = scope.ServiceProvider.GetRequiredService<InstallationConfigurationService>();

        Assert.Equal(
            PrdbConnectionUpdateVerdict.VerificationPending,
            (await configuration.VerifyCredentialAsync(
                "first-api-key",
                TestContext.Current.CancellationToken)).Verdict);
        var pending = await configuration.GetAsync(TestContext.Current.CancellationToken);
        Assert.Equal(PrdbConnectionStatus.VerificationPending, pending.PrdbConnectionStatus);
        Assert.True(pending.CredentialReplacementPending);

        Assert.Equal(
            PrdbConnectionUpdateVerdict.Verified,
            (await configuration.RetryCredentialAsync(TestContext.Current.CancellationToken)).Verdict);
        Assert.Equal(
            PrdbConnectionUpdateVerdict.Rejected,
            (await configuration.VerifyCredentialAsync(
                "replacement-api-key",
                TestContext.Current.CancellationToken)).Verdict);

        var afterRejection = await configuration.GetAsync(TestContext.Current.CancellationToken);
        Assert.Equal(PrdbConnectionStatus.Verified, afterRejection.PrdbConnectionStatus);
        Assert.True(afterRejection.HasPrdbCredential);
        Assert.False(afterRejection.CredentialReplacementPending);
        Assert.Equal(PrdbConnectionIssue.ReplacementRejected, afterRejection.LastConnectionIssue);
    }

    [Fact]
    public async Task Expired_or_retargeted_directory_stage_cannot_be_activated()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 27, 20, 0, 0, TimeSpan.Zero));
        await using var database = await TestDatabase.CreateAsync(
            time,
            new StubPrdbConnectionVerifier(PrdbVerificationOutcome.Verified));
        await using var scope = database.Scope();
        var configuration = scope.ServiceProvider.GetRequiredService<InstallationConfigurationService>();
        var library = Path.Combine(database.LibraryMountRoot.Path, "expiring");
        Directory.CreateDirectory(library);

        var expiring = await configuration.StageLibraryDirectoryAsync(
            "Expiring",
            library,
            TestContext.Current.CancellationToken);
        time.Advance(ConfigurationStageLifetimes.LibraryDirectory + TimeSpan.FromSeconds(1));
        Assert.Equal(
            LibraryDirectoryActivationVerdict.Expired,
            (await configuration.ActivateLibraryDirectoryAsync(
                expiring.StageId!.Value,
                TestContext.Current.CancellationToken)).Verdict);

        var retargeted = await configuration.StageLibraryDirectoryAsync(
            "Retargeted",
            library,
            TestContext.Current.CancellationToken);
        Directory.Delete(library);
        Assert.Equal(
            LibraryDirectoryActivationVerdict.NoLongerValid,
            (await configuration.ActivateLibraryDirectoryAsync(
                retargeted.StageId!.Value,
                TestContext.Current.CancellationToken)).Verdict);
    }

    [Fact]
    public async Task Symbolic_link_cannot_escape_the_documented_mount_area()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        await using var database = await TestDatabase.CreateAsync();
        await using var scope = database.Scope();
        var outside = Path.Combine(database.Directory, "outside-library-root");
        Directory.CreateDirectory(outside);
        var link = Path.Combine(database.LibraryMountRoot.Path, "escape");
        Directory.CreateSymbolicLink(link, outside);
        var inspector = scope.ServiceProvider.GetRequiredService<LibraryDirectoryInspector>();

        Assert.Equal(
            LibraryDirectoryStageVerdict.OutsideMountArea,
            inspector.Inspect(link).Verdict);
    }

    [Fact]
    public async Task Superseded_connection_verification_cannot_replace_the_newer_credential()
    {
        var verifier = new CoordinatedPrdbConnectionVerifier();
        await using var database = await TestDatabase.CreateAsync(prdbConnectionVerifier: verifier);
        await using var firstScope = database.Scope();
        await using var secondScope = database.Scope();
        var firstService = firstScope.ServiceProvider.GetRequiredService<InstallationConfigurationService>();
        var secondService = secondScope.ServiceProvider.GetRequiredService<InstallationConfigurationService>();

        var first = firstService.VerifyCredentialAsync(
            "first-api-key",
            TestContext.Current.CancellationToken);
        await verifier.FirstRequested.Task;
        var second = secondService.VerifyCredentialAsync(
            "second-api-key",
            TestContext.Current.CancellationToken);
        await verifier.SecondRequested.Task;

        verifier.SecondCompletion.SetResult(PrdbVerificationOutcome.Verified);
        Assert.Equal(PrdbConnectionUpdateVerdict.Verified, (await second).Verdict);
        verifier.FirstCompletion.SetResult(PrdbVerificationOutcome.Verified);
        Assert.Equal(PrdbConnectionUpdateVerdict.Superseded, (await first).Verdict);

        await using var finalScope = database.Scope();
        var final = finalScope.ServiceProvider.GetRequiredService<InstallationConfigurationService>();
        Assert.Equal(
            PrdbConnectionUpdateVerdict.Verified,
            (await final.RetryCredentialAsync(TestContext.Current.CancellationToken)).Verdict);
        Assert.Equal("second-api-key", verifier.LastCredential);
    }

    private static async Task ClaimAdministratorAsync(AccessService access)
    {
        var delivery = await access.CreateBootstrapAuthorizationAsync(
            TestContext.Current.CancellationToken);
        var authorization = (await File.ReadAllTextAsync(
            delivery.DeliveryPath!,
            TestContext.Current.CancellationToken)).Trim();
        _ = await access.ClaimAsync(
            authorization,
            "administrator",
            "administrator password",
            email: null,
            TestContext.Current.CancellationToken);
    }

    private sealed class StubPrdbConnectionVerifier(params PrdbVerificationOutcome[] outcomes)
        : IPrdbConnectionVerifier
    {
        private readonly Queue<PrdbVerificationOutcome> outcomes = new(outcomes);

        public List<string> Credentials { get; } = [];

        public Task<PrdbVerificationOutcome> VerifyAsync(
            string credential,
            CancellationToken cancellationToken = default)
        {
            Credentials.Add(credential);
            return Task.FromResult(outcomes.Dequeue());
        }
    }

    private sealed class CoordinatedPrdbConnectionVerifier : IPrdbConnectionVerifier
    {
        public TaskCompletionSource FirstRequested { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource SecondRequested { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<PrdbVerificationOutcome> FirstCompletion { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<PrdbVerificationOutcome> SecondCompletion { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public string? LastCredential { get; private set; }

        public Task<PrdbVerificationOutcome> VerifyAsync(
            string credential,
            CancellationToken cancellationToken = default)
        {
            LastCredential = credential;

            if (credential == "first-api-key")
            {
                FirstRequested.SetResult();
                return FirstCompletion.Task;
            }

            SecondRequested.TrySetResult();
            return SecondCompletion.Task;
        }
    }
}
