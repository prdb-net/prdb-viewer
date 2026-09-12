using Microsoft.EntityFrameworkCore.Storage;

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
    LocalSimilarityService similarity,
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

        var neighbours = await NeighbouringFilesAsync(
            videos.SelectMany(video => video.IdentificationCandidates),
            cancellationToken);
        var proposals = videos
            .SelectMany(video => video.IdentificationCandidates
                .DistinctBy(candidate => candidate.Id)
                .Where(candidate => candidate.Status == IdentificationCandidateStatus.Pending)
                .Select(candidate => IdentificationCasePresentation.Item(
                    video,
                    candidate,
                    Neighbour(neighbours, candidate))));

        return proposals
            .Concat(await ProposedAssociationsAsync(cancellationToken))
            // A proposal naming a work and one naming nothing are both cases, and the queue is one
            // queue. Conclusive evidence first, then the oldest question: an association carries no
            // evidence class of its own, because it is not evidence about an identity at all.
            .OrderByDescending(item =>
                item.Candidate?.EvidenceClass ?? IdentificationEvidenceClass.Suggestive)
            .ThenBy(item => item.Candidate?.CreatedAt ?? item.Association!.CreatedAt)
            .ToArray();
    }

    /// <summary>
    /// The associations waiting for an Administrator, as queue cases. One proposal is one case:
    /// it is asked on the Video the pair is held under rather than on both of them, because two
    /// entries for one question would be answered twice or not at all.
    /// </summary>
    private async Task<IReadOnlyList<IdentificationQueueItem>> ProposedAssociationsAsync(
        CancellationToken cancellationToken)
    {
        var associations = await database.WorkAssociations
            .AsNoTracking()
            .Where(row => row.Status == WorkAssociationStatus.Proposed)
            .ToListAsync(cancellationToken);

        if (associations.Count == 0)
        {
            return [];
        }

        var videos = await AssociatedVideosAsync(associations, cancellationToken);
        var files = await AssociatedFilesAsync(associations, cancellationToken);
        var items = new List<IdentificationQueueItem>(associations.Count);

        foreach (var association in associations)
        {
            if (!videos.TryGetValue(association.VideoId, out var video) ||
                !videos.TryGetValue(association.OtherVideoId, out var other))
            {
                continue;
            }

            files.TryGetValue(association.OtherVideoFileId, out var otherFile);
            items.Add(new IdentificationQueueItem(
                video.Id,
                video.CaseVersion,
                VideoPresentation.DisplayLabel(video),
                VideoPresentation.PreviewUrl(video),
                IdentificationDimension.WorkIdentification,
                IdentificationResolution.Unknown,
                null,
                null,
                video.VideoFiles.Count + other.VideoFiles.Count,
                "Two Videos of this library look alike and neither is identified.",
                IdentificationCasePresentation.AssociationView(
                    association,
                    video.Id,
                    other,
                    otherFile)));
        }

        return items;
    }

    private async Task<Dictionary<Guid, VideoRow>> AssociatedVideosAsync(
        IReadOnlyCollection<WorkAssociationRow> associations,
        CancellationToken cancellationToken)
    {
        var wanted = associations
            .SelectMany(row => new[] { row.VideoId, row.OtherVideoId })
            .Distinct()
            .ToArray();

        return await Query()
            .Where(video => wanted.Contains(video.Id) && video.SurvivingVideoId == null)
            .ToDictionaryAsync(video => video.Id, cancellationToken);
    }

    private async Task<Dictionary<Guid, VideoFileRow>> AssociatedFilesAsync(
        IReadOnlyCollection<WorkAssociationRow> associations,
        CancellationToken cancellationToken)
    {
        var wanted = associations
            .SelectMany(row => new[] { row.VideoFileId, row.OtherVideoFileId })
            .Distinct()
            .ToArray();

        return await database.VideoFiles
            .AsNoTracking()
            .Where(file => wanted.Contains(file.Id))
            .ToDictionaryAsync(file => file.Id, cancellationToken);
    }

    /// <summary>
    /// Every association this Video takes part in, from either side, as the case shows them.
    /// </summary>
    private async Task<IReadOnlyList<IdentificationAssociationView>> AssociationsOfAsync(
        Guid videoId,
        CancellationToken cancellationToken)
    {
        var associations = await database.WorkAssociations
            .AsNoTracking()
            .Where(row => row.VideoId == videoId || row.OtherVideoId == videoId)
            .OrderByDescending(row => row.CreatedAt)
            .Take(40)
            .ToListAsync(cancellationToken);

        if (associations.Count == 0)
        {
            return [];
        }

        var videos = await AssociatedVideosAsync(associations, cancellationToken);
        var files = await AssociatedFilesAsync(associations, cancellationToken);

        return associations
            .Select(association =>
            {
                var otherVideoId = association.VideoId == videoId
                    ? association.OtherVideoId
                    : association.VideoId;
                var otherFileId = association.VideoId == videoId
                    ? association.OtherVideoFileId
                    : association.VideoFileId;
                videos.TryGetValue(otherVideoId, out var otherVideo);
                files.TryGetValue(otherFileId, out var otherFile);

                return IdentificationCasePresentation.AssociationView(
                    association,
                    videoId,
                    otherVideo,
                    otherFile);
            })
            .ToArray();
    }

    /// <summary>
    /// The other Video Files a set of candidates was proposed from, read in one query rather than
    /// one per case. The Video comes with them for its projected label, which is what names the
    /// neighbour on the screen; nothing here reads a claim again.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, VideoFileRow>> NeighbouringFilesAsync(
        IEnumerable<IdentificationCandidateRow> candidates,
        CancellationToken cancellationToken)
    {
        var wanted = candidates
            .Select(candidate => candidate.NeighbourVideoFileId)
            .OfType<Guid>()
            .Distinct()
            .ToArray();

        return wanted.Length == 0
            ? new Dictionary<Guid, VideoFileRow>()
            : await database.VideoFiles
                .AsNoTracking()
                .Include(file => file.Video)
                .Where(file => wanted.Contains(file.Id))
                .ToDictionaryAsync(file => file.Id, cancellationToken);
    }

    private static VideoFileRow? Neighbour(
        IReadOnlyDictionary<Guid, VideoFileRow> neighbours,
        IdentificationCandidateRow candidate) =>
        candidate.NeighbourVideoFileId is { } id && neighbours.TryGetValue(id, out var file)
            ? file
            : null;

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

        // An association is decided on the same case, through the same endpoint, bound to the same
        // version and recorded in the same history — but it names no target and belongs to no
        // dimension, so it is answered before everything below reads one.
        if (request.Action is IdentificationDecisionAction.AssociateVideos or
            IdentificationDecisionAction.RejectAssociation)
        {
            return await DecideAssociationAsync(
                accountId,
                video,
                request,
                transaction,
                cancellationToken);
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

        // A decision that establishes a work identity has just made this Video worth something to
        // the files of this library that look like it. It joins this transaction rather than
        // opening its own, so a decision and what it proposes elsewhere are one act or neither.
        await similarity.OfferVideoToNeighboursAsync(subject.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new IdentificationDecisionResult(
            IdentificationDecisionVerdict.Applied,
            consequence,
            await GetCaseAsync(subject.Id, cancellationToken));
    }

    /// <summary>
    /// Answers a proposed Work Association: the two Videos become one, or a person says they are
    /// not the same content.
    /// </summary>
    /// <remarks>
    /// It shares everything an identification decision has that is worth sharing — the version the
    /// case was read at, the consequence said before it is taken, the note, the record in the
    /// Video's own history — and nothing that would only fit an identification. There is no target
    /// to name and no dimension to move, because an association identifies neither Video: what
    /// comes out of an accepted one is still an Unknown Video, with two Video Files.
    /// </remarks>
    private async Task<IdentificationDecisionResult> DecideAssociationAsync(
        Guid accountId,
        VideoRow video,
        IdentificationDecisionRequest request,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        var association = request.AssociationId is null
            ? null
            : await database.WorkAssociations
                .AsTracking()
                .SingleOrDefaultAsync(
                    row => row.Id == request.AssociationId &&
                           row.Status == WorkAssociationStatus.Proposed &&
                           (row.VideoId == video.Id || row.OtherVideoId == video.Id),
                    cancellationToken);

        if (association is null)
        {
            return new IdentificationDecisionResult(
                IdentificationDecisionVerdict.InvalidTarget,
                Case: await CaseOfAsync(video, cancellationToken));
        }

        var associates = request.Action == IdentificationDecisionAction.AssociateVideos;
        var otherId = association.VideoId == video.Id
            ? association.OtherVideoId
            : association.VideoId;
        var other = await Query()
            .AsTracking()
            .SingleOrDefaultAsync(row => row.Id == otherId, cancellationToken);

        if (other is null)
        {
            return new IdentificationDecisionResult(
                IdentificationDecisionVerdict.InvalidTarget,
                Case: await CaseOfAsync(video, cancellationToken));
        }

        var consequence = IdentificationCasePresentation.DescribeAssociation(
            video,
            other,
            association,
            associates);

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

        var now = Now();
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        var priorState = IdentificationCasePresentation.StateOf(
            video,
            IdentificationDimension.WorkIdentification);
        var subject = video;

        if (associates)
        {
            subject = await identification.MergeAsync(video, other, cancellationToken);
            association.VideoId = subject.Id;
            association.OtherVideoId = subject.Id == video.Id ? other.Id : video.Id;
            association.EstablishedAt = now;
        }

        association.Status = associates
            ? WorkAssociationStatus.Established
            : WorkAssociationStatus.Rejected;
        association.Source = IdentificationSource.AdministratorDecision;
        association.DecidedByAccountId = accountId;
        association.Note = note;
        association.ResolvedAt = now;
        subject.CaseVersion++;
        database.IdentificationDecisions.Add(new IdentificationDecisionRow
        {
            Id = Guid.CreateVersion7(),
            VideoId = subject.Id,
            Dimension = IdentificationDimension.WorkIdentification,
            Action = request.Action,
            DecidedByAccountId = accountId,
            CandidateId = null,
            TargetKey = null,
            PriorState = priorState,
            ResultingState = IdentificationCasePresentation.StateOf(
                subject,
                IdentificationDimension.WorkIdentification),
            MergedAnotherVideo = associates,
            Note = note,
            CreatedAt = now,
        });

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

        // An association that put two of these files under one identity has just been undone. It
        // stays on record as what it was — a Split is how an association is undone, and the record
        // is what stops the rule concluding it again from the same reading.
        await database.WorkAssociations
            .Where(row => row.Status == WorkAssociationStatus.Established &&
                          (separated.Contains(row.VideoFileId) ||
                           separated.Contains(row.OtherVideoFileId)))
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(row => row.Status, WorkAssociationStatus.Separated)
                    .SetProperty(row => row.ResolvedAt, now),
                cancellationToken);

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
        var neighbours = await NeighbouringFilesAsync(
            video.IdentificationCandidates,
            cancellationToken);
        var associations = await AssociationsOfAsync(video.Id, cancellationToken);

        foreach (var candidate in open)
        {
            openViews.Add(IdentificationCasePresentation.CandidateView(
                candidate,
                Neighbour(neighbours, candidate),
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
                .Select(candidate => IdentificationCasePresentation.CandidateView(
                    candidate,
                    Neighbour(neighbours, candidate)))
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
            IdentificationCasePresentation.Explain(video),
            associations
                .Where(association => association.Status == WorkAssociationStatus.Proposed)
                .ToArray(),
            associations
                .Where(association => association.Status != WorkAssociationStatus.Proposed)
                .ToArray());
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
