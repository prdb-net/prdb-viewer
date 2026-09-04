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

    /// <summary>
    /// Adds or removes an Actor from this Account's own list. It is idempotent, like every other
    /// personal reference: setting what is already set changes nothing and answers the same way.
    /// </summary>
    /// <remarks>
    /// The Actor has to be one this installation knows, so a favourite cannot be filed against an
    /// identifier nothing here has ever heard of. It does not have to have a profile: an Actor
    /// exists here the moment a credit resolves to them.
    /// </remarks>
    public async Task<bool> SetFavouriteActorAsync(
        Guid accountId,
        string prdbActorId,
        bool selected,
        CancellationToken cancellationToken = default)
    {
        if (!await database.Actors.AnyAsync(
                actor => actor.PrdbActorId == prdbActorId,
                cancellationToken))
        {
            return false;
        }

        var held = await database.PersonalActorStates
            .AsTracking()
            .SingleOrDefaultAsync(
                row => row.AccountId == accountId && row.PrdbActorId == prdbActorId,
                cancellationToken);

        if (selected && held is null)
        {
            database.PersonalActorStates.Add(new PersonalActorStateRow
            {
                AccountId = accountId,
                PrdbActorId = prdbActorId,
                FavouriteAddedAt = UtcNow(),
            });
        }
        else if (!selected && held is not null)
        {
            database.PersonalActorStates.Remove(held);
        }

        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

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

    /// <summary>
    /// Preserves both Videos' private viewing history when their identities merge. Play Counts
    /// sum, overlapping Active Watching is counted once, completion history remains true, and the
    /// most recent authoritative activity supplies the current resume state. Nothing about it is
    /// exposed to the Administrator whose decision caused the merge.
    /// </summary>
    internal async Task ReconcileMergedVideoAsync(
        Guid survivingVideoId,
        Guid mergedVideoId,
        CancellationToken cancellationToken = default)
    {
        var merged = await database.PersonalVideoStates
            .AsTracking()
            .Where(state => state.VideoId == mergedVideoId)
            .ToListAsync(cancellationToken);
        var surviving = await database.PersonalVideoStates
            .AsTracking()
            .Where(state => state.VideoId == survivingVideoId)
            .ToListAsync(cancellationToken);

        foreach (var state in merged)
        {
            var target = surviving.SingleOrDefault(candidate => candidate.AccountId == state.AccountId);

            if (target is null)
            {
                database.PersonalVideoStates.Add(new PersonalVideoStateRow
                {
                    AccountId = state.AccountId,
                    VideoId = survivingVideoId,
                    PlaybackProgressMilliseconds = state.PlaybackProgressMilliseconds,
                    ProgressVideoFileId = state.ProgressVideoFileId,
                    AccumulatedWatchDurationMilliseconds = state.AccumulatedWatchDurationMilliseconds,
                    PlayCount = state.PlayCount,
                    HasViewingCompletion = state.HasViewingCompletion,
                    LastCompletedAt = state.LastCompletedAt,
                    PlayState = state.PlayState,
                    PlayStateChangedAt = state.PlayStateChangedAt,
                    LastQualifiedActivityAt = state.LastQualifiedActivityAt,
                    ContinueWatchingDismissedAt = state.ContinueWatchingDismissedAt,
                    FavouriteAddedAt = state.FavouriteAddedAt,
                    WatchLaterAddedAt = state.WatchLaterAddedAt,
                    PersonalRating = state.PersonalRating,
                    UpdatedAt = state.UpdatedAt,
                });
            }
            else
            {
                Combine(target, state);
            }

            database.PersonalVideoStates.Remove(state);
        }

        await database.SaveChangesAsync(cancellationToken);

        foreach (var state in await database.PersonalVideoStates
                     .AsTracking()
                     .Where(candidate => candidate.VideoId == survivingVideoId)
                     .ToListAsync(cancellationToken))
        {
            state.AccumulatedWatchDurationMilliseconds = await GetConfirmedDurationAsync(
                state.AccountId,
                survivingVideoId,
                cancellationToken);
        }

        await database.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Redistributes private viewing state when one Video splits into two. Activity attributable
    /// to a separated Video File follows it and is derived again for both identities, while
    /// ambiguous Video-level state — organisation, ratings, and dismissals — stays with the
    /// explicitly chosen continuing Video. Nothing is exposed to the deciding Administrator.
    /// </summary>
    internal async Task SeparateSplitVideoAsync(
        Guid continuingVideoId,
        Guid separatedVideoId,
        IReadOnlyCollection<Guid> separatedVideoFileIds,
        CancellationToken cancellationToken = default)
    {
        var attempts = await database.PlaybackAttempts
            .AsTracking()
            .Include(attempt => attempt.VideoFiles)
            .Where(attempt => attempt.VideoId == continuingVideoId)
            .ToListAsync(cancellationToken);

        foreach (var attempt in attempts.Where(attempt =>
                     attempt.VideoFiles.Count > 0 &&
                     attempt.VideoFiles.All(participation =>
                         separatedVideoFileIds.Contains(participation.VideoFileId))))
        {
            attempt.VideoId = separatedVideoId;
        }

        await database.SaveChangesAsync(cancellationToken);

        foreach (var accountId in attempts.Select(attempt => attempt.AccountId).Distinct())
        {
            await DeriveStateAsync(accountId, continuingVideoId, cancellationToken);
            await DeriveStateAsync(accountId, separatedVideoId, cancellationToken);
        }

        await database.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Moves the Video-level state that no Video File can claim — organisation, rating, and a
    /// Continue Watching dismissal — to the Video the Administrator chose to carry it.
    /// </summary>
    internal async Task TransferAmbiguousStateAsync(
        Guid fromVideoId,
        Guid toVideoId,
        CancellationToken cancellationToken = default)
    {
        var sources = await database.PersonalVideoStates
            .AsTracking()
            .Where(state => state.VideoId == fromVideoId)
            .ToListAsync(cancellationToken);

        foreach (var source in sources.Where(state =>
                     state.FavouriteAddedAt is not null ||
                     state.WatchLaterAddedAt is not null ||
                     state.PersonalRating is not null ||
                     state.ContinueWatchingDismissedAt is not null))
        {
            var target = await GetOrCreateStateAsync(
                source.AccountId,
                toVideoId,
                source.UpdatedAt,
                cancellationToken);
            target.FavouriteAddedAt = source.FavouriteAddedAt;
            target.WatchLaterAddedAt = source.WatchLaterAddedAt;
            target.PersonalRating = source.PersonalRating;
            target.ContinueWatchingDismissedAt = source.ContinueWatchingDismissedAt;
            source.FavouriteAddedAt = null;
            source.WatchLaterAddedAt = null;
            source.PersonalRating = null;
            source.ContinueWatchingDismissedAt = null;
        }

        await database.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Rebuilds the derived playback facts of one Account and Video from its retained Playback
    /// Attempts. Explicitly maintained references and preferences are never derived.
    /// </summary>
    private async Task DeriveStateAsync(
        Guid accountId,
        Guid videoId,
        CancellationToken cancellationToken)
    {
        var attempts = await database.PlaybackAttempts
            .AsNoTracking()
            .Where(attempt => attempt.AccountId == accountId && attempt.VideoId == videoId)
            .OrderBy(attempt => attempt.AttemptedAt)
            .ToListAsync(cancellationToken);
        var state = await database.PersonalVideoStates
            .AsTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.AccountId == accountId && candidate.VideoId == videoId,
                cancellationToken);

        if (attempts.Count == 0)
        {
            if (state is not null)
            {
                state.PlayCount = 0;
                state.AccumulatedWatchDurationMilliseconds = 0;
                state.PlaybackProgressMilliseconds = null;
                state.ProgressVideoFileId = null;
                state.HasViewingCompletion = false;
                state.PlayState = PersonalPlayState.Unplayed;
                state.PlayStateChangedAt = null;
                state.LastQualifiedActivityAt = null;
            }

            return;
        }

        if (state is null)
        {
            state = new PersonalVideoStateRow
            {
                AccountId = accountId,
                VideoId = videoId,
                PlayState = PersonalPlayState.Unplayed,
                UpdatedAt = attempts[^1].AttemptedAt,
            };
            database.PersonalVideoStates.Add(state);
        }

        var authoritative = attempts
            .Where(attempt => attempt.LastActivityAt is not null)
            .OrderByDescending(attempt => attempt.LastActivityAt)
            .FirstOrDefault();
        state.PlayCount = attempts.Count(attempt => attempt.Qualified);
        state.HasViewingCompletion = attempts.Any(attempt => attempt.CompletionRecorded);
        state.AccumulatedWatchDurationMilliseconds = await GetConfirmedDurationAsync(
            accountId,
            videoId,
            cancellationToken);
        state.PlaybackProgressMilliseconds = authoritative?.LastPositionMilliseconds;
        state.ProgressVideoFileId = authoritative is null
            ? null
            : await database.PlaybackAttemptVideoFiles
                .Where(participation => participation.PlaybackAttemptId == authoritative.Id)
                .Select(participation => (Guid?)participation.VideoFileId)
                .FirstOrDefaultAsync(cancellationToken);
        state.PlayState = authoritative switch
        {
            null => PersonalPlayState.Unplayed,
            { CompletionRecorded: true } => PersonalPlayState.Completed,
            { Qualified: true } => PersonalPlayState.InProgress,
            _ => state.HasViewingCompletion
                ? PersonalPlayState.Completed
                : PersonalPlayState.Unplayed,
        };
        state.PlayStateChangedAt = authoritative?.AttemptedAt;
        state.LastQualifiedActivityAt = state.PlayState == PersonalPlayState.InProgress
            ? authoritative?.LastActivityAt
            : null;
    }

    private static void Combine(PersonalVideoStateRow target, PersonalVideoStateRow merged)
    {
        target.PlayCount += merged.PlayCount;
        target.HasViewingCompletion |= merged.HasViewingCompletion;
        target.LastCompletedAt = Later(target.LastCompletedAt, merged.LastCompletedAt);
        target.ContinueWatchingDismissedAt = Later(
            target.ContinueWatchingDismissedAt,
            merged.ContinueWatchingDismissedAt);
        target.FavouriteAddedAt = Earlier(target.FavouriteAddedAt, merged.FavouriteAddedAt);
        target.WatchLaterAddedAt = Earlier(target.WatchLaterAddedAt, merged.WatchLaterAddedAt);

        if (AuthoritativeAt(merged) > AuthoritativeAt(target))
        {
            target.PlaybackProgressMilliseconds = merged.PlaybackProgressMilliseconds;
            target.ProgressVideoFileId = merged.ProgressVideoFileId;
            target.PlayState = merged.PlayState;
            target.PlayStateChangedAt = merged.PlayStateChangedAt;
            target.LastQualifiedActivityAt = merged.LastQualifiedActivityAt;
            target.PersonalRating = merged.PersonalRating ?? target.PersonalRating;
        }
        else
        {
            target.PersonalRating ??= merged.PersonalRating;
        }

        target.UpdatedAt = Later(target.UpdatedAt, merged.UpdatedAt) ?? target.UpdatedAt;
    }

    private static DateTime AuthoritativeAt(PersonalVideoStateRow state) =>
        state.LastQualifiedActivityAt ?? state.PlayStateChangedAt ?? state.UpdatedAt;

    private static DateTime? Later(DateTime? left, DateTime? right) =>
        left is null || (right is not null && right > left) ? right ?? left : left;

    private static DateTime? Earlier(DateTime? left, DateTime? right) =>
        left is null || (right is not null && right < left) ? right ?? left : left;

    private async Task<long> GetConfirmedDurationAsync(
        Guid accountId,
        Guid videoId,
        CancellationToken cancellationToken)
    {
        var intervals = await database.PlaybackReports
            .AsNoTracking()
            .Where(report =>
                report.PlaybackAttempt.AccountId == accountId &&
                report.PlaybackAttempt.VideoId == videoId &&
                report.ActivityStartedAt != null &&
                report.ActivityEndedAt != null)
            .Select(report => new ActivityInterval(
                report.ActivityStartedAt!.Value,
                report.ActivityEndedAt!.Value))
            .ToListAsync(cancellationToken);

        return UnionLengthMilliseconds(intervals);
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
