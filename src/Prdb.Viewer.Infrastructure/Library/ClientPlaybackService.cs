using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// Holds what one Account's client knows about playing this library: the media configurations it
/// has assessed, and what happened when it actually played a file.
///
/// Everything here is Personal State. It is scoped to the Account and the client context that
/// produced it, influences no other Account's results, and is never exposed as activity to an
/// Administrator.
/// </summary>
public sealed class ClientPlaybackService(ViewerDbContext database, TimeProvider timeProvider)
{
    /// <summary>
    /// How many configurations one round of client qualification is asked about. A library asks a
    /// handful of distinct questions however many files it holds, and this keeps even a pathological
    /// one from turning startup into a survey.
    /// </summary>
    private const int ProfileLimit = 200;

    /// <summary>
    /// The media configurations this client has not answered for yet. It asks about the whole
    /// library rather than the current page, because a configuration this client cannot play is
    /// exactly what keeps a Video out of ordinary results — a client that only ever qualified what
    /// it could already see would never learn about the rest.
    /// </summary>
    public async Task<IReadOnlyList<UnassessedPlaybackProfile>> UnassessedProfilesAsync(
        Guid accountId,
        string clientContextKey,
        CancellationToken cancellationToken = default)
    {
        var answered = database.ClientPlaybackAssessments
            .Where(assessment => assessment.AccountId == accountId &&
                                 assessment.ClientContextKey == clientContextKey)
            .Select(assessment => assessment.ProfileKey);
        var files = await database.VideoFiles
            .AsNoTracking()
            .Where(file => file.Availability == VideoFileAvailability.Available &&
                           file.DirectPlayClassification != DirectPlayClassification.Unsupported &&
                           file.ProfileKey != "" &&
                           !answered.Contains(file.ProfileKey))
            .GroupBy(file => file.ProfileKey)
            .Select(group => group.OrderByDescending(file => file.InspectedAt).First())
            .Take(ProfileLimit)
            .ToListAsync(cancellationToken);

        return files.Select(Profile).ToArray();
    }

    /// <summary>
    /// Records what the client concluded. An answer replaces the previous one for the same
    /// configuration, because the client's own current answer is the only one worth keeping.
    /// </summary>
    public async Task<int> RecordAssessmentsAsync(
        Guid accountId,
        string clientContextKey,
        IReadOnlyList<ClientPlaybackAssessmentReport> reports,
        CancellationToken cancellationToken = default)
    {
        if (reports.Count == 0)
        {
            return 0;
        }

        var now = Now();
        var keys = reports.Select(report => report.ProfileKey).Distinct().ToArray();
        var existing = await database.ClientPlaybackAssessments
            .AsTracking()
            .Where(assessment => assessment.AccountId == accountId &&
                                 assessment.ClientContextKey == clientContextKey &&
                                 keys.Contains(assessment.ProfileKey))
            .ToDictionaryAsync(assessment => assessment.ProfileKey, cancellationToken);

        foreach (var report in reports.DistinctBy(report => report.ProfileKey))
        {
            if (existing.TryGetValue(report.ProfileKey, out var assessment))
            {
                assessment.Verdict = report.Verdict;
                assessment.Smooth = report.Smooth;
                assessment.PowerEfficient = report.PowerEfficient;
                assessment.Method = report.Method;
                assessment.AssessedAt = now;
                continue;
            }

            database.ClientPlaybackAssessments.Add(new ClientPlaybackAssessmentRow
            {
                AccountId = accountId,
                ClientContextKey = clientContextKey,
                ProfileKey = report.ProfileKey,
                Verdict = report.Verdict,
                Smooth = report.Smooth,
                PowerEfficient = report.PowerEfficient,
                Method = report.Method,
                AssessedAt = now,
            });
        }

        await database.SaveChangesAsync(cancellationToken);
        return keys.Length;
    }

    /// <summary>
    /// Records what happened when this client played a Video File.
    ///
    /// Only a media failure is evidence about the file: a delivery or network failure is the
    /// installation's problem and an availability failure is the library's, and neither may quietly
    /// turn into "this browser cannot play it". Those are retained as nothing at all, so a broken
    /// reverse proxy cannot empty a library one variant at a time.
    /// </summary>
    public async Task<bool> RecordOutcomeAsync(
        Guid accountId,
        string clientContextKey,
        Guid videoFileId,
        ObservedPlaybackOutcome outcome,
        PlaybackFailureCategory? failureCategory,
        CancellationToken cancellationToken = default)
    {
        var file = await database.VideoFiles
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == videoFileId, cancellationToken);

        if (file is null)
        {
            return false;
        }

        if (outcome == ObservedPlaybackOutcome.Failed &&
            failureCategory != PlaybackFailureCategory.Media)
        {
            return false;
        }

        var existing = await database.ObservedPlaybackOutcomes
            .AsTracking()
            .SingleOrDefaultAsync(
                row => row.AccountId == accountId &&
                       row.ClientContextKey == clientContextKey &&
                       row.VideoFileId == videoFileId,
                cancellationToken);

        if (existing is null)
        {
            database.ObservedPlaybackOutcomes.Add(new ObservedPlaybackOutcomeRow
            {
                AccountId = accountId,
                ClientContextKey = clientContextKey,
                VideoFileId = videoFileId,
                ContentSha256 = file.Sha256,
                Outcome = outcome,
                FailureCategory = outcome == ObservedPlaybackOutcome.Failed ? failureCategory : null,
                ObservedAt = Now(),
            });
        }
        else
        {
            existing.ContentSha256 = file.Sha256;
            existing.Outcome = outcome;
            existing.FailureCategory = outcome == ObservedPlaybackOutcome.Failed
                ? failureCategory
                : null;
            existing.ObservedAt = Now();
        }

        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Forgets what this client observed about one Video, so an explicit retry is not answered by a
    /// remembered failure. The Client Playback Assessments stay: the client's reading of the
    /// configuration is not what the User is disputing.
    /// </summary>
    public async Task<int> ForgetOutcomesAsync(
        Guid accountId,
        string clientContextKey,
        Guid videoId,
        CancellationToken cancellationToken = default) =>
        await database.ObservedPlaybackOutcomes
            .Where(outcome => outcome.AccountId == accountId &&
                              outcome.ClientContextKey == clientContextKey &&
                              database.VideoFiles.Any(file =>
                                  file.Id == outcome.VideoFileId && file.VideoId == videoId))
            .ExecuteDeleteAsync(cancellationToken);

    private static UnassessedPlaybackProfile Profile(VideoFileRow file)
    {
        var media = file.Media;

        return new UnassessedPlaybackProfile(
            file.ProfileKey,
            PlaybackProfileRule.PreciseVideoContentType(media),
            PlaybackProfileRule.PreciseAudioContentType(media),
            PlaybackProfileRule.BasicContentType(media),
            file.Width,
            file.Height,
            file.FrameRate,
            file.VideoBitrate,
            file.AudioChannels,
            file.AudioSampleRate,
            file.AudioBitrate);
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
}
