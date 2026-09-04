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
                "back while the evidence behind it stays the same; materially stronger evidence " +
                $"may propose it again.{elsewhere} " +
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

        return "The evidence is only suggestive, so it can propose a candidate but cannot " +
               "establish knowledge by itself.";
    }

    internal static IdentificationQueueItem Item(VideoRow video, IdentificationCandidateRow candidate)
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

    internal static IdentificationCandidateView CandidateView(
        IdentificationCandidateRow candidate,
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
            decisions ?? [],
            VideoPresentation.AsOffset(candidate.CreatedAt)!.Value,
            VideoPresentation.AsOffset(candidate.ResolvedAt));

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

    internal static string Label(IdentificationDimension dimension) =>
        dimension == IdentificationDimension.WorkIdentification
            ? "Work Identification"
            : "Site Recognition";

    internal static DateTime Earliest(VideoRow left, VideoRow right) =>
        left.DiscoveryDate <= right.DiscoveryDate ? left.DiscoveryDate : right.DiscoveryDate;

    /// <summary>
    /// Hashes are shown in a shortened form: enough to compare two files at a glance without
    /// turning the review screen into a copy of the remote lookup keys.
    /// </summary>
    internal static string? Summarized(string? hash) =>
        string.IsNullOrEmpty(hash) ? null : $"{hash[..Math.Min(6, hash.Length)]}…";
}
