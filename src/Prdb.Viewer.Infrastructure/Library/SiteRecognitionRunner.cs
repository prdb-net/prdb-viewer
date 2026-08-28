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
    /// The files prdb has already answered about and whose current path has not been read yet.
    /// Waiting for that answer is what makes this local Site Recognition for files that did not
    /// receive a full prdb match, rather than a race against the remote ladder.
    /// </summary>
    protected override IQueryable<VideoFileRow> Outstanding(Guid libraryDirectoryId) =>
        Database.VideoFiles.Where(file =>
            file.LibraryDirectoryId == libraryDirectoryId &&
            file.Availability == VideoFileAvailability.Available &&
            file.IdentifiedSha256 != null &&
            file.IdentifiedSha256 == file.Sha256 &&
            (file.SiteRecognisedPath == null || file.SiteRecognisedPath != file.RelativePath));

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

    protected override async Task AdvanceAsync(
        BackgroundWorkRow work,
        IReadOnlyList<VideoFileRow> files,
        CancellationToken cancellationToken)
    {
        await RefreshDirectoryAsync(work, cancellationToken);

        var vocabulary = await directory.ReadAsync(cancellationToken);

        foreach (var file in files)
        {
            await identification.ApplyLocalSiteRecognitionAsync(
                file.Id,
                vocabulary.Recognise(file.RelativePath),
                cancellationToken);
        }

        work.CompletedItemCount += files.Count;
    }

    /// <summary>
    /// Refreshes the vocabulary when the retained copy is a day old, and reports only the case an
    /// Administrator can act on: nothing was ever fetched, so no site can be recognised at all. A
    /// stale copy that still recognises sites is not an obstacle and is not reported as one.
    /// </summary>
    private async Task RefreshDirectoryAsync(
        BackgroundWorkRow work,
        CancellationToken cancellationToken)
    {
        // At most one request per run: a successful refresh then lasts a day, and an unsuccessful
        // one is retried by the next run rather than by every batch of files inside this one.
        if (work.CompletedItemCount > 0 || !await directory.IsStaleAsync(cancellationToken))
        {
            return;
        }

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

            return;
        }

        if (!await directory.IsEmptyAsync(cancellationToken))
        {
            return;
        }

        await ReportAsync(work, Unfetched(refresh), cancellationToken);
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
