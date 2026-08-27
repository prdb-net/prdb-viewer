using System.Data;

using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Access;
using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Configuration;

public sealed class InstallationConfigurationService(
    ViewerDbContext database,
    LibraryMountRoot mountRoot,
    LibraryDirectoryInspector directories,
    IPrdbConnectionVerifier prdb,
    TimeProvider timeProvider)
{
    public async Task<InstallationConfigurationSummary> GetAsync(
        CancellationToken cancellationToken = default)
    {
        var now = Now();
        var claimed = await database.Accounts.AnyAsync(
            account => account.Authority == AccountAuthority.Administrator &&
                       account.State == AccountState.Approved,
            cancellationToken);
        var configuration = await database.InstallationConfigurations
            .SingleOrDefaultAsync(cancellationToken);
        var activeDirectories = await database.LibraryDirectories
            .Where(directory => directory.State == LibraryDirectoryState.Active)
            .OrderBy(directory => directory.Name)
            .ToListAsync(cancellationToken);
        var hasPendingDirectory = await database.LibraryDirectoryStages.AnyAsync(
            stage => stage.ExpiresAt > now,
            cancellationToken);
        var connection = configuration?.PrdbConnectionStatus ?? PrdbConnectionStatus.Missing;
        var status = InstallationConfigurationRule.Determine(
            claimed,
            connection,
            activeDirectories.Count > 0,
            hasPendingDirectory,
            activeDirectories.Any(directory => directory.InitialProcessingStartedAt is not null));

        return new InstallationConfigurationSummary(
            status,
            connection,
            configuration?.ActivePrdbCredential is not null ||
            configuration?.PendingPrdbCredential is not null,
            configuration?.PendingPrdbCredential is not null,
            AsOffset(configuration?.LastConnectionAttemptAt),
            AsOffset(configuration?.LastConnectionVerifiedAt),
            configuration?.LastConnectionIssue,
            mountRoot.Path,
            activeDirectories.Select(Summary).ToArray());
    }

    public async Task<PrdbConnectionUpdateResult> VerifyCredentialAsync(
        string credential,
        CancellationToken cancellationToken = default)
    {
        if (!ValidCredential(credential))
        {
            return new PrdbConnectionUpdateResult(PrdbConnectionUpdateVerdict.InvalidInput);
        }

        var revision = Guid.CreateVersion7();
        var configuration = await GetOrCreateConfigurationAsync(cancellationToken);
        configuration.PendingPrdbCredential = credential;
        configuration.PendingCredentialRevision = revision;
        configuration.PrdbConnectionStatus = PrdbConnectionStatus.VerificationPending;
        configuration.LastConnectionAttemptAt = Now();
        configuration.LastConnectionIssue = null;
        await database.SaveChangesAsync(cancellationToken);

        var outcome = await prdb.VerifyAsync(credential, cancellationToken);
        return await CompleteCredentialVerificationAsync(revision, outcome, cancellationToken);
    }

    public async Task<PrdbConnectionUpdateResult> RetryCredentialAsync(
        CancellationToken cancellationToken = default)
    {
        var configuration = await database.InstallationConfigurations
            .AsTracking()
            .SingleOrDefaultAsync(cancellationToken);
        var credential = configuration?.PendingPrdbCredential ?? configuration?.ActivePrdbCredential;

        return credential is null
            ? new PrdbConnectionUpdateResult(PrdbConnectionUpdateVerdict.Missing)
            : await VerifyCredentialAsync(credential, cancellationToken);
    }

    public IReadOnlyList<string> DiscoverLibraryDirectories() => directories.Discover();

    public async Task<LibraryDirectoryStageResult> StageLibraryDirectoryAsync(
        string name,
        string requestedPath,
        CancellationToken cancellationToken = default)
    {
        if (!LibraryDirectoryNameRule.IsValid(name))
        {
            return new LibraryDirectoryStageResult(LibraryDirectoryStageVerdict.InvalidInput);
        }

        var inspection = directories.Inspect(requestedPath);

        if (inspection.Verdict != LibraryDirectoryStageVerdict.Staged)
        {
            return new LibraryDirectoryStageResult(inspection.Verdict);
        }

        if (await database.LibraryDirectories.AnyAsync(
                directory => directory.State == LibraryDirectoryState.Active &&
                             directory.ContainerPath == inspection.ContainerPath,
                cancellationToken))
        {
            return new LibraryDirectoryStageResult(LibraryDirectoryStageVerdict.AlreadyConfigured);
        }

        var now = Now();
        await database.LibraryDirectoryStages
            .Where(stage => stage.ExpiresAt <= now)
            .ExecuteDeleteAsync(cancellationToken);
        var stage = new LibraryDirectoryStageRow
        {
            Id = Guid.CreateVersion7(),
            Name = LibraryDirectoryNameRule.Normalize(name),
            ContainerPath = inspection.ContainerPath!,
            CreatedAt = now,
            ExpiresAt = now + ConfigurationStageLifetimes.LibraryDirectory,
        };
        database.LibraryDirectoryStages.Add(stage);
        await database.SaveChangesAsync(cancellationToken);

        return new LibraryDirectoryStageResult(
            LibraryDirectoryStageVerdict.Staged,
            stage.Id,
            stage.Name,
            stage.ContainerPath,
            AsOffset(stage.ExpiresAt));
    }

    public async Task<LibraryDirectoryActivationResult> ActivateLibraryDirectoryAsync(
        Guid stageId,
        CancellationToken cancellationToken = default)
    {
        var stage = await database.LibraryDirectoryStages
            .AsTracking()
            .SingleOrDefaultAsync(row => row.Id == stageId, cancellationToken);

        if (stage is null)
        {
            return new LibraryDirectoryActivationResult(LibraryDirectoryActivationVerdict.NotFound);
        }

        if (stage.ExpiresAt <= Now())
        {
            database.LibraryDirectoryStages.Remove(stage);
            await database.SaveChangesAsync(cancellationToken);
            return new LibraryDirectoryActivationResult(LibraryDirectoryActivationVerdict.Expired);
        }

        var inspection = directories.Inspect(stage.ContainerPath);

        if (inspection.Verdict != LibraryDirectoryStageVerdict.Staged)
        {
            database.LibraryDirectoryStages.Remove(stage);
            await database.SaveChangesAsync(cancellationToken);
            return new LibraryDirectoryActivationResult(LibraryDirectoryActivationVerdict.NoLongerValid);
        }

        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        if (await database.LibraryDirectories.AnyAsync(
                directory => directory.State == LibraryDirectoryState.Active &&
                             directory.ContainerPath == stage.ContainerPath,
                cancellationToken))
        {
            database.LibraryDirectoryStages.Remove(stage);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new LibraryDirectoryActivationResult(
                LibraryDirectoryActivationVerdict.AlreadyConfigured);
        }

        var now = Now();
        var directory = new LibraryDirectoryRow
        {
            Id = Guid.CreateVersion7(),
            Name = stage.Name,
            ContainerPath = stage.ContainerPath,
            State = LibraryDirectoryState.Active,
            Health = LibraryDirectoryHealth.Healthy,
            ConfigurationGeneration = 1,
            CreatedAt = stage.CreatedAt,
            ActivatedAt = now,
        };
        database.LibraryDirectories.Add(directory);
        database.LibraryDirectoryStages.Remove(stage);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new LibraryDirectoryActivationResult(
            LibraryDirectoryActivationVerdict.Activated,
            Summary(directory));
    }

    private async Task<PrdbConnectionUpdateResult> CompleteCredentialVerificationAsync(
        Guid revision,
        PrdbVerificationOutcome outcome,
        CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        var configuration = await database.InstallationConfigurations
            .AsTracking()
            .SingleAsync(cancellationToken);

        if (configuration.PendingCredentialRevision != revision)
        {
            return new PrdbConnectionUpdateResult(PrdbConnectionUpdateVerdict.Superseded);
        }

        var now = Now();
        configuration.LastConnectionAttemptAt = now;
        configuration.PendingCredentialRevision = null;

        switch (outcome)
        {
            case PrdbVerificationOutcome.Verified:
                configuration.ActivePrdbCredential = configuration.PendingPrdbCredential;
                configuration.PendingPrdbCredential = null;
                configuration.PrdbConnectionStatus = PrdbConnectionStatus.Verified;
                configuration.LastConnectionVerifiedAt = now;
                configuration.LastConnectionIssue = null;
                await database.SaveChangesAsync(cancellationToken);
                return new PrdbConnectionUpdateResult(PrdbConnectionUpdateVerdict.Verified);

            case PrdbVerificationOutcome.Rejected:
                var rejectedActive = string.Equals(
                    configuration.PendingPrdbCredential,
                    configuration.ActivePrdbCredential,
                    StringComparison.Ordinal);
                configuration.PendingPrdbCredential = null;
                configuration.PrdbConnectionStatus = rejectedActive ||
                    configuration.ActivePrdbCredential is null
                    ? PrdbConnectionStatus.Rejected
                    : PrdbConnectionStatus.Verified;
                configuration.LastConnectionIssue = rejectedActive ||
                    configuration.ActivePrdbCredential is null
                    ? PrdbConnectionIssue.ExternalAuthority
                    : PrdbConnectionIssue.ReplacementRejected;
                await database.SaveChangesAsync(cancellationToken);
                return new PrdbConnectionUpdateResult(PrdbConnectionUpdateVerdict.Rejected);

            case PrdbVerificationOutcome.Unavailable:
                configuration.PrdbConnectionStatus = configuration.ActivePrdbCredential is null
                    ? PrdbConnectionStatus.VerificationPending
                    : PrdbConnectionStatus.Degraded;
                configuration.LastConnectionIssue = PrdbConnectionIssue.ExternalAvailability;
                await database.SaveChangesAsync(cancellationToken);
                return new PrdbConnectionUpdateResult(
                    PrdbConnectionUpdateVerdict.VerificationPending);

            default:
                throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null);
        }
    }

    private async Task<InstallationConfigurationRow> GetOrCreateConfigurationAsync(
        CancellationToken cancellationToken)
    {
        var configuration = await database.InstallationConfigurations
            .AsTracking()
            .SingleOrDefaultAsync(cancellationToken);

        if (configuration is not null)
        {
            return configuration;
        }

        configuration = new InstallationConfigurationRow();
        database.InstallationConfigurations.Add(configuration);
        return configuration;
    }

    private static bool ValidCredential(string? credential) =>
        credential is { Length: >= 8 and <= 4096 } &&
        !credential.Any(char.IsControl) &&
        credential.Trim().Length == credential.Length;

    private static LibraryDirectorySummary Summary(LibraryDirectoryRow directory) =>
        new(
            directory.Id,
            directory.Name,
            directory.ContainerPath,
            directory.State,
            directory.Health,
            directory.ConfigurationGeneration,
            AsOffset(directory.ActivatedAt)!.Value,
            directory.InitialProcessingStartedAt is not null);

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    private static DateTimeOffset? AsOffset(DateTime? value) => value is null
        ? null
        : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
}
