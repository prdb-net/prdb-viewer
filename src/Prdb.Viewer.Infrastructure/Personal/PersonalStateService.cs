using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Core.Personal;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Personal;

public sealed class PersonalStateService(
    ViewerDbContext database,
    TimeProvider timeProvider)
{
    public async Task<PlaybackAttemptResult> StartPlaybackAttemptAsync(
        Guid accountId,
        Guid videoId,
        Guid videoFileId,
        CancellationToken cancellationToken = default)
    {
        var videoExists = await database.Videos
            .AnyAsync(video => video.Id == videoId, cancellationToken);
        if (!videoExists)
        {
            return new PlaybackAttemptResult(PlaybackAttemptVerdict.VideoNotFound, null, null);
        }

        var file = await database.VideoFiles
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate =>
                candidate.Id == videoFileId &&
                candidate.VideoId == videoId &&
                candidate.Availability == VideoFileAvailability.Available,
                cancellationToken);
        if (file is null)
        {
            return new PlaybackAttemptResult(
                PlaybackAttemptVerdict.VideoFileUnavailable,
                null,
                null);
        }

        var state = await database.PersonalVideoStates
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate =>
                candidate.AccountId == accountId && candidate.VideoId == videoId,
                cancellationToken);
        var resumePosition = state?.ProgressVideoFileId == videoFileId &&
            PlaybackActivityRule.IsMeaningfulResumePosition(
                file.DurationMilliseconds,
                state.PlaybackProgressMilliseconds)
            ? state.PlaybackProgressMilliseconds
            : null;
        var now = UtcNow();
        var attempt = new PlaybackAttemptRow
        {
            Id = Guid.CreateVersion7(),
            AccountId = accountId,
            VideoId = videoId,
            AttemptedAt = now,
        };
        database.PlaybackAttempts.Add(attempt);
        await database.SaveChangesAsync(cancellationToken);

        return new PlaybackAttemptResult(
            PlaybackAttemptVerdict.Started,
            attempt.Id,
            resumePosition);
    }

    public async Task<PlaybackReportResult> ReportPlaybackAsync(
        Guid accountId,
        Guid playbackAttemptId,
        Guid reportId,
        int sequence,
        Guid videoFileId,
        long positionMilliseconds,
        long activeWatchingMilliseconds,
        bool naturalEndConfirmed,
        bool endSession,
        CancellationToken cancellationToken = default)
    {
        var invalid = reportId == Guid.Empty ||
            sequence < 0 ||
            positionMilliseconds < 0 ||
            activeWatchingMilliseconds < 0 ||
            activeWatchingMilliseconds > PlaybackActivityRule.MaximumReportDurationMilliseconds;
        if (invalid)
        {
            return new PlaybackReportResult(PlaybackReportVerdict.InvalidReport, null);
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var attempt = await database.PlaybackAttempts
            .AsTracking()
            .SingleOrDefaultAsync(candidate =>
                candidate.Id == playbackAttemptId && candidate.AccountId == accountId,
                cancellationToken);
        if (attempt is null)
        {
            return new PlaybackReportResult(PlaybackReportVerdict.NotFound, null);
        }

        if (await database.PlaybackReports.AnyAsync(report => report.Id == reportId, cancellationToken))
        {
            return new PlaybackReportResult(
                PlaybackReportVerdict.Duplicate,
                await GetSummaryAsync(accountId, attempt.VideoId, cancellationToken));
        }

        var file = await database.VideoFiles
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate =>
                candidate.Id == videoFileId && candidate.VideoId == attempt.VideoId,
                cancellationToken);
        if (file is null || positionMilliseconds > Math.Max(0, file.DurationMilliseconds) + 1_000)
        {
            return new PlaybackReportResult(PlaybackReportVerdict.InvalidReport, null);
        }

        var now = UtcNow();
        var inactivityCutoff = now - PlaybackActivityRule.SessionInactivityTimeout;
        var latestEvidenceAt = attempt.LastActivityAt ?? attempt.AttemptedAt;
        if (attempt.EndedAt is not null || latestEvidenceAt < inactivityCutoff)
        {
            if (attempt.EndedAt is null)
            {
                attempt.EndedAt = latestEvidenceAt + PlaybackActivityRule.SessionInactivityTimeout;
                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            return new PlaybackReportResult(
                PlaybackReportVerdict.AttemptEnded,
                await GetSummaryAsync(accountId, attempt.VideoId, cancellationToken));
        }

        var state = await GetOrCreateStateAsync(accountId, attempt.VideoId, now, cancellationToken);
        DateTime? activityStartedAt = null;
        DateTime? activityEndedAt = null;
        if (activeWatchingMilliseconds > 0)
        {
            activityEndedAt = now;
            activityStartedAt = now.AddMilliseconds(-activeWatchingMilliseconds);
            state.AccumulatedWatchDurationMilliseconds += await GetUncoveredDurationAsync(
                accountId,
                attempt.VideoId,
                activityStartedAt.Value,
                activityEndedAt.Value,
                cancellationToken);
            attempt.ActiveWatchDurationMilliseconds += activeWatchingMilliseconds;
            attempt.ViewingSessionBeganAt ??= activityStartedAt;
            attempt.LastActivityAt = now;

            if (!await database.PlaybackAttemptVideoFiles.AnyAsync(participation =>
                    participation.PlaybackAttemptId == attempt.Id &&
                    participation.VideoFileId == videoFileId,
                    cancellationToken))
            {
                database.PlaybackAttemptVideoFiles.Add(new PlaybackAttemptVideoFileRow
                {
                    PlaybackAttemptId = attempt.Id,
                    VideoFileId = videoFileId,
                });
            }
        }

        var reportIsLatestForSession = sequence > attempt.LastReportSequence;
        if (reportIsLatestForSession)
        {
            attempt.LastReportSequence = sequence;
            attempt.LastPositionMilliseconds = positionMilliseconds;
        }

        var justQualified = !attempt.Qualified && PlaybackActivityRule.Qualifies(
            file.DurationMilliseconds,
            attempt.ActiveWatchDurationMilliseconds,
            naturalEndConfirmed && activeWatchingMilliseconds > 0);
        if (justQualified)
        {
            attempt.Qualified = true;
            state.PlayCount++;
        }

        var establishesCompletion = !attempt.CompletionRecorded &&
            PlaybackActivityRule.EstablishesCompletion(
                file.DurationMilliseconds,
                positionMilliseconds,
                activeWatchingMilliseconds,
                naturalEndConfirmed);
        if (establishesCompletion)
        {
            attempt.CompletionRecorded = true;
            state.HasViewingCompletion = true;
            state.LastCompletedAt = now;
            if (state.PlayStateChangedAt is null || state.PlayStateChangedAt <= attempt.AttemptedAt)
            {
                state.PlayState = PersonalPlayState.Completed;
                state.PlayStateChangedAt = attempt.AttemptedAt;
                state.LastQualifiedActivityAt = null;
            }
        }
        else if (!attempt.CompletionRecorded && attempt.Qualified && activeWatchingMilliseconds > 0 &&
                 (state.PlayStateChangedAt is null || state.PlayStateChangedAt <= attempt.AttemptedAt))
        {
            state.PlayState = PersonalPlayState.InProgress;
            state.PlayStateChangedAt = attempt.AttemptedAt;
            state.LastQualifiedActivityAt = now;
        }

        if (activeWatchingMilliseconds > 0 && reportIsLatestForSession &&
            !await HasNewerActiveSessionAsync(
                accountId,
                attempt.VideoId,
                attempt,
                inactivityCutoff,
                cancellationToken))
        {
            state.PlaybackProgressMilliseconds = Math.Min(
                positionMilliseconds,
                Math.Max(0, file.DurationMilliseconds));
            state.ProgressVideoFileId = videoFileId;
        }

        if (endSession)
        {
            attempt.EndedAt = now;
        }

        state.UpdatedAt = now;
        database.PlaybackReports.Add(new PlaybackReportRow
        {
            Id = reportId,
            PlaybackAttemptId = attempt.Id,
            Sequence = sequence,
            PositionMilliseconds = positionMilliseconds,
            ActiveWatchingMilliseconds = activeWatchingMilliseconds,
            ActivityStartedAt = activityStartedAt,
            ActivityEndedAt = activityEndedAt,
            ReceivedAt = now,
        });
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new PlaybackReportResult(
            PlaybackReportVerdict.Accepted,
            ToSummary(state, file.DurationMilliseconds));
    }

    public async Task<bool> EndPlaybackAttemptAsync(
        Guid accountId,
        Guid playbackAttemptId,
        CancellationToken cancellationToken = default)
    {
        var attempt = await database.PlaybackAttempts
            .AsTracking()
            .SingleOrDefaultAsync(candidate =>
                candidate.Id == playbackAttemptId && candidate.AccountId == accountId,
                cancellationToken);
        if (attempt is null)
        {
            return false;
        }

        attempt.EndedAt ??= UtcNow();
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task EndAccountPlaybackAttemptsAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var now = UtcNow();
        await database.PlaybackAttempts
            .Where(attempt => attempt.AccountId == accountId && attempt.EndedAt == null)
            .ExecuteUpdateAsync(
                updates => updates.SetProperty(attempt => attempt.EndedAt, now),
                cancellationToken);
    }

    public Task<PersonalStateMutationResult> SetFavouriteAsync(
        Guid accountId,
        Guid videoId,
        bool selected,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            accountId,
            videoId,
            state => state.FavouriteAddedAt = selected ? state.FavouriteAddedAt ?? UtcNow() : null,
            cancellationToken);

    public Task<PersonalStateMutationResult> SetWatchLaterAsync(
        Guid accountId,
        Guid videoId,
        bool selected,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            accountId,
            videoId,
            state => state.WatchLaterAddedAt = selected ? state.WatchLaterAddedAt ?? UtcNow() : null,
            cancellationToken);

    public Task<PersonalStateMutationResult> DismissContinueWatchingAsync(
        Guid accountId,
        Guid videoId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            accountId,
            videoId,
            state => state.ContinueWatchingDismissedAt = UtcNow(),
            cancellationToken);

    public async Task<PersonalStateMutationResult> SetRatingAsync(
        Guid accountId,
        Guid videoId,
        int? rating,
        CancellationToken cancellationToken = default)
    {
        if (rating is not null && !PersonalRatingRule.IsValid(rating.Value))
        {
            return new PersonalStateMutationResult(
                PersonalStateMutationVerdict.InvalidRating,
                null);
        }

        return await MutateAsync(
            accountId,
            videoId,
            state => state.PersonalRating = rating,
            cancellationToken);
    }

    public async Task<PersonalVideoStateSummary> GetSummaryAsync(
        Guid accountId,
        Guid videoId,
        CancellationToken cancellationToken = default)
    {
        var state = await database.PersonalVideoStates
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate =>
                candidate.AccountId == accountId && candidate.VideoId == videoId,
                cancellationToken);
        if (state is null)
        {
            return EmptySummary();
        }

        var duration = state.ProgressVideoFileId is null
            ? 0
            : await database.VideoFiles
                .Where(file => file.Id == state.ProgressVideoFileId)
                .Select(file => file.DurationMilliseconds)
                .SingleOrDefaultAsync(cancellationToken);
        return ToSummary(state, duration);
    }

    private async Task<PersonalStateMutationResult> MutateAsync(
        Guid accountId,
        Guid videoId,
        Action<PersonalVideoStateRow> mutation,
        CancellationToken cancellationToken)
    {
        if (!await database.Videos.AnyAsync(video => video.Id == videoId, cancellationToken))
        {
            return new PersonalStateMutationResult(
                PersonalStateMutationVerdict.VideoNotFound,
                null);
        }

        var now = UtcNow();
        var state = await GetOrCreateStateAsync(accountId, videoId, now, cancellationToken);
        mutation(state);
        state.UpdatedAt = now;
        await database.SaveChangesAsync(cancellationToken);
        return new PersonalStateMutationResult(
            PersonalStateMutationVerdict.Updated,
            await GetSummaryAsync(accountId, videoId, cancellationToken));
    }

    private async Task<PersonalVideoStateRow> GetOrCreateStateAsync(
        Guid accountId,
        Guid videoId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var state = await database.PersonalVideoStates
            .AsTracking()
            .SingleOrDefaultAsync(candidate =>
                candidate.AccountId == accountId && candidate.VideoId == videoId,
                cancellationToken);
        if (state is not null)
        {
            return state;
        }

        state = new PersonalVideoStateRow
        {
            AccountId = accountId,
            VideoId = videoId,
            PlayState = PersonalPlayState.Unplayed,
            UpdatedAt = now,
        };
        database.PersonalVideoStates.Add(state);
        return state;
    }

    private async Task<bool> HasNewerActiveSessionAsync(
        Guid accountId,
        Guid videoId,
        PlaybackAttemptRow current,
        DateTime inactivityCutoff,
        CancellationToken cancellationToken) =>
        await database.PlaybackAttempts.AnyAsync(candidate =>
            candidate.AccountId == accountId &&
            candidate.VideoId == videoId &&
            candidate.Id != current.Id &&
            candidate.ViewingSessionBeganAt != null &&
            candidate.EndedAt == null &&
            candidate.LastActivityAt >= inactivityCutoff &&
            (candidate.AttemptedAt > current.AttemptedAt ||
             (candidate.AttemptedAt == current.AttemptedAt && candidate.Id.CompareTo(current.Id) > 0)),
            cancellationToken);

    private async Task<long> GetUncoveredDurationAsync(
        Guid accountId,
        Guid videoId,
        DateTime start,
        DateTime end,
        CancellationToken cancellationToken)
    {
        var existing = await database.PlaybackReports
            .AsNoTracking()
            .Where(report =>
                report.PlaybackAttempt.AccountId == accountId &&
                report.PlaybackAttempt.VideoId == videoId &&
                report.ActivityStartedAt != null &&
                report.ActivityEndedAt != null &&
                report.ActivityStartedAt < end &&
                report.ActivityEndedAt > start)
            .Select(report => new ActivityInterval(
                report.ActivityStartedAt!.Value,
                report.ActivityEndedAt!.Value))
            .ToListAsync(cancellationToken);
        var priorLength = UnionLengthMilliseconds(existing);
        existing.Add(new ActivityInterval(start, end));
        return UnionLengthMilliseconds(existing) - priorLength;
    }

    private static long UnionLengthMilliseconds(List<ActivityInterval> intervals)
    {
        if (intervals.Count == 0)
        {
            return 0;
        }

        intervals.Sort((left, right) => left.Start.CompareTo(right.Start));
        var totalTicks = 0L;
        var currentStart = intervals[0].Start;
        var currentEnd = intervals[0].End;
        foreach (var interval in intervals.Skip(1))
        {
            if (interval.Start <= currentEnd)
            {
                if (interval.End > currentEnd)
                {
                    currentEnd = interval.End;
                }
            }
            else
            {
                totalTicks += (currentEnd - currentStart).Ticks;
                currentStart = interval.Start;
                currentEnd = interval.End;
            }
        }

        totalTicks += (currentEnd - currentStart).Ticks;
        return totalTicks / TimeSpan.TicksPerMillisecond;
    }

    internal static PersonalVideoStateSummary ToSummary(
        PersonalVideoStateRow state,
        long progressDurationMilliseconds)
    {
        var continueWatching = state.PlayState == PersonalPlayState.InProgress &&
            PlaybackActivityRule.IsMeaningfulResumePosition(
                progressDurationMilliseconds,
                state.PlaybackProgressMilliseconds) &&
            state.LastQualifiedActivityAt is not null &&
            (state.ContinueWatchingDismissedAt is null ||
             state.LastQualifiedActivityAt > state.ContinueWatchingDismissedAt);
        return new PersonalVideoStateSummary(
            state.PlaybackProgressMilliseconds,
            state.AccumulatedWatchDurationMilliseconds,
            state.PlayCount,
            state.HasViewingCompletion,
            state.PlayState,
            continueWatching,
            state.FavouriteAddedAt is not null,
            state.WatchLaterAddedAt is not null,
            state.PersonalRating);
    }

    internal static PersonalVideoStateSummary EmptySummary() =>
        new(null, 0, 0, false, PersonalPlayState.Unplayed, false, false, false, null);

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private sealed record ActivityInterval(DateTime Start, DateTime End);
}
