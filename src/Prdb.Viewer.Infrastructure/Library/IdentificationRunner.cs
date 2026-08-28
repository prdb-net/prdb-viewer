using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// Offers hashed Video Files to the public prdb API in bounded batches and turns the answers into
/// provenance-bearing Shared Library Knowledge. A missing credential, a refusal, or an outage
/// leaves the lane visibly waiting and never removes what is already known locally.
/// </summary>
public sealed class IdentificationRunner(
    ViewerDbContext database,
    IPrdbIdentificationClient client,
    IdentificationService identification,
    TimeProvider timeProvider) : VideoFileWorkRunner(database, timeProvider)
{
    private static readonly TimeSpan AvailabilityRetry = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan AuthorityRetry = TimeSpan.FromMinutes(30);

    protected override BackgroundWorkCategory Category => BackgroundWorkCategory.Identification;

    protected override int BatchSize => 25;

    protected override IQueryable<VideoFileRow> Outstanding(Guid libraryDirectoryId) =>
        Database.VideoFiles.Where(file =>
            file.LibraryDirectoryId == libraryDirectoryId &&
            file.Availability == VideoFileAvailability.Available &&
            file.HashedSha256 == file.Sha256 &&
            (file.IdentifiedSha256 == null || file.IdentifiedSha256 != file.Sha256));

    /// <summary>
    /// A new run asks again about Videos that are still Unknown, because the remote catalogue may
    /// have learned about them since. Established knowledge is never re-derived this way.
    /// </summary>
    protected override Task RetryEarlierFailuresAsync(
        Guid libraryDirectoryId,
        CancellationToken cancellationToken) =>
        Database.VideoFiles
            .Where(file => file.LibraryDirectoryId == libraryDirectoryId &&
                           file.Availability == VideoFileAvailability.Available &&
                           file.IdentifiedSha256 != null &&
                           !Database.IdentificationClaims.Any(claim =>
                               claim.VideoId == file.VideoId &&
                               claim.Dimension == IdentificationDimension.WorkIdentification &&
                               claim.Status == IdentificationClaimStatus.Current))
            .ExecuteUpdateAsync(
                update => update.SetProperty(file => file.IdentifiedSha256, (string?)null),
                cancellationToken);

    protected override async Task AdvanceAsync(
        BackgroundWorkRow work,
        IReadOnlyList<VideoFileRow> files,
        CancellationToken cancellationToken)
    {
        var configuration = await Database.InstallationConfigurations
            .AsTracking()
            .SingleAsync(cancellationToken);

        if (string.IsNullOrEmpty(configuration.ActivePrdbCredential))
        {
            await AddIssueOnceAsync(
                work,
                "prdb connection",
                WorkIssueCause.Configuration,
                WorkIssueSeverity.OperationalBlocker,
                RemediationOwner.Administrator,
                "Videos cannot be identified because the installation has no verified prdb API key.",
                "Verify a prdb API key in Installation Configuration.",
                cancellationToken);
            await WaitAsync(
                work,
                "A verified prdb API key is required before Videos can be identified.",
                AuthorityRetry,
                cancellationToken);
            return;
        }

        var result = await client.IdentifyAsync(
            configuration.ActivePrdbCredential,
            files.Select(file => new RemoteIdentificationRequest(
                file.Id,
                Path.GetFileName(file.RelativePath),
                file.Size,
                file.OsHash,
                file.PerceptualHash)).ToArray(),
            cancellationToken);

        switch (result.Status)
        {
            case IdentificationBatchStatus.Rejected:
                configuration.PrdbConnectionStatus = PrdbConnectionStatus.Rejected;
                configuration.LastConnectionIssue = PrdbConnectionIssue.ExternalAuthority;
                configuration.LastConnectionAttemptAt = Now();
                await AddIssueOnceAsync(
                    work,
                    "prdb connection",
                    WorkIssueCause.ExternalAuthority,
                    WorkIssueSeverity.OperationalBlocker,
                    RemediationOwner.Administrator,
                    "prdb refused the installation credential, so no new identifications are made. " +
                    "Established knowledge and playback are unaffected.",
                    "Replace the prdb API key in Installation Configuration.",
                    cancellationToken);
                await WaitAsync(
                    work,
                    "prdb refused the installation credential.",
                    AuthorityRetry,
                    cancellationToken);
                return;

            case IdentificationBatchStatus.Unavailable:
                if (configuration.PrdbConnectionStatus == PrdbConnectionStatus.Verified)
                {
                    configuration.PrdbConnectionStatus = PrdbConnectionStatus.Degraded;
                    configuration.LastConnectionIssue = PrdbConnectionIssue.ExternalAvailability;
                    configuration.LastConnectionAttemptAt = Now();
                }

                await AddIssueOnceAsync(
                    work,
                    "prdb connection",
                    WorkIssueCause.ExternalAvailability,
                    WorkIssueSeverity.ScopedIssue,
                    RemediationOwner.AutomaticRecovery,
                    $"prdb is temporarily unavailable, so identification is paused. {result.Detail}",
                    "No action is required; the lane retries by itself.",
                    cancellationToken);
                await WaitAsync(
                    work,
                    "prdb is temporarily unavailable.",
                    AvailabilityRetry,
                    cancellationToken);
                return;
        }

        if (configuration.PrdbConnectionStatus is PrdbConnectionStatus.Degraded)
        {
            configuration.PrdbConnectionStatus = PrdbConnectionStatus.Verified;
            configuration.LastConnectionIssue = null;
            configuration.LastConnectionVerifiedAt = Now();
        }

        await ResolveIssuesAsync(work, WorkIssueCause.ExternalAvailability, cancellationToken);
        await ResolveIssuesAsync(work, WorkIssueCause.ExternalAuthority, cancellationToken);
        await ResolveIssuesAsync(work, WorkIssueCause.Configuration, cancellationToken);

        foreach (var identified in result.Results)
        {
            await identification.ApplyRemoteIdentificationAsync(identified, cancellationToken);
        }

        var answered = result.Results.Select(identified => identified.VideoFileId).ToHashSet();
        var unanswered = files
            .Where(file => !answered.Contains(file.Id))
            .Select(file => file.Id)
            .ToArray();

        if (unanswered.Length > 0)
        {
            var now = Now();
            await Database.VideoFiles
                .Where(file => unanswered.Contains(file.Id))
                .ExecuteUpdateAsync(
                    update => update
                        .SetProperty(file => file.IdentifiedAt, now)
                        .SetProperty(file => file.IdentifiedSha256, file => file.Sha256),
                    cancellationToken);
        }

        work.CompletedItemCount += files.Count;
    }
}
