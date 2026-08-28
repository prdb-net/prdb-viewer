using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;
using Prdb.Viewer.Infrastructure.Personal;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// The Administrator's review of open identification work: a queue of what needs a decision, one
/// focused case per Video, and decisions that are previewed, bound to the version they were shown,
/// and recorded with their attribution. Ordinary Users never reach any of it.
/// </summary>
public sealed class IdentificationReviewService(
    ViewerDbContext database,
    IdentificationService identification,
    VideoProjection projection,
    PersonalStateService personalState,
    TimeProvider timeProvider)
{
    private sealed record ApplyOutcome(VideoRow Subject, string? ResultingState);

    public async Task<IReadOnlyList<IdentificationQueueItem>> GetQueueAsync(
        CancellationToken cancellationToken = default)
    {
        var videos = await Query()
            .Where(video => video.SurvivingVideoId == null &&
                            video.IdentificationCandidates.Any(candidate =>
                                candidate.Status == IdentificationCandidateStatus.Pending))
            .ToListAsync(cancellationToken);

        return videos
            .SelectMany(video => video.IdentificationCandidates
                .DistinctBy(candidate => candidate.Id)
                .Where(candidate => candidate.Status == IdentificationCandidateStatus.Pending)
                .Select(candidate => Item(video, candidate)))
            .OrderByDescending(item => item.Candidate.EvidenceClass)
            .ThenBy(item => item.Candidate.CreatedAt)
            .ToArray();
    }

    public async Task<IdentificationCase?> GetCaseAsync(
        Guid videoId,
        CancellationToken cancellationToken = default)
    {
        var video = await Query()
            .SingleOrDefaultAsync(candidate => candidate.Id == videoId, cancellationToken);

        return video is null ? null : await CaseOfAsync(video, cancellationToken);
    }

    public async Task<IdentificationDecisionResult> DecideAsync(
        Guid accountId,
        Guid videoId,
        IdentificationDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await database.Database
            .BeginTransactionAsync(cancellationToken);
        var video = await Query()
            .AsTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == videoId && candidate.SurvivingVideoId == null,
                cancellationToken);

        if (video is null)
        {
            return new IdentificationDecisionResult(IdentificationDecisionVerdict.NotFound);
        }

        if (request.CaseVersion != video.CaseVersion)
        {
            return new IdentificationDecisionResult(
                IdentificationDecisionVerdict.Stale,
                Case: await CaseOfAsync(video, cancellationToken));
        }

        var candidate = request.CandidateId is null
            ? null
            : video.IdentificationCandidates.SingleOrDefault(row =>
                row.Id == request.CandidateId &&
                row.Dimension == request.Dimension &&
                row.Status == IdentificationCandidateStatus.Pending);
        var current = IdentificationService.Current(video, request.Dimension);

        if (request.Dimension == IdentificationDimension.SiteRecognition &&
            UnavailableSiteActions(video).Contains(request.Action))
        {
            return new IdentificationDecisionResult(
                IdentificationDecisionVerdict.ActionUnavailable,
                Case: await CaseOfAsync(video, cancellationToken));
        }

        var target = request.Action switch
        {
            IdentificationDecisionAction.AcceptCandidate => candidate is null
                ? null
                : new IdentificationService.Target(
                    candidate.TargetKey,
                    candidate.TargetTitle,
                    candidate.TargetUrl),
            IdentificationDecisionAction.AssignDirectly or
                IdentificationDecisionAction.ReplaceClaim =>
                string.IsNullOrWhiteSpace(request.TargetKey) ||
                string.IsNullOrWhiteSpace(request.TargetTitle)
                    ? null
                    : new IdentificationService.Target(
                        request.TargetKey.Trim(),
                        request.TargetTitle.Trim(),
                        string.IsNullOrWhiteSpace(request.TargetUrl) ? null : request.TargetUrl.Trim()),
            _ => null,
        };
        var invalid = request.Action switch
        {
            IdentificationDecisionAction.AcceptCandidate => candidate is null,
            IdentificationDecisionAction.RejectCandidate => candidate is null,
            IdentificationDecisionAction.AssignDirectly => target is null,
            IdentificationDecisionAction.ReplaceClaim => target is null || current is null,
            IdentificationDecisionAction.RevokeClaim => current is null,
            IdentificationDecisionAction.SplitVideo => !SeparableFiles(video, request).Any() ||
                SeparableFiles(video, request).Count() == video.VideoFiles.Count,
            _ => true,
        };

        if (invalid)
        {
            return new IdentificationDecisionResult(
                IdentificationDecisionVerdict.InvalidTarget,
                Case: await CaseOfAsync(video, cancellationToken));
        }

        var mergesWith = target is null
            ? null
            : await MergeCounterpartAsync(video, request.Dimension, target, cancellationToken);
        var consequence = Describe(video, request, current, candidate, target, mergesWith);

        if (!request.Confirm)
        {
            return new IdentificationDecisionResult(
                IdentificationDecisionVerdict.Preview,
                consequence,
                await CaseOfAsync(video, cancellationToken));
        }

        if (consequence.RequiresNote && string.IsNullOrWhiteSpace(request.Note))
        {
            return new IdentificationDecisionResult(
                IdentificationDecisionVerdict.NoteRequired,
                consequence,
                await CaseOfAsync(video, cancellationToken));
        }

        var priorState = StateOf(video, request.Dimension);
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        var outcome = await ApplyAsync(
            video,
            request,
            candidate,
            target,
            accountId,
            note,
            cancellationToken);
        var subject = outcome.Subject;
        subject.CaseVersion++;
        database.IdentificationDecisions.Add(new IdentificationDecisionRow
        {
            Id = Guid.CreateVersion7(),
            VideoId = subject.Id,
            Dimension = request.Dimension,
            Action = request.Action,
            DecidedByAccountId = accountId,
            CandidateId = candidate?.Id,
            TargetKey = target?.Key,
            PriorState = priorState,
            ResultingState = outcome.ResultingState ?? StateOf(subject, request.Dimension),
            MergedAnotherVideo = consequence.MergesAnotherVideo,
            Note = note,
            CreatedAt = Now(),
        });

        // A decision can move a claim, a Video's files, or both Videos of a merge or split. The
        // projection follows whatever this unit of work actually changed rather than a list this
        // method has to remember to keep correct.
        await projection.RefreshTrackedAsync(cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new IdentificationDecisionResult(
            IdentificationDecisionVerdict.Applied,
            consequence,
            await GetCaseAsync(subject.Id, cancellationToken));
    }

    private async Task<ApplyOutcome> ApplyAsync(
        VideoRow video,
        IdentificationDecisionRequest request,
        IdentificationCandidateRow? candidate,
        IdentificationService.Target? target,
        Guid accountId,
        string? note,
        CancellationToken cancellationToken)
    {
        var now = Now();

        if (request.Action == IdentificationDecisionAction.RejectCandidate)
        {
            candidate!.Status = IdentificationCandidateStatus.Rejected;
            candidate.ResolvedAt = now;
            candidate.DecidedByAccountId = accountId;
            candidate.Note = note;
            return new ApplyOutcome(video, null);
        }

        if (request.Action == IdentificationDecisionAction.RevokeClaim)
        {
            var revoked = IdentificationService.Current(video, request.Dimension)!;
            revoked.Status = IdentificationClaimStatus.Revoked;
            revoked.EndedAt = now;
            revoked.DecidedByAccountId = accountId;
            revoked.Note = note;
            await ReevaluateAsync(video, cancellationToken);
            return new ApplyOutcome(video, null);
        }

        if (request.Action == IdentificationDecisionAction.SplitVideo)
        {
            return await SplitAsync(video, request, note, cancellationToken);
        }

        SupersedeCurrent(video, request.Dimension, now);
        SupersedePending(video, request.Dimension, accountId, now);

        var subject = video;
        var counterpart = target is null
            ? null
            : await MergeCounterpartAsync(video, request.Dimension, target, cancellationToken);

        if (counterpart is not null)
        {
            subject = await identification.MergeAsync(counterpart, video, cancellationToken);
            SupersedeCurrent(subject, request.Dimension, now);
            SupersedePending(subject, request.Dimension, accountId, now);
        }

        identification.AddClaim(
            subject,
            request.Dimension,
            target!,
            IdentificationSource.AdministratorDecision,
            IdentificationEvidenceClass.Conclusive,
            matchedBy: null,
            supportingVideoFileId: candidate?.SupportingVideoFileId,
            administrativeOverride: true,
            decidedBy: accountId,
            note: note);
        return new ApplyOutcome(subject, null);
    }

    /// <summary>
    /// Separates Video Files that represent a different work. A historical identity that these
    /// occurrences carried before a merge is reactivated where one exists; otherwise the separated
    /// files receive a genuinely new Video with the split time as its Discovery Date. Both Videos
    /// are then offered to prdb again.
    /// </summary>
    private async Task<ApplyOutcome> SplitAsync(
        VideoRow video,
        IdentificationDecisionRequest request,
        string? note,
        CancellationToken cancellationToken)
    {
        var now = Now();
        var separated = SeparableFiles(video, request).ToArray();
        var files = await database.VideoFiles
            .AsTracking()
            .Include(file => file.LibraryDirectory)
            .Where(file => separated.Contains(file.Id))
            .ToListAsync(cancellationToken);
        var reactivated = await ReactivatableIdentityAsync(video, files, cancellationToken);
        var target = reactivated;

        if (target is null)
        {
            target = new VideoRow
            {
                Id = Guid.CreateVersion7(),
                DiscoveryDate = now,
            };
            database.Videos.Add(target);
        }
        else
        {
            target.SurvivingVideoId = null;
            target.MergedAt = null;
            target.CaseVersion++;
        }

        foreach (var file in files)
        {
            file.PreviousVideoId = video.Id;
            file.VideoId = target.Id;
            file.IdentifiedSha256 = null;
        }

        await database.SaveChangesAsync(cancellationToken);
        await personalState.SeparateSplitVideoAsync(
            video.Id,
            target.Id,
            separated,
            cancellationToken);

        if (!request.RetainPersonalStateWithContinuing)
        {
            await personalState.TransferAmbiguousStateAsync(video.Id, target.Id, cancellationToken);
        }

        foreach (var directory in files
                     .Select(file => file.LibraryDirectory)
                     .DistinctBy(directory => directory.Id))
        {
            await DerivedWorkQueue.QueueAsync(
                database,
                directory.Id,
                directory.ConfigurationGeneration,
                BackgroundWorkCategory.Identification,
                BackgroundWorkTrigger.FollowUpWork,
                now,
                cancellationToken);
        }

        return new ApplyOutcome(
            video,
            $"Split: {files.Count} Video File(s) separated into " +
            (reactivated is null ? "a new Video identity" : "their previous Video identity") +
            $" {target.Id}.");
    }

    /// <summary>
    /// The historical Video identity every separated occurrence carried before a merge into this
    /// Video, when they all share exactly one.
    /// </summary>
    private async Task<VideoRow?> ReactivatableIdentityAsync(
        VideoRow video,
        IReadOnlyCollection<VideoFileRow> files,
        CancellationToken cancellationToken)
    {
        var previous = files
            .Select(file => file.PreviousVideoId)
            .Distinct()
            .ToArray();

        if (previous.Length != 1 || previous[0] is not { } previousId)
        {
            return null;
        }

        return await database.Videos
            .AsTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == previousId &&
                             candidate.SurvivingVideoId == video.Id,
                cancellationToken);
    }

    private static IEnumerable<Guid> SeparableFiles(
        VideoRow video,
        IdentificationDecisionRequest request) =>
        (request.SeparatedVideoFileIds ?? [])
            .Distinct()
            .Where(id => video.VideoFiles.Any(file => file.Id == id));

    /// <summary>
    /// Offers the Video's retained content evidence to prdb again after a revocation, so the
    /// dimension is resolved from what is currently true rather than from a superseded claim.
    /// </summary>
    private async Task ReevaluateAsync(VideoRow video, CancellationToken cancellationToken)
    {
        var files = await database.VideoFiles
            .AsTracking()
            .Include(file => file.LibraryDirectory)
            .Where(file => file.VideoId == video.Id)
            .ToListAsync(cancellationToken);

        foreach (var file in files)
        {
            file.IdentifiedSha256 = null;
        }

        foreach (var directory in files
                     .Select(file => file.LibraryDirectory)
                     .DistinctBy(directory => directory.Id))
        {
            await DerivedWorkQueue.QueueAsync(
                database,
                directory.Id,
                directory.ConfigurationGeneration,
                BackgroundWorkCategory.Identification,
                BackgroundWorkTrigger.FollowUpWork,
                Now(),
                cancellationToken);
        }
    }

    private async Task<VideoRow?> MergeCounterpartAsync(
        VideoRow video,
        IdentificationDimension dimension,
        IdentificationService.Target target,
        CancellationToken cancellationToken)
    {
        if (dimension != IdentificationDimension.WorkIdentification)
        {
            return null;
        }

        var otherId = await database.IdentificationClaims
            .AsNoTracking()
            .Where(claim => claim.Dimension == dimension &&
                            claim.Status == IdentificationClaimStatus.Current &&
                            claim.TargetKey == target.Key &&
                            claim.VideoId != video.Id)
            .Select(claim => claim.VideoId)
            .FirstOrDefaultAsync(cancellationToken);

        return otherId == Guid.Empty
            ? null
            : await Query()
                .AsTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == otherId, cancellationToken);
    }

    private static void SupersedeCurrent(
        VideoRow video,
        IdentificationDimension dimension,
        DateTime now)
    {
        var current = IdentificationService.Current(video, dimension);

        if (current is not null)
        {
            current.Status = IdentificationClaimStatus.Superseded;
            current.EndedAt = now;
        }
    }

    private static void SupersedePending(
        VideoRow video,
        IdentificationDimension dimension,
        Guid accountId,
        DateTime now)
    {
        foreach (var candidate in video.IdentificationCandidates.Where(row =>
                     row.Dimension == dimension &&
                     row.Status == IdentificationCandidateStatus.Pending))
        {
            candidate.Status = IdentificationCandidateStatus.Superseded;
            candidate.ResolvedAt = now;
            candidate.DecidedByAccountId = accountId;
        }
    }

    private static IdentificationConsequence Describe(
        VideoRow video,
        IdentificationDecisionRequest request,
        IdentificationClaimRow? current,
        IdentificationCandidateRow? candidate,
        IdentificationService.Target? target,
        VideoRow? mergesWith)
    {
        var dimension = Label(request.Dimension);
        var currentLabel = current is null ? "Unknown" : $"Established \"{current.TargetTitle}\"";
        var pending = video.IdentificationCandidates
            .DistinctBy(row => row.Id)
            .Count(row => row.Dimension == request.Dimension &&
                          row.Status == IdentificationCandidateStatus.Pending);
        var merges = mergesWith is not null &&
            request.Action != IdentificationDecisionAction.RejectCandidate &&
            request.Action != IdentificationDecisionAction.RevokeClaim;
        var separated = SeparableFiles(video, request).Count();
        var claimTransition = request.Action switch
        {
            IdentificationDecisionAction.SplitVideo =>
                $"{separated} of {video.VideoFiles.Count} Video Files leave this Video and receive " +
                "their own identity. Both Videos are offered to prdb again, and their file facts, " +
                "claim history, and provenance are retained.",
            IdentificationDecisionAction.RejectCandidate =>
                $"{dimension} stays {currentLabel}.",
            IdentificationDecisionAction.RevokeClaim =>
                $"{dimension} becomes Unknown; the revoked claim stays in history and the retained " +
                "evidence is offered to prdb again.",
            _ =>
                $"{dimension}: {currentLabel} becomes Established \"{target!.Title}\" as an " +
                "Administrative Override.",
        };
        var candidateTransition = request.Action switch
        {
            IdentificationDecisionAction.SplitVideo =>
                "Open candidates stay with the continuing Video. Private viewing activity " +
                "attributable to a separated Video File follows it; ambiguous Video-level state " +
                (request.RetainPersonalStateWithContinuing
                    ? "stays with this Video."
                    : "moves to the separated Video."),
            IdentificationDecisionAction.RejectCandidate =>
                $"The candidate \"{candidate!.TargetTitle}\" becomes Rejected; the same evidence " +
                "stays suppressed until materially stronger evidence appears.",
            IdentificationDecisionAction.RevokeClaim =>
                "Candidates are unchanged.",
            _ => pending switch
            {
                0 => "No candidate is open for this dimension.",
                1 => "The open candidate becomes Superseded.",
                _ => $"All {pending} open candidates for this dimension become Superseded.",
            },
        };
        var resultingReview = request.Action switch
        {
            IdentificationDecisionAction.SplitVideo => pending > 0
                ? IdentificationReviewStatus.ReviewNeeded
                : IdentificationReviewStatus.Clear,
            IdentificationDecisionAction.RevokeClaim => pending > 0
                ? IdentificationReviewStatus.ReviewNeeded
                : IdentificationReviewStatus.Clear,
            IdentificationDecisionAction.RejectCandidate => pending > 1
                ? IdentificationReviewStatus.ReviewNeeded
                : IdentificationReviewStatus.Clear,
            _ => IdentificationReviewStatus.Clear,
        };

        return new IdentificationConsequence(
            claimTransition,
            candidateTransition,
            video.VideoFiles.Count + (merges ? mergesWith!.VideoFiles.Count : 0),
            resultingReview,
            merges,
            merges
                ? $"\"{VideoPresentation.DisplayLabel(mergesWith!)}\" already carries this work " +
                  $"identity. The two Videos merge, the earliest Discovery Date " +
                  $"({Earliest(video, mergesWith!):yyyy-MM-dd}) and both identification histories " +
                  "are retained, and private viewing state is reconciled without being shown."
                : null,
            IdentificationEvidenceRule.RequiresDecisionNote(request.Action) || merges);
    }

    private async Task<IdentificationCase> CaseOfAsync(
        VideoRow video,
        CancellationToken cancellationToken)
    {
        var decisions = await database.IdentificationDecisions
            .AsNoTracking()
            .Where(decision => decision.VideoId == video.Id)
            .OrderByDescending(decision => decision.CreatedAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        return new IdentificationCase(
            video.Id,
            video.CaseVersion,
            VideoPresentation.DisplayLabel(video),
            VideoPresentation.PreviewUrl(video),
            VideoPresentation.Summarize(video),
            video.IdentificationCandidates
                .DistinctBy(candidate => candidate.Id)
                .Where(candidate => candidate.Status == IdentificationCandidateStatus.Pending)
                .OrderByDescending(candidate => candidate.EvidenceClass)
                .ThenBy(candidate => candidate.CreatedAt)
                .Select(CandidateView)
                .ToArray(),
            video.IdentificationCandidates
                .DistinctBy(candidate => candidate.Id)
                .Where(candidate => candidate.Status != IdentificationCandidateStatus.Pending)
                .OrderByDescending(candidate => candidate.ResolvedAt)
                .Take(20)
                .Select(CandidateView)
                .ToArray(),
            video.VideoFiles
                .OrderBy(file => file.RelativePath)
                .Select(file => new IdentificationCaseFile(
                    file.Id,
                    file.RelativePath,
                    file.Availability,
                    file.DirectPlayClassification,
                    file.ContainerFormat,
                    file.VideoCodec,
                    file.AudioCodec,
                    file.DurationMilliseconds,
                    Summarized(file.OsHash),
                    Summarized(file.PerceptualHash),
                    file.HashState))
                .ToArray(),
            decisions
                .Select(decision => new IdentificationDecisionView(
                    decision.Id,
                    decision.Dimension,
                    decision.Action,
                    decision.PriorState,
                    decision.ResultingState,
                    decision.MergedAnotherVideo,
                    decision.Note,
                    VideoPresentation.AsOffset(decision.CreatedAt)!.Value))
                .ToArray(),
            UnavailableSiteActions(video),
            Explain(video));
    }

    /// <summary>
    /// A Site Recognition decision that would contradict the canonical Site of an Established Work
    /// Identification is not offered: the Administrator corrects the work or the remote catalogue
    /// instead of creating two site truths.
    /// </summary>
    private static IReadOnlyList<IdentificationDecisionAction> UnavailableSiteActions(VideoRow video) =>
        IdentificationService.Current(video, IdentificationDimension.WorkIdentification) is not null &&
        video.Metadata?.SiteId is not null
            ? [
                IdentificationDecisionAction.AcceptCandidate,
                IdentificationDecisionAction.AssignDirectly,
                IdentificationDecisionAction.ReplaceClaim,
                IdentificationDecisionAction.RevokeClaim,
            ]
            : [];

    private static string Explain(VideoRow video)
    {
        var open = video.IdentificationCandidates
            .DistinctBy(candidate => candidate.Id)
            .Where(candidate => candidate.Status == IdentificationCandidateStatus.Pending)
            .ToArray();

        if (open.Length == 0)
        {
            return "Nothing is waiting for a decision on this Video.";
        }

        if (open.Any(candidate =>
                candidate.Reason == IdentificationReviewReason.ConflictsWithAdministrativeOverride))
        {
            return "An Administrative Override is in place, so automation may report conflicting " +
                   "evidence but may not replace the current claim.";
        }

        if (open.Any(candidate =>
                candidate.Reason == IdentificationReviewReason.ConflictingConclusiveEvidence))
        {
            return "Two conclusive results disagree, and automation cannot choose between them.";
        }

        return "The evidence is only suggestive, so it can propose a candidate but cannot " +
               "establish knowledge by itself.";
    }

    private static IdentificationQueueItem Item(VideoRow video, IdentificationCandidateRow candidate)
    {
        var current = IdentificationService.Current(video, candidate.Dimension);

        return new IdentificationQueueItem(
            video.Id,
            video.CaseVersion,
            VideoPresentation.DisplayLabel(video),
            VideoPresentation.PreviewUrl(video),
            candidate.Dimension,
            current is null
                ? IdentificationResolution.Unknown
                : IdentificationResolution.Established,
            current?.TargetTitle,
            CandidateView(candidate),
            video.VideoFiles.Count,
            Explain(video));
    }

    private static IdentificationCandidateView CandidateView(IdentificationCandidateRow candidate) =>
        new(
            candidate.Id,
            candidate.Dimension,
            candidate.Status,
            candidate.TargetTitle,
            candidate.TargetUrl,
            candidate.EvidenceClass,
            candidate.Reason,
            EvidenceSummary(candidate),
            candidate.SupportingVideoFileId,
            VideoPresentation.AsOffset(candidate.CreatedAt)!.Value,
            VideoPresentation.AsOffset(candidate.ResolvedAt));

    private static string EvidenceSummary(IdentificationCandidateRow candidate) =>
        candidate.MatchedBy is null
            ? $"{candidate.EvidenceClass} evidence"
            : $"{candidate.EvidenceClass} evidence, matched by {candidate.MatchedBy}" +
              (candidate.Confidence is null ? "" : $" with {candidate.Confidence} confidence");

    private static string StateOf(VideoRow video, IdentificationDimension dimension)
    {
        var claim = IdentificationService.Current(video, dimension);

        return claim is null
            ? "Unknown"
            : $"Established \"{claim.TargetTitle}\"" +
              (claim.IsAdministrativeOverride ? " (Administrative Override)" : "");
    }

    private static string Label(IdentificationDimension dimension) =>
        dimension == IdentificationDimension.WorkIdentification
            ? "Work Identification"
            : "Site Recognition";

    private static DateTime Earliest(VideoRow left, VideoRow right) =>
        left.DiscoveryDate <= right.DiscoveryDate ? left.DiscoveryDate : right.DiscoveryDate;

    /// <summary>
    /// Hashes are shown in a shortened form: enough to compare two files at a glance without
    /// turning the review screen into a copy of the remote lookup keys.
    /// </summary>
    private static string? Summarized(string? hash) =>
        string.IsNullOrEmpty(hash) ? null : $"{hash[..Math.Min(6, hash.Length)]}…";

    private IQueryable<VideoRow> Query() =>
        database.Videos
            .AsNoTracking()
            .Include(video => video.Metadata)
            .Include(video => video.VideoFiles)
            .Include(video => video.IdentificationClaims)
            .Include(video => video.IdentificationCandidates);

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
}
