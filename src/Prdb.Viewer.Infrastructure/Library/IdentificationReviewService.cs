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
                .Select(candidate => IdentificationCasePresentation.Item(video, candidate)))
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
            IdentificationCasePresentation.UnavailableSiteActions(video).Contains(request.Action))
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
        var consequence = IdentificationCasePresentation.Describe(
            video,
            request,
            current,
            candidate,
            target,
            mergesWith,
            SeparableFiles(video, request).Count());

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

        var priorState = IdentificationCasePresentation.StateOf(video, request.Dimension);
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
            ResultingState = outcome.ResultingState ?? IdentificationCasePresentation.StateOf(subject, request.Dimension),
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

        // Only an open candidate is worth an outlook: a resolved one is history, and history is
        // read for what happened rather than for what pressing something would do.
        var open = video.IdentificationCandidates
            .DistinctBy(candidate => candidate.Id)
            .Where(candidate => candidate.Status == IdentificationCandidateStatus.Pending)
            .OrderByDescending(candidate => candidate.EvidenceClass)
            .ThenBy(candidate => candidate.CreatedAt)
            .ToArray();
        var openViews = new List<IdentificationCandidateView>(open.Length);

        foreach (var candidate in open)
        {
            openViews.Add(IdentificationCasePresentation.CandidateView(
                candidate,
                await OutlookAsync(video, candidate, cancellationToken)));
        }

        return new IdentificationCase(
            video.Id,
            video.CaseVersion,
            VideoPresentation.DisplayLabel(video),
            VideoPresentation.PreviewUrl(video),
            VideoPresentation.Summarize(video),
            openViews,
            video.IdentificationCandidates
                .DistinctBy(candidate => candidate.Id)
                .Where(candidate => candidate.Status != IdentificationCandidateStatus.Pending)
                .OrderByDescending(candidate => candidate.ResolvedAt)
                .Take(20)
                .Select(candidate => IdentificationCasePresentation.CandidateView(candidate))
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
                    IdentificationCasePresentation.Summarized(file.OsHash),
                    IdentificationCasePresentation.Summarized(file.PerceptualHash),
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
            IdentificationCasePresentation.UnavailableSiteActions(video),
            IdentificationCasePresentation.Explain(video));
    }

    /// <summary>
    /// Every decision this case offers for one candidate, each with what the installation looks
    /// like once it is taken, or with the reason it cannot be taken at all.
    /// </summary>
    /// <remarks>
    /// The five controls under a review case used to say what they do to the candidate and nothing
    /// about what they leave behind, and the reasons a locked one was locked sat under the whole
    /// row as though they were a remark about the case. Both are settled here, where the rules
    /// that decide them already live, rather than being read a second time by the screen.
    /// </remarks>
    private async Task<IReadOnlyList<IdentificationDecisionOutlook>> OutlookAsync(
        VideoRow video,
        IdentificationCandidateRow candidate,
        CancellationToken cancellationToken)
    {
        var dimension = candidate.Dimension;
        var refused = IdentificationCasePresentation.UnavailableSiteActions(video);
        // Accepting is the one decision whose target is known before it is taken, so it is the one
        // whose merge can be named in advance. The two that read a typed target say instead that a
        // merge is possible, which is the honest thing to say about a name nobody has typed yet.
        var mergesWith = refused.Contains(IdentificationDecisionAction.AcceptCandidate)
            ? null
            : await MergeCounterpartAsync(
                video,
                dimension,
                new IdentificationService.Target(
                    candidate.TargetKey,
                    candidate.TargetTitle,
                    candidate.TargetUrl),
                cancellationToken);
        var offered = new[]
        {
            IdentificationDecisionAction.AcceptCandidate,
            IdentificationDecisionAction.RejectCandidate,
            IdentificationDecisionAction.AssignDirectly,
            IdentificationDecisionAction.ReplaceClaim,
            IdentificationDecisionAction.RevokeClaim,
            IdentificationDecisionAction.SplitVideo,
        };

        return offered
            .Where(action => action != IdentificationDecisionAction.SplitVideo ||
                             video.VideoFiles.Count > 1)
            .Select(action => new IdentificationDecisionOutlook(
                action,
                IdentificationCasePresentation.RefusalOf(video, dimension, action, refused),
                IdentificationCasePresentation.Outcome(video, candidate, action, mergesWith)))
            .ToArray();
    }

    /// <summary>

    private IQueryable<VideoRow> Query() =>
        database.Videos
            .AsNoTracking()
            .Include(video => video.Metadata)
            .Include(video => video.VideoFiles)
            .Include(video => video.IdentificationClaims)
            .Include(video => video.IdentificationCandidates)
            .ThenInclude(candidate => candidate.ProposedWork);

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
}
