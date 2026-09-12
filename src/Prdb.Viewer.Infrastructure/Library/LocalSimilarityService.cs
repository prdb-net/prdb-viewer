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
    VideoProjection projection)
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

            await OfferAsync(left, right, neighbourhood, cancellationToken);
            await OfferAsync(right, left, neighbourhood, cancellationToken);
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
    /// The material evidence behind a neighbour proposal is the other file. Rejecting it suppresses
    /// what that file proposes for this one; a different file that looks like it is new evidence,
    /// and a closer reading of the same file is stronger evidence rather than new evidence.
    /// </summary>
    private static string EvidenceKey(Guid neighbourVideoFileId) =>
        $"PerceptualNeighbour:{neighbourVideoFileId}";
}
