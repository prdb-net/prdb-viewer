using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Viewer.Core.Access;
using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Core.Recovery;
using Prdb.Viewer.Infrastructure.Access;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;
using Prdb.Viewer.Infrastructure.Personal;
using Prdb.Viewer.Infrastructure.Recovery;
using Prdb.Viewer.Infrastructure.Tests.Library;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Recovery;

public sealed class BackupTests
{
    private const string Passphrase = "a long enough archive passphrase";
    private const string WorkId = "6f1a2c34-0000-4000-8000-000000000001";

    [Fact]
    public async Task An_archive_validates_independently_and_returns_precious_state_to_an_empty_target()
    {
        await using var source = await PopulatedAsync();
        var sourceFiles = await BytesAsync(source);
        var archivePath = Path.Combine(source.Directory, "installation.prdbviewer");

        await using (var scope = source.Scope())
        {
            var result = await scope.ServiceProvider
                .GetRequiredService<BackupService>()
                .CreateAsync(archivePath, Passphrase, TestContext.Current.CancellationToken);
            Assert.True(result.Created, result.Reason);
            Assert.Equal(BackupArchiveFormat.CurrentVersion, result.FormatVersion);
        }

        // The archive protects itself: it is owner-only and the source library is untouched.
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(archivePath));
        }

        Assert.Equal(sourceFiles, await BytesAsync(source));

        await using var target = await TestDatabase.CreateAsync(
            mediaProbe: new FixtureProbe(),
            hasher: new FixtureHasher(),
            previewGenerator: new FixturePreviewGenerator(),
            identificationClient: new FixtureIdentificationClient());

        await using (var scope = target.Scope())
        {
            var validation = await scope.ServiceProvider
                .GetRequiredService<BackupService>()
                .ValidateAsync(archivePath, Passphrase, TestContext.Current.CancellationToken);
            Assert.True(validation.Valid, validation.Reason);
            Assert.Equal("argon2id", validation.Header!.KeyDerivation);
        }

        await using (var scope = target.Scope())
        {
            var restored = await scope.ServiceProvider
                .GetRequiredService<BackupService>()
                .RestoreAsync(archivePath, Passphrase, TestContext.Current.CancellationToken);
            Assert.Equal(RestoreVerdict.Restored, restored.Verdict);
            Assert.Equal(1, restored.Accounts);
        }

        await using (var scope = target.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            var account = await database.Accounts.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal("administrator", account.Username);
            Assert.Equal(AccountAuthority.Administrator, account.Authority);
            Assert.Equal(AccountState.Approved, account.State);

            // Ephemeral authority never travels: nobody keeps a session and nobody may bootstrap.
            Assert.Empty(await database.Sessions.ToListAsync(TestContext.Current.CancellationToken));
            Assert.Empty(await database.BootstrapAuthorizations.ToListAsync(
                TestContext.Current.CancellationToken));
            Assert.Empty(await database.RecoveryCodes.ToListAsync(
                TestContext.Current.CancellationToken));

            var personal = await database.PersonalVideoStates.SingleAsync(
                TestContext.Current.CancellationToken);
            Assert.NotNull(personal.FavouriteAddedAt);
            Assert.Equal(4, personal.PersonalRating);
            Assert.Equal(account.Id, personal.AccountId);

            var claim = await database.IdentificationClaims.SingleAsync(
                claim => claim.Dimension == IdentificationDimension.WorkIdentification,
                TestContext.Current.CancellationToken);
            Assert.Equal(WorkId, claim.TargetKey);

            var file = await database.VideoFiles.SingleAsync(TestContext.Current.CancellationToken);

            // Backed-up availability is last-known history, never proof of current storage.
            Assert.Equal(VideoFileAvailability.Unreachable, file.Availability);
            Assert.Equal(0, file.ConsecutiveCompleteAbsences);

            // Hashes are retained evidence; previews are regenerable and are rebuilt.
            Assert.NotNull(file.OsHash);
            Assert.Equal(VideoFilePreviewState.Pending, file.PreviewState);
            Assert.Null(file.PublicPreviewId);

            var configuration = await database.InstallationConfigurations.SingleAsync(
                TestContext.Current.CancellationToken);
            Assert.Equal("installation-key", configuration.ActivePrdbCredential);
            Assert.Equal(
                PrdbConnectionStatus.VerificationPending,
                configuration.PrdbConnectionStatus);
            Assert.NotNull(configuration.FirstPlayableVideoReachedAt);

            // Necessary work is derived again rather than resumed across installations.
            var work = await database.BackgroundWork.SingleAsync(
                TestContext.Current.CancellationToken);
            Assert.Equal(BackgroundWorkCategory.LibraryScan, work.Category);
            Assert.Equal(BackgroundWorkState.Queued, work.State);
        }
    }

    [Fact]
    public async Task A_wrong_passphrase_or_an_altered_archive_fails_before_any_target_changes()
    {
        await using var source = await PopulatedAsync();
        var archivePath = Path.Combine(source.Directory, "installation.prdbviewer");

        await using (var scope = source.Scope())
        {
            Assert.True((await scope.ServiceProvider
                .GetRequiredService<BackupService>()
                .CreateAsync(archivePath, Passphrase, TestContext.Current.CancellationToken))
                .Created);
        }

        await using var target = await TestDatabase.CreateAsync(mediaProbe: new FixtureProbe());

        await using (var scope = target.Scope())
        {
            var backup = scope.ServiceProvider.GetRequiredService<BackupService>();
            var wrong = await backup.ValidateAsync(
                archivePath,
                "an entirely different passphrase",
                TestContext.Current.CancellationToken);
            Assert.False(wrong.Valid);
            Assert.Equal(BackupValidationFailure.WrongPassphraseOrAltered, wrong.Failure);

            var restore = await backup.RestoreAsync(
                archivePath,
                "an entirely different passphrase",
                TestContext.Current.CancellationToken);
            Assert.Equal(RestoreVerdict.ArchiveRejected, restore.Verdict);
        }

        var original = await File.ReadAllBytesAsync(
            archivePath,
            TestContext.Current.CancellationToken);
        var altered = Path.Combine(source.Directory, "altered.prdbviewer");
        var bytes = original.ToArray();
        bytes[^1] ^= 0xFF;
        await File.WriteAllBytesAsync(altered, bytes, TestContext.Current.CancellationToken);
        var truncated = Path.Combine(source.Directory, "truncated.prdbviewer");
        await File.WriteAllBytesAsync(
            truncated,
            original.AsSpan(0, original.Length / 2).ToArray(),
            TestContext.Current.CancellationToken);

        await using (var scope = target.Scope())
        {
            var backup = scope.ServiceProvider.GetRequiredService<BackupService>();
            Assert.Equal(
                BackupValidationFailure.WrongPassphraseOrAltered,
                (await backup.ValidateAsync(
                    altered,
                    Passphrase,
                    TestContext.Current.CancellationToken)).Failure);
            Assert.Equal(
                BackupValidationFailure.WrongPassphraseOrAltered,
                (await backup.ValidateAsync(
                    truncated,
                    Passphrase,
                    TestContext.Current.CancellationToken)).Failure);
            Assert.Equal(
                BackupValidationFailure.NotAnArchive,
                (await backup.ValidateAsync(
                    Path.Combine(source.Directory, "missing.prdbviewer"),
                    Passphrase,
                    TestContext.Current.CancellationToken)).Failure);
        }

        // Nothing reached the target, so it is still an empty, restorable installation.
        await using (var scope = target.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            Assert.Empty(await database.Accounts.ToListAsync(TestContext.Current.CancellationToken));
            Assert.Empty(await database.Videos.ToListAsync(TestContext.Current.CancellationToken));
        }

        // The original archive is unchanged by every rejected attempt.
        Assert.Equal(
            original,
            await File.ReadAllBytesAsync(archivePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Restore_refuses_an_existing_installation_and_backup_refuses_to_overwrite()
    {
        await using var store = await PopulatedAsync();
        var archivePath = Path.Combine(store.Directory, "installation.prdbviewer");

        await using (var scope = store.Scope())
        {
            var backup = scope.ServiceProvider.GetRequiredService<BackupService>();
            Assert.True((await backup.CreateAsync(
                archivePath,
                Passphrase,
                TestContext.Current.CancellationToken)).Created);

            var again = await backup.CreateAsync(
                archivePath,
                Passphrase,
                TestContext.Current.CancellationToken);
            Assert.False(again.Created);
            Assert.Contains("already exists", again.Reason);

            var refused = await backup.RestoreAsync(
                archivePath,
                Passphrase,
                TestContext.Current.CancellationToken);
            Assert.Equal(RestoreVerdict.TargetNotEmpty, refused.Verdict);
            Assert.Contains("empty", refused.Reason);
        }

        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            Assert.Single(await database.Accounts.ToListAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public void An_unknown_newer_format_names_what_to_do_instead_of_being_guessed_at()
    {
        Assert.False(BackupArchiveFormat.CanRestoreDirectly(BackupArchiveFormat.CurrentVersion + 1));
        Assert.Contains(
            "Restore it with the product version that wrote it",
            BackupArchiveFormat.ExplainUnsupported(BackupArchiveFormat.CurrentVersion + 1));
        Assert.Contains(
            "product version 0.1",
            BackupArchiveFormat.ExplainUnsupported(0));
        Assert.False(BackupArchiveFormat.IsAcceptableCost(1024, 1));
        Assert.True(BackupArchiveFormat.IsAcceptableCost(
            BackupArchiveFormat.MemoryKibiBytes,
            BackupArchiveFormat.Iterations));
    }

    /// <summary>
    /// An installation with an Administrator, a configured connection, a scanned and identified
    /// Video, and one Account's private organisation — the state a Backup Archive must return.
    /// </summary>
    private static async Task<TestDatabase> PopulatedAsync()
    {
        var store = await TestDatabase.CreateAsync(
            mediaProbe: new FixtureProbe(),
            hasher: new FixtureHasher(),
            previewGenerator: new FixturePreviewGenerator(),
            identificationClient: new FixtureIdentificationClient()
                .Conclusive("first.mp4", WorkId, "A Known Work"));
        var source = Path.Combine(store.LibraryMountRoot.Path, "source");
        Directory.CreateDirectory(source);
        await File.WriteAllBytesAsync(
            Path.Combine(source, "first.mp4"),
            [1, 2, 3, 4],
            TestContext.Current.CancellationToken);
        await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");
        await LibraryPipeline.DrainAsync(store);

        Guid accountId;
        await using (var scope = store.Scope())
        {
            var access = scope.ServiceProvider.GetRequiredService<AccessService>();
            var authorization = await access.CreateBootstrapAuthorizationAsync(
                TestContext.Current.CancellationToken);
            var token = (await File.ReadAllTextAsync(
                authorization.DeliveryPath!,
                TestContext.Current.CancellationToken)).Trim();
            var claim = await access.ClaimAsync(
                token,
                "administrator",
                "administrator password",
                null,
                TestContext.Current.CancellationToken);
            accountId = claim.Session!.Account.Id;
        }

        await using (var scope = store.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            var videoId = (await database.Videos.FirstAsync(TestContext.Current.CancellationToken)).Id;
            var personal = scope.ServiceProvider.GetRequiredService<PersonalStateService>();
            await personal.SetFavouriteAsync(
                accountId,
                videoId,
                true,
                TestContext.Current.CancellationToken);
            await personal.SetRatingAsync(
                accountId,
                videoId,
                4,
                TestContext.Current.CancellationToken);
        }

        return store;
    }

    private static async Task<Dictionary<string, byte[]>> BytesAsync(TestDatabase store)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateFiles(
                     store.LibraryMountRoot.Path,
                     "*",
                     SearchOption.AllDirectories))
        {
            files[path] = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        }

        return files;
    }
}
