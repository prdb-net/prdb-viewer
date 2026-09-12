using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// Draws the consequences of two of this installation's Video Files looking alike.
///
/// The first of them is worth having on its own: one file of a pair is identified and the other is
/// not. prdb answered for the 1080p copy and had nothing for the re-encode in another Library
/// Directory, and until now that knowledge died where it was computed — the Unknown Video stayed
/// Unknown until prdb learned the second file or somebody found both by hand.
///
/// It proposes, and only proposes. An Established Work Identification on one Video becomes an
/// Identification Candidate on its neighbour, never a claim: a similarity is this installation's
/// own inference about two pictures rather than an inspection of the content by the catalogue that
/// holds the work. A neighbour whose own work identity disagrees is therefore a conflict for
/// review rather than a silent second claim, which is the reasoning
/// <see cref="IdentificationEvidenceRule.SupersedesAutomatically"/> already encodes.
/// </summary>
public sealed class LocalSimilarityService(
    ViewerDbContext database,
    IdentificationService identification,
    VideoProjection projection,
    TimeProvider timeProvider)
{
    /// <summary>How a neighbour-derived proposal was matched, as the review surfaces name it.</summary>
    public const string MatchedBy = "another Video File of this library that looks like it";

    /// <summary>
    /// Offers what is established about each of these Video Files' neighbours to the other side of
    /// every Perceptual Neighbourhood they take part in.
    ///
    /// It is called wherever one of the two facts it reads has just changed — a neighbourhood was
    /// established, or a work identity was — and it is idempotent, because a proposal that names
    /// what is already established confirms the claim instead of being recorded twice.
    /// </summary>
    public async Task OfferToNeighboursAsync(
        IReadOnlyCollection<Guid> videoFileIds,
        CancellationToken cancellationToken = default)
    {
        if (videoFileIds.Count == 0)
        {
            return;
        }

        var subjects = videoFileIds.ToArray();
        var neighbourhoods = await database.PerceptualNeighbourhoods
            .AsNoTracking()
            .Where(row => subjects.Contains(row.LeftVideoFileId) ||
                          subjects.Contains(row.RightVideoFileId))
            .ToListAsync(cancellationToken);

        if (neighbourhoods.Count == 0)
        {
            return;
        }

        var participants = neighbourhoods
            .SelectMany(row => new[] { row.LeftVideoFileId, row.RightVideoFileId })
            .Distinct()
            .ToArray();
        var files = await database.VideoFiles
            .AsNoTracking()
            .Where(file => participants.Contains(file.Id))
            .ToDictionaryAsync(file => file.Id, cancellationToken);

        // The offer is sometimes the whole unit of work and sometimes the tail of somebody else's —
        // an Administrator's decision, which has already written a claim and must not have this
        // committed separately from it. It joins the transaction it finds rather than opening a
        // second one.
        var transaction = database.Database.CurrentTransaction is null
            ? await database.Database.BeginTransactionAsync(cancellationToken)
            : null;

        foreach (var neighbourhood in neighbourhoods)
        {
            if (!files.TryGetValue(neighbourhood.LeftVideoFileId, out var left) ||
                !files.TryGetValue(neighbourhood.RightVideoFileId, out var right) ||
                left.VideoId == right.VideoId)
            {
                continue;
            }

            // The narrow path first. Where it applies there is nothing left to propose: the two
            // Videos become one, and a proposal about the other side of a pair that no longer has
            // two sides is a question nobody can answer.
            if (await AssociateAsync(left, right, neighbourhood, files, cancellationToken))
            {
                continue;
            }

            await OfferAsync(left, right, neighbourhood, cancellationToken);
            await OfferAsync(right, left, neighbourhood, cancellationToken);
            await ProposeAssociationAsync(left, right, neighbourhood, cancellationToken);
        }

        await projection.RefreshTrackedAsync(cancellationToken);
        await database.SaveChangesAsync(cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            await transaction.DisposeAsync();
        }
    }

    public Task OfferToNeighboursAsync(Guid videoFileId, CancellationToken cancellationToken = default) =>
        OfferToNeighboursAsync([videoFileId], cancellationToken);

    /// <summary>
    /// Offers every Video File of one Video to its neighbours, which is what a Video that has just
    /// acquired a work identity has to say to the files that look like it.
    /// </summary>
    public async Task OfferVideoToNeighboursAsync(
        Guid videoId,
        CancellationToken cancellationToken = default)
    {
        var files = await database.VideoFiles
            .AsNoTracking()
            .Where(file => file.VideoId == videoId)
            .Select(file => file.Id)
            .ToListAsync(cancellationToken);

        await OfferToNeighboursAsync(files, cancellationToken);
    }

    private async Task OfferAsync(
        VideoFileRow known,
        VideoFileRow neighbour,
        PerceptualNeighbourhoodRow neighbourhood,
        CancellationToken cancellationToken)
    {
        var evidence = IdentificationEvidenceRule.ClassifyNeighbourWorkIdentification(
            neighbourhood.Distance);

        if (evidence == IdentificationEvidenceClass.Insufficient)
        {
            return;
        }

        var source = await identification.LoadAsync(known.VideoId, cancellationToken);
        var claim = IdentificationService.Current(
            source,
            IdentificationDimension.WorkIdentification);

        if (claim is null)
        {
            return;
        }

        var subject = await identification.LoadAsync(neighbour.VideoId, cancellationToken);
        // What prdb says about the work, where this installation happens to hold it already. The
        // case leads with the neighbouring file, because that is what the proposal actually rests
        // on; the work's own facts are what the file is being compared against.
        var described = await database.ProposedWorks
            .AsTracking()
            .SingleOrDefaultAsync(
                work => work.PrdbVideoId == claim.TargetKey,
                cancellationToken);

        identification.Propose(
            subject,
            IdentificationDimension.WorkIdentification,
            new IdentificationService.Target(claim.TargetKey, claim.TargetTitle, claim.TargetUrl),
            evidence,
            result: null,
            neighbour,
            EvidenceKey(known.Id),
            IdentificationSource.LocalInference,
            MatchedBy,
            described?.Id,
            new IdentificationService.NeighbourEvidence(
                known.Id,
                neighbourhood.Distance,
                neighbourhood.DurationsAgree),
            IdentificationReviewReason.PerceptualNeighbour);
    }

    /// <summary>
    /// Associates two Videos without review where ADR 0021's narrow path applies: their files lie
    /// within the measured band, their running times agree within the tolerance, and their
    /// Established work identities do not disagree.
    ///
    /// It names nothing. The surviving Video keeps whatever each side had established — which for
    /// two Unknown Videos is nothing at all, so what comes out of the merge is one Unknown Video
    /// with two Video Files rather than a Video that has quietly acquired an identity. Both files
    /// keep their own paths, hashes, containers and running times, because those are what a later
    /// Split and any later identification have to work from.
    /// </summary>
    private async Task<bool> AssociateAsync(
        VideoFileRow left,
        VideoFileRow right,
        PerceptualNeighbourhoodRow neighbourhood,
        Dictionary<Guid, VideoFileRow> files,
        CancellationToken cancellationToken)
    {
        var leftVideo = await identification.LoadAsync(left.VideoId, cancellationToken);
        var rightVideo = await identification.LoadAsync(right.VideoId, cancellationToken);
        var leftClaim = IdentificationService.Current(
            leftVideo,
            IdentificationDimension.WorkIdentification);
        var rightClaim = IdentificationService.Current(
            rightVideo,
            IdentificationDimension.WorkIdentification);
        var disagree = leftClaim is not null &&
                       rightClaim is not null &&
                       !string.Equals(
                           leftClaim.TargetKey,
                           rightClaim.TargetKey,
                           StringComparison.OrdinalIgnoreCase);

        if (!IdentificationEvidenceRule.AssociatesAutomatically(
                neighbourhood.Distance,
                neighbourhood.DurationsAgree,
                disagree))
        {
            return false;
        }

        var settled = await ExistingAsync(left, right, cancellationToken);

        // A person has already said these two are not the same content, or has taken them apart
        // again. The rule does not overrule that by concluding it a second time from the same
        // reading; only a closer one, or running times that stop disagreeing, may reopen it.
        if (settled is not null && !Supersedes(settled, neighbourhood))
        {
            return false;
        }

        var survivor = await identification.MergeAsync(leftVideo, rightVideo, cancellationToken);
        var merged = survivor.Id == leftVideo.Id ? rightVideo : leftVideo;

        // The dictionary is this pass's picture of where each file lives, and the merge has just
        // moved some of them. A later pair in the same batch must not be read against where they
        // used to be.
        foreach (var moved in files.Values.Where(file => file.VideoId == merged.Id))
        {
            moved.VideoId = survivor.Id;
        }

        Record(
            settled,
            left,
            right,
            neighbourhood,
            WorkAssociationStatus.Established,
            survivor.Id,
            merged.Id,
            IdentificationSource.LocalInference,
            decidedBy: null,
            note: null);
        return true;
    }

    /// <summary>
    /// Puts two Unknown Videos that look alike but cannot be associated without review in front of
    /// an Administrator.
    ///
    /// Only two Unknown Videos: where one of them carries an Established work identity the review
    /// case is the Identification Candidate that identity has already proposed, and a second case
    /// asking the same question in different words is worse than none.
    /// </summary>
    private async Task ProposeAssociationAsync(
        VideoFileRow left,
        VideoFileRow right,
        PerceptualNeighbourhoodRow neighbourhood,
        CancellationToken cancellationToken)
    {
        var leftVideo = await identification.LoadAsync(left.VideoId, cancellationToken);
        var rightVideo = await identification.LoadAsync(right.VideoId, cancellationToken);

        if (IdentificationService.Current(leftVideo, IdentificationDimension.WorkIdentification)
                is not null ||
            IdentificationService.Current(rightVideo, IdentificationDimension.WorkIdentification)
                is not null)
        {
            return;
        }

        var existing = await ExistingAsync(left, right, cancellationToken);

        if (existing is { Status: WorkAssociationStatus.Proposed } ||
            existing is { Status: WorkAssociationStatus.Established })
        {
            return;
        }

        if (existing is not null && !Supersedes(existing, neighbourhood))
        {
            return;
        }

        Record(
            existing,
            left,
            right,
            neighbourhood,
            WorkAssociationStatus.Proposed,
            left.VideoId,
            right.VideoId,
            IdentificationSource.LocalInference,
            decidedBy: null,
            note: null);
    }

    /// <summary>
    /// Writes what was concluded about one pair of files, or rewrites it where the pair has been
    /// read again. One pair of files is one association whatever it is currently worth, so a
    /// rejection and the reading that reopened it are the same row rather than a second one.
    /// </summary>
    private void Record(
        WorkAssociationRow? existing,
        VideoFileRow left,
        VideoFileRow right,
        PerceptualNeighbourhoodRow neighbourhood,
        WorkAssociationStatus status,
        Guid videoId,
        Guid otherVideoId,
        IdentificationSource source,
        Guid? decidedBy,
        string? note)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var (first, second) = left.Id.CompareTo(right.Id) <= 0 ? (left, right) : (right, left);
        var association = existing;

        if (association is null)
        {
            association = new WorkAssociationRow
            {
                Id = Guid.CreateVersion7(),
                VideoFileId = first.Id,
                OtherVideoFileId = second.Id,
                CreatedAt = now,
            };
            database.WorkAssociations.Add(association);
        }

        association.Status = status;
        association.VideoId = videoId;
        association.OtherVideoId = otherVideoId;
        association.Distance = neighbourhood.Distance;
        association.DurationsAgree = neighbourhood.DurationsAgree;
        association.DurationMilliseconds = first.DurationMilliseconds;
        association.OtherDurationMilliseconds = second.DurationMilliseconds;
        association.Source = source;
        association.DecidedByAccountId = decidedBy;
        association.Note = note;
        association.EstablishedAt = status == WorkAssociationStatus.Established ? now : null;
        association.ResolvedAt = status == WorkAssociationStatus.Proposed ? null : now;
    }

    private Task<WorkAssociationRow?> ExistingAsync(
        VideoFileRow left,
        VideoFileRow right,
        CancellationToken cancellationToken)
    {
        var (first, second) = left.Id.CompareTo(right.Id) <= 0 ? (left.Id, right.Id) : (right.Id, left.Id);

        return database.WorkAssociations
            .AsTracking()
            .SingleOrDefaultAsync(
                row => row.VideoFileId == first && row.OtherVideoFileId == second,
                cancellationToken);
    }

    /// <summary>
    /// Whether a new reading of one pair is materially stronger than what was concluded from the
    /// last one. A person's rejection, or a Split, holds until the two files move closer together
    /// or their running times stop disagreeing — the same measure the neighbour rung is reopened by.
    /// </summary>
    private static bool Supersedes(
        WorkAssociationRow settled,
        PerceptualNeighbourhoodRow neighbourhood) =>
        settled.Status is not (WorkAssociationStatus.Rejected or WorkAssociationStatus.Separated) ||
        IdentificationEvidenceRule.NeighbourEvidenceSupersedesRejection(
            settled.Distance,
            settled.DurationsAgree,
            neighbourhood.Distance,
            neighbourhood.DurationsAgree);

    /// <summary>
    /// The material evidence behind a neighbour proposal is the other file. Rejecting it suppresses
    /// what that file proposes for this one; a different file that looks like it is new evidence,
    /// and a closer reading of the same file is stronger evidence rather than new evidence.
    /// </summary>
    private static string EvidenceKey(Guid neighbourVideoFileId) =>
        $"PerceptualNeighbour:{neighbourVideoFileId}";
}
