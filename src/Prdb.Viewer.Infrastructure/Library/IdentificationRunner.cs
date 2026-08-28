using System.Security.Cryptography;
using System.Text;

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
    WorkIssueRecorder issues,
    TimeProvider timeProvider) : VideoFileWorkRunner(database, issues, timeProvider)
{
    private const string PrdbScope = "prdb.net";

    private static readonly TimeSpan AvailabilityRetry = TimeSpan.FromMinutes(5);

    protected override BackgroundWorkCategory Category => BackgroundWorkCategory.Identification;

    protected override string Phase => BackgroundWorkPhases.Identifying;

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
            await ReportAsync(
                work,
                new WorkIssueReport(
                    WorkIssueCause.Configuration,
                    WorkIssueSeverity.OperationalBlocker,
                    WorkIssueRetryDisposition.NoAutomaticRetry,
                    PrdbScope,
                    PrdbScope,
                    Phase,
                    WorkIssueMessages.NeedsConfiguration(),
                    "Videos cannot be identified because the installation has no verified prdb " +
                    "API key. Local inspection, previews, browsing, and playback continue, and " +
                    "everything already established stays visible.",
                    "No new prdb identification is attempted.",
                    "Verify a prdb API key in Installation Configuration.",
                    "The installation holds no active prdb credential.",
                    "A verified prdb API key followed by a successful identification request.")
                {
                    AggregatesItems = false,
                },
                cancellationToken);
            await HoldAsync(
                work,
                "A verified prdb API key is required before Videos can be identified.",
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
                await ReportAsync(
                    work,
                    new WorkIssueReport(
                        WorkIssueCause.ExternalAuthority,
                        WorkIssueSeverity.OperationalBlocker,
                        WorkIssueRetryDisposition.NoAutomaticRetry,
                        PrdbScope,
                        PrdbScope,
                        Phase,
                        WorkIssueMessages.PrdbRejected(),
                        "prdb refused the installation credential, so no request is repeated " +
                        "against unchanged authority. Established knowledge, browsing, and " +
                        "playback are unaffected, and local work continues.",
                        "No new prdb identification is attempted.",
                        "Replace the prdb API key in Installation Configuration.",
                        $"{result.Detail ?? "prdb refused the request."} " +
                        $"Credential {Masked(configuration.ActivePrdbCredential)}.",
                        "A verified replacement credential followed by a successful " +
                        "identification request.")
                    {
                        AggregatesItems = false,
                    },
                    cancellationToken);
                await HoldAsync(
                    work,
                    "prdb refused the installation credential.",
                    cancellationToken);
                return;

            case IdentificationBatchStatus.Unavailable:
                if (configuration.PrdbConnectionStatus == PrdbConnectionStatus.Verified)
                {
                    configuration.PrdbConnectionStatus = PrdbConnectionStatus.Degraded;
                    configuration.LastConnectionIssue = PrdbConnectionIssue.ExternalAvailability;
                    configuration.LastConnectionAttemptAt = Now();
                }

                var nextAttempt = Now() + AvailabilityRetry;
                await ReportAsync(
                    work,
                    new WorkIssueReport(
                        WorkIssueCause.ExternalAvailability,
                        WorkIssueSeverity.ScopedIssue,
                        WorkIssueRetryDisposition.AutomaticRetryScheduled,
                        PrdbScope,
                        PrdbScope,
                        Phase,
                        WorkIssueMessages.PrdbUnavailable(),
                        "Remote identification waits with backoff. Local inspection, previews, " +
                        "browsing, and playback continue, and one message covers every waiting " +
                        "file instead of one alert per Video.",
                        "New identifications are delayed until prdb answers again.",
                        "No action is required; the lane retries by itself.",
                        result.Detail ?? "prdb did not answer the identification request.",
                        "A successful identification request for the waiting files.")
                    {
                        NextAttemptAt = nextAttempt,
                        AggregatesItems = false,
                    },
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

        const string evidence = "prdb answered an identification request for the waiting files.";
        await ResolveAsync(work, WorkIssueCause.ExternalAvailability, evidence, cancellationToken);
        await ResolveAsync(work, WorkIssueCause.ExternalAuthority, evidence, cancellationToken);
        await ResolveAsync(work, WorkIssueCause.Configuration, evidence, cancellationToken);

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

    /// <summary>
    /// Hands the answered files to local Site Recognition. It follows this lane because it exists
    /// for the files the remote ladder could not fully match, and it re-reads the paths of Videos
    /// whose Site is still Unknown whenever it runs again.
    /// </summary>
    protected override Task CompleteAsync(
        BackgroundWorkRow work,
        CancellationToken cancellationToken) =>
        DerivedWorkQueue.QueueAsync(
            Database,
            work.LibraryDirectoryId,
            work.ConfigurationGeneration,
            BackgroundWorkCategory.SiteRecognition,
            BackgroundWorkTrigger.FollowUpWork,
            Now(),
            cancellationToken);

    /// <summary>
    /// Identifies which key prdb refused without disclosing any part of it. A one-way fingerprint
    /// lets an Administrator tell a refused key apart from the replacement they supplied, while the
    /// configuration surface keeps its promise that a stored credential is never shown again.
    /// </summary>
    private static string Masked(string credential)
    {
        var fingerprint = SHA256.HashData(Encoding.UTF8.GetBytes(credential)).AsSpan(0, 4);

        return $"fingerprint {Convert.ToHexString(fingerprint).ToLowerInvariant()}";
    }
}
