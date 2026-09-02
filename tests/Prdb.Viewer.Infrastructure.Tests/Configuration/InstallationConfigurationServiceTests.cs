using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Infrastructure.Access;
using Prdb.Viewer.Infrastructure.Configuration;
using Prdb.Viewer.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Configuration;

public sealed class InstallationConfigurationServiceTests
{
    [Theory]
    [InlineData("  /libraries/main  ")]
    [InlineData("/libraries/main/")]
    public async Task A_pasted_container_path_is_accepted_despite_its_surroundings(string typed)
    {
        await using var store = await TestDatabase.CreateAsync();
        var mounted = Path.Combine(store.LibraryMountRoot.Path, "main");
        Directory.CreateDirectory(mounted);
        await using var scope = store.Scope();

        var staged = await scope.ServiceProvider
            .GetRequiredService<InstallationConfigurationService>()
            .StageLibraryDirectoryAsync(
                "Main Library",
                typed.Replace("/libraries/main", mounted, StringComparison.Ordinal),
                TestContext.Current.CancellationToken);

        Assert.Equal(LibraryDirectoryStageVerdict.Staged, staged.Verdict);
        Assert.Equal(mounted, staged.ContainerPath);
    }

    [Fact]
    public async Task An_unusable_name_and_an_unusable_path_are_reported_apart()
    {
        await using var store = await TestDatabase.CreateAsync();
        var mounted = Path.Combine(store.LibraryMountRoot.Path, "main");
        Directory.CreateDirectory(mounted);
        await using var scope = store.Scope();
        var configuration = scope.ServiceProvider
            .GetRequiredService<InstallationConfigurationService>();

        // Which field to correct is the whole point of the message, so the two are not collapsed.
        Assert.Equal(
            LibraryDirectoryStageVerdict.InvalidName,
            (await configuration.StageLibraryDirectoryAsync(
                "   ",
                mounted,
                TestContext.Current.CancellationToken)).Verdict);
        Assert.Equal(
            LibraryDirectoryStageVerdict.InvalidPath,
            (await configuration.StageLibraryDirectoryAsync(
                "Main Library",
                "libraries/main",
                TestContext.Current.CancellationToken)).Verdict);
    }

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

    /// Withdrawing a Library Directory takes its occurrences out of the active library and leaves
    /// everything established about them alone. It also has to stop work in flight from crossing
    /// the removal, which is what the configuration generation is for.
    [Fact]
    public async Task Removing_a_library_directory_withdraws_its_occurrences_and_stops_its_work()
    {
        await using var database = await TestDatabase.CreateAsync(
            prdbConnectionVerifier: new StubPrdbConnectionVerifier(PrdbVerificationOutcome.Verified));
        await using var scope = database.Scope();
        var configuration = scope.ServiceProvider.GetRequiredService<InstallationConfigurationService>();
        await configuration.VerifyCredentialAsync("api-key", TestContext.Current.CancellationToken);

        var library = Path.Combine(database.LibraryMountRoot.Path, "withdrawn");
        Directory.CreateDirectory(library);
        var staged = await configuration.StageLibraryDirectoryAsync(
            "Withdrawn",
            library,
            TestContext.Current.CancellationToken);
        var activated = await configuration.ActivateLibraryDirectoryAsync(
            staged.StageId!.Value,
            TestContext.Current.CancellationToken);
        var directoryId = activated.Directory!.Id;
        var generationBefore = activated.Directory.ConfigurationGeneration;

        var removed = await configuration.RemoveLibraryDirectoryAsync(
            directoryId,
            TestContext.Current.CancellationToken);
        Assert.Equal(LibraryDirectoryRemovalVerdict.Removed, removed.Verdict);

        // It leaves the active configuration, so the screen that lists what is configured no
        // longer names it.
        var current = await configuration.GetAsync(TestContext.Current.CancellationToken);
        Assert.Empty(current.LibraryDirectories);

        await using var reader = database.Scope();
        var store = reader.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var row = await store.LibraryDirectories.SingleAsync(
            directory => directory.Id == directoryId,
            TestContext.Current.CancellationToken);

        // Retained rather than deleted: the record, its path history and its identity survive.
        Assert.Equal(LibraryDirectoryState.Removed, row.State);
        Assert.NotNull(row.RemovedAt);
        Assert.Equal("Withdrawn", row.Name);

        // The generation moved, which is what every runner compares against before it does anything.
        Assert.True(row.ConfigurationGeneration > generationBefore);

        // Nothing is due for it either. A Library Directory that has left the library is not
        // waiting for its next Scan.
        Assert.Null(row.NextScanDueAt);

        // And the Scan queued on activation is asked to stop rather than left waiting for a
        // directory that is gone.
        var work = await store.BackgroundWork
            .Where(entry => entry.LibraryDirectoryId == directoryId && entry.FinishedAt == null)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.All(work, entry => Assert.True(entry.CancellationRequested));

        // Removing it twice is not a second removal.
        Assert.Equal(
            LibraryDirectoryRemovalVerdict.AlreadyRemoved,
            (await configuration.RemoveLibraryDirectoryAsync(
                directoryId,
                TestContext.Current.CancellationToken)).Verdict);
        Assert.Equal(
            LibraryDirectoryRemovalVerdict.NotFound,
            (await configuration.RemoveLibraryDirectoryAsync(
                Guid.CreateVersion7(),
                TestContext.Current.CancellationToken)).Verdict);
    }

    /// A directory reports what a Scan of it found, because "configured" and "holds Video Files"
    /// are different facts and an Operator staring at an empty library needs both.
    [Fact]
    public async Task A_configured_directory_reports_what_its_last_scan_found()
    {
        await using var database = await TestDatabase.CreateAsync(
            prdbConnectionVerifier: new StubPrdbConnectionVerifier(PrdbVerificationOutcome.Verified));
        await using var scope = database.Scope();
        var configuration = scope.ServiceProvider.GetRequiredService<InstallationConfigurationService>();
        await configuration.VerifyCredentialAsync("api-key", TestContext.Current.CancellationToken);

        var library = Path.Combine(database.LibraryMountRoot.Path, "described");
        Directory.CreateDirectory(library);
        var staged = await configuration.StageLibraryDirectoryAsync(
            "Described",
            library,
            TestContext.Current.CancellationToken);
        await configuration.ActivateLibraryDirectoryAsync(
            staged.StageId!.Value,
            TestContext.Current.CancellationToken);

        // Nothing has finished reading it yet, and the summary says exactly that rather than
        // implying a Scan found nothing.
        var before = await configuration.GetAsync(TestContext.Current.CancellationToken);
        var directory = Assert.Single(before.LibraryDirectories);
        Assert.Null(directory.LastScanCompletedAt);
        Assert.Equal(0, directory.AvailableVideoFileCount);
        Assert.Equal(LibraryDirectoryHealth.Healthy, directory.Health);
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
