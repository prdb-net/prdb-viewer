using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// Recognises the site of Video Files the remote ladder could not fully match, from the files' own
/// paths and the retained Site Directory. It reads no content and contacts no service for a
/// decision, so it keeps working while prdb is unreachable; the only request it makes is the daily
/// refresh of the vocabulary it reads against.
/// </summary>
public sealed class SiteRecognitionRunner(
    ViewerDbContext database,
    SiteDirectory directory,
    IdentificationService identification,
    WorkIssueRecorder issues,
    TimeProvider timeProvider) : VideoFileWorkRunner(database, issues, timeProvider)
{
    private const string DirectoryScope = "prdb.net site directory";

    protected override BackgroundWorkCategory Category => BackgroundWorkCategory.SiteRecognition;

    protected override string Phase => BackgroundWorkPhases.RecognisingSites;

    protected override int BatchSize => 25;

    /// <summary>
    /// The files prdb has already answered about, whose Video has no Site of its own yet, and
    /// whose current path has not been read yet. Waiting for that answer is what makes this local
    /// Site Recognition for files that did not receive a full prdb match, rather than a race
    /// against the remote ladder — and skipping a Video whose Site is Established is what keeps it
    /// to files whose question is still open. Reading the path of a Video prdb has already placed
    /// answers a question nobody asked, and a path that names a second known site along the way —
    /// a work title that happens to be a site's name, say — turns that into a review nobody can
    /// resolve: the Site came with the Work Identification, so the review refuses every decision
    /// but rejection. The same condition already governs
    /// <see cref="RetryEarlierFailuresAsync"/>; it belongs here too.
    /// </summary>
    protected override IQueryable<VideoFileRow> Outstanding(Guid libraryDirectoryId) =>
        Database.VideoFiles.Where(file =>
            file.LibraryDirectoryId == libraryDirectoryId &&
            file.Availability == VideoFileAvailability.Available &&
            file.IdentifiedSha256 != null &&
            file.IdentifiedSha256 == file.Sha256 &&
            ((!Database.IdentificationClaims.Any(claim =>
                claim.VideoId == file.VideoId &&
                claim.Dimension == IdentificationDimension.SiteRecognition &&
                claim.Status == IdentificationClaimStatus.Current) &&
              (file.SiteRecognisedPath == null || file.SiteRecognisedPath != file.RelativePath)) ||
             Database.IdentificationClaims.Any(claim => claim.VideoId == file.VideoId &&
                claim.Dimension == IdentificationDimension.SiteRecognition &&
                claim.Status == IdentificationClaimStatus.Current &&
                claim.Source == IdentificationSource.LocalInference && !claim.IsAdministrativeOverride &&
                claim.SiteRecognitionPolicyVersion == 0)));

    /// <summary>
    /// A new run reads the paths of Videos whose Site is still Unknown again, because the Site
    /// Directory may have learned about them since. A Video whose Site is Established is left
    /// alone: its path has already said what it had to say.
    /// </summary>
    protected override Task RetryEarlierFailuresAsync(
        Guid libraryDirectoryId,
        CancellationToken cancellationToken) =>
        Database.VideoFiles
            .Where(file => file.LibraryDirectoryId == libraryDirectoryId &&
                           file.Availability == VideoFileAvailability.Available &&
                           file.SiteRecognisedPath != null &&
                           !Database.IdentificationClaims.Any(claim =>
                               claim.VideoId == file.VideoId &&
                               claim.Dimension == IdentificationDimension.SiteRecognition &&
                               claim.Status == IdentificationClaimStatus.Current))
            .ExecuteUpdateAsync(
                update => update.SetProperty(file => file.SiteRecognisedPath, (string?)null),
                cancellationToken);

    /// <summary>
    /// Hands the Library Directory on to Enrichment, which asks prdb again about the works this
    /// pass has now finished placing. It is last in the chain because it is the only lane that
    /// wants every Video settled before it asks about them.
    /// </summary>
    protected override Task CompleteAsync(
        BackgroundWorkRow work,
        CancellationToken cancellationToken) =>
        DerivedWorkQueue.QueueAsync(
            Database,
            work.LibraryDirectoryId,
            work.ConfigurationGeneration,
            BackgroundWorkCategory.Enrichment,
            BackgroundWorkTrigger.FollowUpWork,
            Now(),
            cancellationToken);

    protected override async Task AdvanceAsync(
        BackgroundWorkRow work,
        IReadOnlyList<VideoFileRow> files,
        CancellationToken cancellationToken)
    {
        var vocabulary = await directory.ReadAsync(cancellationToken);

        foreach (var file in files)
        {
            await identification.ReviewLegacySiteRecognitionAsync(file.VideoId, vocabulary, cancellationToken);
            if (await Database.IdentificationClaims.AnyAsync(claim => claim.VideoId == file.VideoId &&
                claim.Dimension == IdentificationDimension.SiteRecognition &&
                claim.Status == IdentificationClaimStatus.Current, cancellationToken)) continue;
            Count(
                work,
                await identification.ApplyLocalSiteRecognitionAsync(
                    file.Id,
                    vocabulary.Recognise(file.RelativePath),
                    cancellationToken));
        }

        work.CompletedItemCount += files.Count;
    }

    /// <summary>
    /// Refreshes the vocabulary when the retained copy is a day old, and reports only the case an
    /// Administrator can act on: nothing was ever fetched, so no site can be recognised at all. A
    /// stale copy that still recognises sites is not an obstacle and is not reported as one.
    /// </summary>
    protected override async Task<bool> BeforeSliceAsync(
        BackgroundWorkRow work,
        CancellationToken cancellationToken)
    {
        // At most one request per run: a successful refresh then lasts a day, and an unsuccessful
        // one is retried by the next run rather than by every batch of files inside this one.
        if (!await directory.IsStaleAsync(cancellationToken)) return true;

        var refresh = await directory.RefreshAsync(cancellationToken);

        if (refresh.Verdict == SiteDirectoryRefreshVerdict.Refreshed)
        {
            foreach (var cause in new[]
            {
                WorkIssueCause.Configuration,
                WorkIssueCause.ExternalAuthority,
                WorkIssueCause.ExternalAvailability,
            })
            {
                await ResolveAsync(
                    work,
                    cause,
                    "prdb answered with the list of sites.",
                    cancellationToken);
            }

            work.Phase = Phase;
            return true;
        }

        if (refresh.Verdict == SiteDirectoryRefreshVerdict.Pending)
        {
            work.Phase = refresh.Detail ?? "Building the Site Directory baseline";
            if (refresh.RetryAfter is { } retry) await WaitAsync(work, "prdb deferred the Site feed.", retry, cancellationToken);
            return false;
        }

        if (!await directory.IsEmptyAsync(cancellationToken) ||
            !(await directory.ReadAsync(cancellationToken)).IsEmpty)
        {
            // Recognition remains available from the retained copy while this feed is deferred.
            return true;
        }

        await ReportAsync(work, Unfetched(refresh), cancellationToken);
        if (refresh.Verdict == SiteDirectoryRefreshVerdict.Unreachable)
            await WaitAsync(work, "prdb Site Directory is temporarily unavailable.",
                refresh.RetryAfter ?? TimeSpan.FromMinutes(5), cancellationToken);
        else await HoldAsync(work, "A verified prdb API key is required for the Site Directory.", cancellationToken);
        return false;
    }

    private WorkIssueReport Unfetched(SiteDirectoryRefreshResult refresh) =>
        new(Cause(refresh.Verdict),
            WorkIssueSeverity.ScopedIssue,
            refresh.Verdict == SiteDirectoryRefreshVerdict.Unreachable
                ? WorkIssueRetryDisposition.AutomaticRetryScheduled
                : WorkIssueRetryDisposition.NoAutomaticRetry,
            DirectoryScope,
            DirectoryScope,
            Phase,
            WorkIssueMessages.SiteDirectoryMissing(),
            "Sites are recognised locally by reading a Video File's path against the list of " +
            "sites prdb publishes, and this installation has never been able to fetch that " +
            "list. Identification, previews, browsing, and playback are unaffected; Videos that " +
            "prdb cannot match simply keep an Unknown Site.",
            "Videos without a prdb match keep an Unknown Site.",
            refresh.Verdict == SiteDirectoryRefreshVerdict.Unreachable
                ? "No action is required; the list is fetched again with the next run."
                : "Verify a prdb API key in Installation Configuration.",
            refresh.Detail ?? "The installation holds no active prdb credential.",
            "A successful request for the prdb list of sites.")
        {
            AggregatesItems = false,
        };

    private static WorkIssueCause Cause(SiteDirectoryRefreshVerdict verdict) =>
        verdict switch
        {
            SiteDirectoryRefreshVerdict.Refused => WorkIssueCause.ExternalAuthority,
            SiteDirectoryRefreshVerdict.Unreachable => WorkIssueCause.ExternalAvailability,
            _ => WorkIssueCause.Configuration,
        };
}
