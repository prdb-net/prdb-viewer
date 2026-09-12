using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// What a review case says, as distinct from what a decision does.
/// </summary>
/// <remarks>
/// An identification review is two jobs that happen to share a screen. One reconciles identity —
/// establishing, revoking, splitting and merging — and belongs with the database transaction that
/// carries it. The other tells an Administrator what they are looking at and what each control
/// would leave behind, and is a pure reading of rows the transaction has already settled.
///
/// Keeping the second here is what lets it be read as prose: every sentence the review screen shows
/// is written in one place, in the vocabulary the rest of the application uses, and none of it can
/// reach the database to ask a question of its own. <see cref="VideoPresentation"/> does the same
/// for a Video.
/// </remarks>
internal static class IdentificationCasePresentation
{
    internal static IdentificationConsequence Describe(
        VideoRow video,
        IdentificationDecisionRequest request,
        IdentificationClaimRow? current,
        IdentificationCandidateRow? candidate,
        IdentificationService.Target? target,
        VideoRow? mergesWith,
        int separated)
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
                $"stays suppressed until {StrongerAppears(candidate)}.",
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

    /// <summary>
    /// What answering a proposed Work Association does, said before it is taken.
    ///
    /// A merge is not a label that can be peeled off: it writes Shared Library Knowledge every User
    /// sees and combines the Personal State of all of them at once. That is why associating always
    /// needs a note, even though the two Videos it joins name nothing.
    /// </summary>
    internal static IdentificationConsequence DescribeAssociation(
        VideoRow video,
        VideoRow other,
        WorkAssociationRow association,
        bool associates) =>
        new(
            associates
                ? $"These two Videos become one. \u201c{DisplayLabel(other)}\u201d and this Video " +
                  "carry the same content, and neither is identified by it: the surviving Video " +
                  "keeps whatever each side had established, which for two Unknown Videos is " +
                  $"nothing. The earliest Discovery Date ({Earliest(video, other):yyyy-MM-dd}) is " +
                  "retained."
                : "Neither Video changes. They are recorded as not carrying the same content.",
            associates
                ? "Both Video Files keep their own path, container, codec, hashes and running " +
                  "time, so a Split can take them apart again and any later identification has " +
                  "what it needs. Private viewing state is reconciled for every Account without " +
                  "being shown to anybody."
                : "The proposal does not come back while the two files stay as far apart as they " +
                  "are; a closer resemblance, or running times that stop disagreeing, may propose " +
                  "it again.",
            associates ? video.VideoFiles.Count + other.VideoFiles.Count : video.VideoFiles.Count,
            IdentificationReviewStatus.Clear,
            associates,
            associates
                ? AssociationSummary(association)
                : null,
            associates);

    /// Why this case would refuse a decision, in the words of the control it locks. Every reason
    /// is one the request checks again; what the screen owes the reader is that a decision it
    /// cannot make does not look like one it can.
    /// </summary>
    internal static string? RefusalOf(
        VideoRow video,
        IdentificationDimension dimension,
        IdentificationDecisionAction action,
        IReadOnlyList<IdentificationDecisionAction> unavailableSiteActions)
    {
        if (dimension == IdentificationDimension.SiteRecognition &&
            unavailableSiteActions.Contains(action))
        {
            return "This Site Recognition came with the Work Identification. Correct that instead " +
                   "of establishing a second site truth.";
        }

        var established = IdentificationService.Current(video, dimension) is not null;
        var label = Label(dimension);

        return action switch
        {
            IdentificationDecisionAction.AssignDirectly when established =>
                $"The {label} is already established. Replace claim is the decision that changes it.",
            IdentificationDecisionAction.ReplaceClaim when !established =>
                $"Nothing is established as the {label} yet. Assign directly is the decision that " +
                "establishes one.",
            IdentificationDecisionAction.RevokeClaim when !established =>
                $"Nothing is established as the {label} to withdraw.",
            _ => null,
        };
    }

    /// <summary>
    /// What the installation looks like after one decision, in the terms the rest of the
    /// application uses for those states: what becomes established, what stops being true, and
    /// what is still waiting afterwards.
    /// </summary>
    internal static string Outcome(
        VideoRow video,
        IdentificationCandidateRow candidate,
        IdentificationDecisionAction action,
        VideoRow? mergesWith)
    {
        var dimension = candidate.Dimension;
        var label = Label(dimension);
        var other = dimension == IdentificationDimension.WorkIdentification
            ? IdentificationDimension.SiteRecognition
            : IdentificationDimension.WorkIdentification;
        var current = IdentificationService.Current(video, dimension);
        var pending = video.IdentificationCandidates
            .DistinctBy(row => row.Id)
            .Where(row => row.Status == IdentificationCandidateStatus.Pending)
            .ToArray();
        var sameDimension = pending.Count(row => row.Dimension == dimension);
        var otherDimension = pending.Count(row => row.Dimension != dimension);
        var withdrawn = current is null
            ? ""
            : $" \u201c{current.TargetTitle}\u201d stops being current and stays in history.";
        var superseded = sameDimension > 1
            ? $" The other {Candidates(sameDimension - 1)} for the {label} " +
              $"{(sameDimension == 2 ? "becomes" : "become")} Superseded."
            : "";
        var elsewhere = $" The {Label(other)} stays {Standing(video, other)}.";
        var subject = dimension == IdentificationDimension.WorkIdentification ? "work" : "Site";
        var merge = mergesWith is null
            ? ""
            : $" \u201c{VideoPresentation.DisplayLabel(mergesWith)}\u201d already carries this " +
              "identity, " +
              "so the two Videos merge into one and the decision needs a note.";

        return action switch
        {
            IdentificationDecisionAction.AcceptCandidate =>
                $"The {label} becomes Established \u201c{candidate.TargetTitle}\u201d as an " +
                "Administrative Override, so the Video is browsable under that title rather than " +
                $"under its file name.{withdrawn}{merge}{superseded}{elsewhere} " +
                Waiting(otherDimension),

            IdentificationDecisionAction.RejectCandidate =>
                $"The {label} stays {Standing(video, dimension)}, and this proposal does not come " +
                $"back while the evidence behind it stays the same; {StrongerProposes(candidate)}" +
                $"{elsewhere} " +
                Waiting(sameDimension - 1 + otherDimension),

            IdentificationDecisionAction.AssignDirectly =>
                $"The {subject} you name becomes the Established {label} as an Administrative " +
                "Override, which conflicting automation cannot silently replace. If another Video " +
                $"already carries it, the two merge into one and the decision needs a note." +
                $"{superseded}{elsewhere} " +
                Waiting(otherDimension),

            IdentificationDecisionAction.ReplaceClaim =>
                $"The {subject} you name takes the place of the established {label}, as an " +
                $"Administrative Override, and the decision needs a note.{withdrawn} If another " +
                "Video already carries it, the two merge into one." +
                $"{superseded}{elsewhere} " +
                Waiting(otherDimension),

            IdentificationDecisionAction.RevokeClaim =>
                $"The {label} becomes Unknown, so the Video is browsable under its file name " +
                $"again, and the decision needs a note.{withdrawn} Its evidence is offered to prdb " +
                $"again.{elsewhere} " +
                Waiting(sameDimension + otherDimension),

            _ =>
                "The Video Files you tick leave this Video and receive an identity of their own, " +
                "and the decision needs a note. Both Videos are offered to prdb again and keep " +
                "their file facts, claim history and provenance; open candidates stay with the " +
                "continuing Video. " +
                Waiting(sameDimension + otherDimension),
        };
    }

    /// <summary>
    /// Where a claim stands, in the words a sentence about it can carry. StateOf writes the same
    /// fact as a heading; this writes it as prose.
    /// </summary>
    internal static string Standing(VideoRow video, IdentificationDimension dimension)
    {
        var claim = IdentificationService.Current(video, dimension);

        return claim is null
            ? "Unknown"
            : $"established as \u201c{claim.TargetTitle}\u201d" +
              (claim.IsAdministrativeOverride ? " by an Administrative Override" : "");
    }

    /// <summary>
    /// What would count as materially stronger evidence than this candidate's, in the terms of the
    /// rung it came from.
    /// </summary>
    /// <remarks>
    /// For the remote ladder the phrase means a stronger evidence class, and saying so is enough.
    /// For a similarity between two of this installation's own files it cannot: every reading of a
    /// similarity is Suggestive, so the class never changes and the reviewer would be told their
    /// rejection holds until something that cannot happen happens. What can happen is that the two
    /// files move closer or their running times stop disagreeing, and that is what this says.
    /// </remarks>
    internal static string StrongerAppears(IdentificationCandidateRow? candidate) =>
        candidate?.NeighbourDistance is null
            ? "materially stronger evidence appears"
            : "the two files move closer together, or their running times stop disagreeing";

    internal static string StrongerProposes(IdentificationCandidateRow? candidate) =>
        candidate?.NeighbourDistance is null
            ? "materially stronger evidence may propose it again."
            : "a closer resemblance between the two files, or running times that stop " +
              "disagreeing, may propose it again.";

    /// <summary>What is left waiting on this Video once a decision has been taken.</summary>
    internal static string Waiting(int remaining) => remaining switch
    {
        <= 0 => "This Video then leaves the review queue.",
        1 => "One other candidate on this Video still waits for a decision.",
        _ => $"{remaining} other candidates on this Video still wait for a decision.",
    };

    internal static string Candidates(int count) =>
        count == 1 ? "candidate" : $"{count} candidates";

    /// <summary>
    /// A Site Recognition decision that would contradict the canonical Site of an Established Work
    /// Identification is not offered: the Administrator corrects the work or the remote catalogue
    /// instead of creating two site truths.
    /// </summary>
    internal static IReadOnlyList<IdentificationDecisionAction> UnavailableSiteActions(VideoRow video) =>
        IdentificationService.Current(video, IdentificationDimension.WorkIdentification) is not null &&
        video.Metadata?.SiteId is not null
            ? [
                IdentificationDecisionAction.AcceptCandidate,
                IdentificationDecisionAction.AssignDirectly,
                IdentificationDecisionAction.ReplaceClaim,
                IdentificationDecisionAction.RevokeClaim,
            ]
            : [];

    internal static string Explain(VideoRow video)
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

        if (open.All(candidate =>
                candidate.Reason == IdentificationReviewReason.PerceptualNeighbour))
        {
            return "Another Video File of this library looks like this one and carries an " +
                   "established work identity. That is this installation's own inference rather " +
                   "than anything prdb said about this file, so it proposes and cannot establish.";
        }

        return "The evidence is only suggestive, so it can propose a candidate but cannot " +
               "establish knowledge by itself.";
    }

    internal static IdentificationQueueItem Item(
        VideoRow video,
        IdentificationCandidateRow candidate,
        VideoFileRow? neighbour = null)
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
            CandidateView(candidate, neighbour),
            video.VideoFiles.Count,
            Explain(video));
    }

    internal static IdentificationCandidateView CandidateView(
        IdentificationCandidateRow candidate,
        VideoFileRow? neighbour = null,
        IReadOnlyList<IdentificationDecisionOutlook>? decisions = null) =>
        new(
            candidate.Id,
            candidate.Dimension,
            candidate.Status,
            candidate.TargetTitle,
            candidate.TargetUrl,
            candidate.EvidenceClass,
            candidate.Reason,
            candidate.Source,
            EvidenceSummary(candidate),
            candidate.SupportingVideoFileId,
            ProposalView(candidate.ProposedWork),
            NeighbourView(candidate, neighbour),
            decisions ?? [],
            VideoPresentation.AsOffset(candidate.CreatedAt)!.Value,
            VideoPresentation.AsOffset(candidate.ResolvedAt));

    /// <summary>
    /// The other file of this library the proposal came from, as the case shows it. A proposal
    /// whose neighbour has since left the library keeps its reading — the distance and the running
    /// times it was made on are on the candidate — but there is no file left to show.
    /// </summary>
    internal static IdentificationNeighbourView? NeighbourView(
        IdentificationCandidateRow candidate,
        VideoFileRow? neighbour)
    {
        if (candidate.NeighbourDistance is not { } distance || neighbour is null)
        {
            return null;
        }

        var agree = candidate.NeighbourDurationsAgree ?? false;

        return new IdentificationNeighbourView(
            neighbour.Id,
            neighbour.VideoId,
            string.IsNullOrWhiteSpace(neighbour.Video?.DisplayLabel)
                ? Path.GetFileNameWithoutExtension(neighbour.RelativePath)
                : neighbour.Video.DisplayLabel,
            neighbour.RelativePath,
            neighbour.PublicPreviewId is not null &&
            neighbour.PreviewState == VideoFilePreviewState.Generated
                ? $"/media/previews/{neighbour.PublicPreviewId}"
                : null,
            neighbour.DurationMilliseconds,
            VideoQualityRule.For(neighbour.Width, neighbour.Height),
            distance,
            agree,
            NeighbourSummary(distance, agree));
    }

    /// <summary>
    /// How close the two files are, in words. The number is 64 bits of Hamming distance, which is
    /// not something a reader can calibrate, and the running times are the condition under which it
    /// means anything at all — so both are said rather than shown.
    /// </summary>
    /// <remarks>
    /// Where the running times disagree the sentence says why that matters, because the reasoning
    /// is not obvious and the decision turns on it: the hash samples 25 frames at proportional
    /// offsets, so two files of different lengths describe different moments, and a resemblance
    /// between them is more likely to be material 64 bits cannot describe than the same work.
    /// </remarks>
    internal static string NeighbourSummary(int distance, bool durationsAgree)
    {
        var closeness = distance switch
        {
            0 => "This file and that one look identical to the picture hash",
            <= 2 => "This file and that one look all but identical",
            <= 4 => "This file and that one look very close",
            _ => "This file and that one look close, at the edge of what counts as close at all",
        };

        return durationsAgree
            ? $"{closeness}, and their running times agree closely enough for the resemblance to " +
              "mean they show the same moments. This is what two encodes of one work look like."
            : $"{closeness} — but their running times disagree by more than the resemblance can " +
              "account for. The hash samples frames at proportional offsets, so two files of " +
              "different lengths describe different moments; a resemblance between them is as " +
              "likely to be material the hash cannot describe as it is to be the same work.";
    }

    /// <summary>
    /// What prdb says the proposed work is. The picture is offered under this installation's own
    /// address or not at all, so a review case never puts an Administrator's browser in touch with
    /// prdb, and a picture that has not arrived says which of the two reasons applies.
    /// </summary>
    internal static IdentificationProposalView? ProposalView(ProposedWorkRow? work) =>
        work is null
            ? null
            : new IdentificationProposalView(
                work.Title,
                work.SiteTitle,
                work.SiteUrl,
                RetainedActors.Names(work.ActorsJson),
                work.ArtworkState == ProposedWorkArtworkState.Retained &&
                work.PublicArtworkId is not null
                    ? $"/media/proposals/{work.PublicArtworkId}"
                    : null,
                work.ArtworkState,
                VideoPresentation.AsOffset(work.ReleaseDate),
                work.DurationMilliseconds,
                VideoPresentation.AsOffset(work.FetchedAt)!.Value);

    /// <summary>
    /// What an Administrator is told the proposal rests on. A locally derived proposal says so,
    /// because reading a name out of a path is not the same evidence as a remote match.
    /// </summary>
    /// <remarks>
    /// The evidence class and the remote confidence are two different judgements — what this
    /// installation may establish from the match, and how far the catalogue trusts the match
    /// itself — and read as one sentence they contradicted each other: "Suggestive evidence,
    /// matched by Filename with Exact confidence". Each is now attributed to whoever made it.
    /// </remarks>
    internal static string EvidenceSummary(IdentificationCandidateRow candidate)
    {
        var origin = candidate.Source == IdentificationSource.LocalInference
            ? "Local"
            : "prdb";

        return candidate.MatchedBy is null
            ? $"{origin}: {candidate.EvidenceClass} evidence"
            : $"{origin}: {candidate.EvidenceClass} evidence, matched by {candidate.MatchedBy}" +
              (candidate.Confidence is null ? "" : $", a match {origin} rates {candidate.Confidence}");
    }

    internal static string StateOf(VideoRow video, IdentificationDimension dimension)
    {
        var claim = IdentificationService.Current(video, dimension);

        return claim is null
            ? "Unknown"
            : $"Established \"{claim.TargetTitle}\"" +
              (claim.IsAdministrativeOverride ? " (Administrative Override)" : "");
    }

    /// <summary>
    /// One Work Association as the review shows it. The other side is a Video of this library, so
    /// what is shown is that Video and the file the conclusion was drawn from — not a work.
    /// </summary>
    internal static IdentificationAssociationView AssociationView(
        WorkAssociationRow association,
        Guid subjectVideoId,
        VideoRow? otherVideo,
        VideoFileRow? otherFile)
    {
        var other = association.VideoId == subjectVideoId
            ? association.OtherVideoId
            : association.VideoId;

        return new IdentificationAssociationView(
            association.Id,
            association.Status,
            association.Source,
            subjectVideoId,
            other,
            otherVideo is not null && !string.IsNullOrWhiteSpace(otherVideo.DisplayLabel)
                ? otherVideo.DisplayLabel
                : otherFile is not null
                    ? Path.GetFileNameWithoutExtension(otherFile.RelativePath)
                    : "Another Video of this library",
            otherFile is not null &&
            otherFile.PublicPreviewId is not null &&
            otherFile.PreviewState == VideoFilePreviewState.Generated
                ? $"/media/previews/{otherFile.PublicPreviewId}"
                : null,
            otherFile?.RelativePath,
            otherFile?.DurationMilliseconds ?? 0,
            VideoQualityRule.For(otherFile?.Width, otherFile?.Height),
            association.Distance,
            association.DurationsAgree,
            AssociationSummary(association),
            association.Note,
            VideoPresentation.AsOffset(association.CreatedAt)!.Value,
            VideoPresentation.AsOffset(association.EstablishedAt),
            VideoPresentation.AsOffset(association.ResolvedAt));
    }

    /// <summary>
    /// What an association asserts and what it was concluded from, in one sentence. It has no
    /// target to name, so the sentence is all a reader has: an association nobody can account for
    /// is exactly what the evidence principle forbids.
    /// </summary>
    internal static string AssociationSummary(WorkAssociationRow association)
    {
        var reading = NeighbourSummary(association.Distance, association.DurationsAgree);
        var by = association.Source == IdentificationSource.AdministratorDecision
            ? "An Administrator decided"
            : "This installation concluded";

        return association.Status switch
        {
            WorkAssociationStatus.Established =>
                $"{by} that these two Videos carry the same content, and they are now one Video. " +
                "It names no work, so the Video is still Unknown until something identifies it. " +
                reading,
            WorkAssociationStatus.Rejected =>
                "These two Videos were decided not to carry the same content. " + reading,
            WorkAssociationStatus.Separated =>
                "These two Videos were associated and a Split has taken them apart again. " + reading,
            _ =>
                "These two Videos may carry the same content. Associating them merges them while " +
                "both stay Unknown: it names no work, both files keep their own facts, and a " +
                "Split undoes it. " + reading,
        };
    }

    /// <summary>
    /// What every case in one group shares, which is exactly what one answer would settle.
    ///
    /// A count on its own is not a description of a group: "400 cases" says how much is at stake
    /// and nothing about what is being asked, and a reviewer who cannot say what a group has in
    /// common cannot safely answer it at all.
    /// </summary>
    internal static string InCommon(
        IdentificationDimension dimension,
        IdentificationReviewReason reason,
        IdentificationEvidenceClass evidence,
        IdentificationSource source,
        string? targetTitle,
        int caseCount,
        bool displaces)
    {
        var videos = caseCount == 1 ? "One Video" : $"{caseCount} Videos";
        var origin = source == IdentificationSource.LocalInference
            ? "this installation's own inference"
            : "prdb";

        if (targetTitle is null)
        {
            return $"{videos} of this library look like one other Video each, and neither of any " +
                   "pair is identified. Each is its own question.";
        }

        var proposal = $"{videos} are proposed as \u201c{targetTitle}\u201d for their " +
                       $"{Label(dimension)}, on {evidence.ToString().ToLowerInvariant()} evidence " +
                       $"from {origin}.";

        return reason switch
        {
            IdentificationReviewReason.PerceptualNeighbour =>
                $"{proposal} Each of them looks like a file this library has already identified " +
                "as that work.",
            IdentificationReviewReason.ConflictsWithAdministrativeOverride =>
                $"{proposal} Each already carries an Administrative Override that says otherwise.",
            IdentificationReviewReason.ConflictingConclusiveEvidence =>
                $"{proposal} Each already carries a conclusive answer that disagrees.",
            IdentificationReviewReason.RemoteIdentityChanged =>
                $"{proposal} prdb has changed its mind about each of them.",
            _ when displaces =>
                $"{proposal} Each already has something established, which answering would replace.",
            _ => proposal,
        };
    }

    /// <summary>
    /// Where the cases of a group differ, so a count never stands alone for what it would settle.
    /// </summary>
    internal static string Differ(int caseCount, string? targetTitle) =>
        (caseCount, targetTitle) switch
        {
            (1, _) => "There is one of them, so there is nothing to differ.",
            (_, null) =>
                "They differ in everything except the shape of the question: each names two " +
                "particular files of this library and nobody else's.",
            _ =>
                "They differ in which Video is being asked about, and in nothing else the answer " +
                "reads: the proposal, the evidence behind it and what it would displace are the " +
                "same for all of them.",
        };

    /// <summary>
    /// Why a decision cannot be taken over a whole group, or null where it can.
    ///
    /// Accepting and rejecting are the two a group can carry at all. The others are per-Video
    /// judgements: assigning and replacing read a target somebody typed for one Video, revoking
    /// withdraws one Video's own established knowledge, and a Split is about which of one Video's
    /// files belong together. None of them means anything said four hundred times at once.
    /// </summary>
    internal static string? GroupRefusal(IdentificationDecisionAction action, bool association) =>
        (action, association) switch
        {
            (IdentificationDecisionAction.AcceptCandidate, false) => null,
            (IdentificationDecisionAction.RejectCandidate, false) => null,
            (IdentificationDecisionAction.AssociateVideos, true) => null,
            (IdentificationDecisionAction.RejectAssociation, true) => null,
            (IdentificationDecisionAction.AcceptCandidate or
                IdentificationDecisionAction.RejectCandidate, true) =>
                "This group is a proposed association, which names no target to accept or reject.",
            (IdentificationDecisionAction.AssociateVideos or
                IdentificationDecisionAction.RejectAssociation, false) =>
                "This group proposes an identification rather than an association.",
            (IdentificationDecisionAction.SplitVideo, _) =>
                "A Split is about which of one Video's files belong together, which is a different " +
                "question for every Video. It stays a decision taken one Video at a time.",
            (IdentificationDecisionAction.RevokeClaim, _) =>
                "Revoking withdraws what one Video has established, on that Video's own evidence. " +
                "It stays a decision taken one Video at a time.",
            _ =>
                "Assigning and replacing read a target you type for one Video. Said over a group " +
                "they would put the same answer on every case without anybody having looked.",
        };

    /// <summary>
    /// What each decision would do to a whole group, said before the button rather than in a
    /// preview it is too late to read.
    /// </summary>
    internal static IReadOnlyList<IdentificationGroupConsequence> GroupDecisions(
        bool association,
        IdentificationDimension dimension,
        int caseCount,
        string? targetTitle,
        bool mergesIntoAnExistingVideo,
        int refusedCases)
    {
        IdentificationDecisionAction[] offered =
        [
            IdentificationDecisionAction.AcceptCandidate,
            IdentificationDecisionAction.RejectCandidate,
            IdentificationDecisionAction.AssociateVideos,
            IdentificationDecisionAction.RejectAssociation,
            IdentificationDecisionAction.AssignDirectly,
            IdentificationDecisionAction.ReplaceClaim,
            IdentificationDecisionAction.RevokeClaim,
            IdentificationDecisionAction.SplitVideo,
        ];

        return offered
            .Select(action => GroupConsequence(
                action,
                association,
                dimension,
                caseCount,
                targetTitle,
                mergesIntoAnExistingVideo,
                refusedCases))
            .ToArray();
    }

    private static IdentificationGroupConsequence GroupConsequence(
        IdentificationDecisionAction action,
        bool association,
        IdentificationDimension dimension,
        int caseCount,
        string? targetTitle,
        bool mergesIntoAnExistingVideo,
        int refusedCases)
    {
        var refusal = GroupRefusal(action, association);
        var settles = Math.Max(0, caseCount - refusedCases);

        if (refusal is not null)
        {
            return new IdentificationGroupConsequence(action, refusal, caseCount, 0, 0, refusedCases, false, refusal);
        }

        // Every case of a work group proposes the same work, so the library ends with one Video
        // carrying it and the rest merged into that one. That is the whole consequence, and it is
        // the reason a count on a button is not enough on its own.
        var merges = action is IdentificationDecisionAction.AcceptCandidate &&
                     dimension == IdentificationDimension.WorkIdentification
            ? Math.Max(0, settles - (mergesIntoAnExistingVideo ? 0 : 1))
            : action == IdentificationDecisionAction.AssociateVideos
                ? settles
                : 0;
        var rejects = action is IdentificationDecisionAction.RejectCandidate or
            IdentificationDecisionAction.RejectAssociation;
        var refusedSentence = refusedCases == 0
            ? ""
            : $" {Cases(refusedCases)} of the group cannot be decided this way and stay open.";
        var mergeSentence = merges == 0
            ? ""
            : $" {Videos(merges)} merge into the Video that carries the work, and every Account's " +
              "private viewing state for them is reconciled without being shown to anybody.";
        var outcome = rejects
            ? $"{Cases(settles)} are rejected. Nothing established changes, and the same evidence " +
              $"stays suppressed on each of them.{refusedSentence}"
            : action == IdentificationDecisionAction.AssociateVideos
                ? $"{Cases(settles)} are associated. Neither Video of any pair is identified by " +
                  $"it, and each pair becomes one Unknown Video.{mergeSentence}{refusedSentence}"
                : $"{Videos(settles)} become Established \u201c{targetTitle}\u201d for their " +
                  $"{Label(dimension)}, each as an Administrative Override.{mergeSentence}" +
                  refusedSentence;

        return new IdentificationGroupConsequence(
            action,
            null,
            caseCount,
            rejects ? 0 : settles,
            merges,
            refusedCases,
            merges > 0,
            outcome);
    }

    /// <summary>What one batch of a group decision actually did.</summary>
    internal static string GroupOutcome(int applied, int skipped, int refused)
    {
        var sentence = applied == 0
            ? "Nothing was settled."
            : $"{Cases(applied)} settled.";

        if (skipped > 0)
        {
            sentence += $" {Cases(skipped)} changed while the group was being read and stayed open.";
        }

        if (refused > 0)
        {
            sentence += $" {Cases(refused)} cannot be decided this way and stayed open.";
        }

        return sentence;
    }

    private static string Cases(int count) => count == 1 ? "One case" : $"{count} cases";

    private static string Videos(int count) => count == 1 ? "One Video" : $"{count} Videos";

    internal static string Label(IdentificationDimension dimension) =>
        dimension == IdentificationDimension.WorkIdentification
            ? "Work Identification"
            : "Site Recognition";

    internal static string DisplayLabel(VideoRow video) => VideoPresentation.DisplayLabel(video);

    internal static DateTime Earliest(VideoRow left, VideoRow right) =>
        left.DiscoveryDate <= right.DiscoveryDate ? left.DiscoveryDate : right.DiscoveryDate;

    /// <summary>
    /// Hashes are shown in a shortened form: enough to compare two files at a glance without
    /// turning the review screen into a copy of the remote lookup keys.
    /// </summary>
    internal static string? Summarized(string? hash) =>
        string.IsNullOrEmpty(hash) ? null : $"{hash[..Math.Min(6, hash.Length)]}…";
}
