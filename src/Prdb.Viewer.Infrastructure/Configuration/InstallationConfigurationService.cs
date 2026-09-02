using System.Data;

using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Access;
using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Configuration;

public sealed class InstallationConfigurationService(
    ViewerDbContext database,
    LibraryMountRoot mountRoot,
    LibraryDirectoryInspector directories,
    IPrdbConnectionVerifier prdb,
    LibraryWorkScheduler work,
    VideoProjection projection,
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
        var facts = await DirectoryFactsAsync(
            activeDirectories.Select(directory => directory.Id).ToArray(),
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
            AsOffset(configuration?.ConfiguredAt),
            AsOffset(configuration?.FirstPlayableVideoReachedAt),
            mountRoot.Path,
            activeDirectories.Select(directory => Summary(directory, facts)).ToArray());
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
            return new LibraryDirectoryStageResult(LibraryDirectoryStageVerdict.InvalidName);
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
        work.QueueInitialScan(directory, now);
        var installation = await database.InstallationConfigurations
            .AsTracking()
            .SingleAsync(cancellationToken);

        if (installation.PrdbConnectionStatus == PrdbConnectionStatus.Verified)
        {
            installation.ConfiguredAt ??= now;
        }

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new LibraryDirectoryActivationResult(
            LibraryDirectoryActivationVerdict.Activated,
            Summary(directory));
    }

    /// <summary>
    /// Withdraws a Library Directory from the active library, on the Administrator's explicit
    /// confirmation.
    ///
    /// The occurrences beneath it become Removed while everything established about them survives:
    /// identity, path history, technical facts, identification and its provenance, Shared Library
    /// Knowledge, and every Account's Personal State. A Video backed by an occurrence in another
    /// Active Library Directory stays active and derives its availability from that one.
    ///
    /// Raising the configuration generation is what stops work in flight from crossing the removal:
    /// every runner compares the generation it was queued under against the directory's, and a Scan
    /// that finds the directory no longer Active is not a complete observation of anything.
    /// </summary>
    public async Task<LibraryDirectoryRemovalResult> RemoveLibraryDirectoryAsync(
        Guid libraryDirectoryId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var directory = await database.LibraryDirectories
            .AsTracking()
            .SingleOrDefaultAsync(row => row.Id == libraryDirectoryId, cancellationToken);

        if (directory is null)
        {
            return new LibraryDirectoryRemovalResult(LibraryDirectoryRemovalVerdict.NotFound);
        }

        if (directory.State != LibraryDirectoryState.Active)
        {
            return new LibraryDirectoryRemovalResult(LibraryDirectoryRemovalVerdict.AlreadyRemoved);
        }

        var now = Now();
        directory.State = LibraryDirectoryState.Removed;
        directory.RemovedAt = now;
        directory.ConfigurationGeneration += 1;
        // Nothing is due for a Library Directory that is no longer part of the library. The
        // periodic sweep reads Active rows only, so this is honesty about the row rather than the
        // thing that stops it being scanned.
        directory.NextScanDueAt = null;

        var occurrences = await database.VideoFiles
            .AsTracking()
            .Where(file => file.LibraryDirectoryId == libraryDirectoryId &&
                           file.Availability != VideoFileAvailability.Removed)
            .ToListAsync(cancellationToken);

        foreach (var occurrence in occurrences)
        {
            occurrence.Availability = VideoFileAvailability.Removed;
        }

        // Work already queued under the old generation is refused by its runner, but a run left
        // Queued or Waiting would sit there forever with nothing to do.
        await database.BackgroundWork
            .Where(row => row.LibraryDirectoryId == libraryDirectoryId &&
                          row.FinishedAt == null)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(row => row.CancellationRequested, true)
                    .SetProperty(row => row.UpdatedAt, now),
                cancellationToken);

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Availability is a fact about a Video derived from its occurrences, so the Videos that
        // just lost one are re-derived rather than left describing a directory that is gone.
        var affected = occurrences.Select(file => file.VideoId).Distinct().ToArray();
        await projection.RefreshAsync(affected, cancellationToken);

        return new LibraryDirectoryRemovalResult(
            LibraryDirectoryRemovalVerdict.Removed,
            occurrences.Count,
            affected.Length);
    }

    /// <summary>
    /// Lets identification continue as soon as a usable credential exists. A lane that stopped for
    /// a missing or refused key carries exactly that condition, so a newly verified key is the
    /// Resolution Evidence it was waiting for.
    /// </summary>
    private Task ResumeIdentificationAsync(DateTime now, CancellationToken cancellationToken) =>
        database.BackgroundWork
            .Where(row => row.Category == BackgroundWorkCategory.Identification &&
                          row.State == BackgroundWorkState.Waiting)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(row => row.State, BackgroundWorkState.Queued)
                    .SetProperty(row => row.NextAttemptAt, (DateTime?)null)
                    .SetProperty(row => row.WaitingReason, (string?)null)
                    .SetProperty(row => row.UpdatedAt, now),
                cancellationToken);

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

                if (await database.LibraryDirectories.AnyAsync(
                        directory => directory.State == LibraryDirectoryState.Active &&
                                     directory.InitialProcessingStartedAt != null,
                        cancellationToken))
                {
                    configuration.ConfiguredAt ??= now;
                }

                await ResumeIdentificationAsync(now, cancellationToken);
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

    /// <summary>
    /// What a configured Library Directory can say about itself beyond its name and its path.
    ///
    /// A directory that is mounted and readable is not the same as one a Scan has actually read,
    /// and neither is the same as one holding Video Files. The Installation screen is where an
    /// Operator looks when the library is empty, so the answer belongs beside the directory rather
    /// than only in the background-work history.
    /// </summary>
    private sealed record DirectoryFacts(
        int AvailableVideoFileCount,
        DateTime? LastScanCompletedAt,
        DateTime? LastScanStartedAt,
        int LastScanCandidateCount,
        bool LastScanCoveredEverything);

    private async Task<IReadOnlyDictionary<Guid, DirectoryFacts>> DirectoryFactsAsync(
        IReadOnlyCollection<Guid> directoryIds,
        CancellationToken cancellationToken)
    {
        if (directoryIds.Count == 0)
        {
            return new Dictionary<Guid, DirectoryFacts>();
        }

        var available = await database.VideoFiles
            .Where(file => directoryIds.Contains(file.LibraryDirectoryId) &&
                           file.Availability == VideoFileAvailability.Available)
            .GroupBy(file => file.LibraryDirectoryId)
            .Select(group => new { LibraryDirectoryId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.LibraryDirectoryId, entry => entry.Count, cancellationToken);

        // The newest Library Scan of each directory that actually finished. An unfinished one says
        // nothing yet about what is there, and a superseded one says it about a different
        // configuration.
        var scans = await database.BackgroundWork
            .Where(work => directoryIds.Contains(work.LibraryDirectoryId) &&
                           work.Category == BackgroundWorkCategory.LibraryScan &&
                           work.FinishedAt != null)
            .GroupBy(work => work.LibraryDirectoryId)
            .Select(group => group
                .OrderByDescending(work => work.FinishedAt)
                .Select(work => new
                {
                    work.LibraryDirectoryId,
                    work.FinishedAt,
                    work.StartedAt,
                    work.DiscoveredCandidateCount,
                    work.CoverageComplete,
                })
                .First())
            .ToListAsync(cancellationToken);
        var newest = scans.ToDictionary(scan => scan.LibraryDirectoryId);

        return directoryIds.ToDictionary(
            id => id,
            id => new DirectoryFacts(
                available.TryGetValue(id, out var count) ? count : 0,
                newest.TryGetValue(id, out var scan) ? scan.FinishedAt : null,
                scan?.StartedAt,
                scan?.DiscoveredCandidateCount ?? 0,
                scan?.CoverageComplete ?? false));
    }

    private static LibraryDirectorySummary Summary(
        LibraryDirectoryRow directory,
        IReadOnlyDictionary<Guid, DirectoryFacts>? facts = null) =>
        new(
            directory.Id,
            directory.Name,
            directory.ContainerPath,
            directory.State,
            directory.Health,
            directory.ConfigurationGeneration,
            AsOffset(directory.ActivatedAt)!.Value,
            directory.InitialProcessingStartedAt is not null,
            Facts(facts, directory.Id).AvailableVideoFileCount,
            AsOffset(Facts(facts, directory.Id).LastScanCompletedAt),
            AsOffset(Facts(facts, directory.Id).LastScanStartedAt),
            Facts(facts, directory.Id).LastScanCandidateCount,
            Facts(facts, directory.Id).LastScanCoveredEverything,
            AsOffset(directory.NextScanDueAt));

    private static DirectoryFacts Facts(
        IReadOnlyDictionary<Guid, DirectoryFacts>? facts,
        Guid directoryId) =>
        facts is not null && facts.TryGetValue(directoryId, out var found)
            ? found
            : new DirectoryFacts(0, null, null, 0, false);

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    private static DateTimeOffset? AsOffset(DateTime? value) => value is null
        ? null
        : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
}
