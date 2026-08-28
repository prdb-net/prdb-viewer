using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;
using Prdb.Viewer.Infrastructure.Personal;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// Turns remote identification results into Shared Library Knowledge. One applicable Conclusive
/// result may establish an Unknown claim; everything weaker, ambiguous, or contradictory becomes a
/// reviewable Identification Candidate that leaves the current claim in place.
/// </summary>
public sealed class IdentificationService(
    ViewerDbContext database,
    PersonalStateService personalState,
    TimeProvider timeProvider)
{
    public sealed record Target(string Key, string Title, string? Url);

    public async Task ApplyRemoteIdentificationAsync(
        RemoteIdentification result,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var file = await database.VideoFiles
            .AsTracking()
            .SingleOrDefaultAsync(row => row.Id == result.VideoFileId, cancellationToken);

        if (file is null)
        {
            return;
        }

        var video = await LoadAsync(file.VideoId, cancellationToken);
        var evidenceKey = EvidenceKeyOf(result, file);
        var workEvidence = IdentificationEvidenceRule.ClassifyWorkIdentification(
            result.MatchedBy,
            result.Confidence,
            result.PrdbVideoId is not null,
            result.Candidates.Count);

        if (workEvidence == IdentificationEvidenceClass.Conclusive && result.PrdbVideoId is not null)
        {
            video = await EstablishOrConflictAsync(
                video,
                IdentificationDimension.WorkIdentification,
                WorkTarget(result, result.PrdbVideoId),
                IdentificationSource.PrdbIdentification,
                workEvidence,
                result,
                file,
                evidenceKey,
                cancellationToken);
        }
        else if (workEvidence == IdentificationEvidenceClass.Suggestive)
        {
            foreach (var candidate in ProposedWorkTargets(result))
            {
                Propose(video, IdentificationDimension.WorkIdentification, candidate, workEvidence,
                    result, file, evidenceKey);
            }
        }

        var site = result.Work?.Site ?? result.Site;

        if (IdentificationEvidenceRule.ClassifySiteRecognition(site is not null) ==
            IdentificationEvidenceClass.Conclusive && site is not null)
        {
            video = await EstablishOrConflictAsync(
                video,
                IdentificationDimension.SiteRecognition,
                new Target(site.Id, site.Title, site.Url),
                IdentificationSource.PrdbIdentification,
                IdentificationEvidenceClass.Conclusive,
                result,
                file,
                evidenceKey,
                cancellationToken);
        }

        RetainMetadata(video, result);
        file.IdentifiedSha256 = file.Sha256;
        file.IdentifiedAt = Now();
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Establishes an Unknown claim from one applicable Conclusive result, confirms a claim that
    /// still holds, or records a reviewable conflict. It never replaces a current claim silently.
    /// </summary>
    public async Task<VideoRow> EstablishOrConflictAsync(
        VideoRow video,
        IdentificationDimension dimension,
        Target target,
        IdentificationSource source,
        IdentificationEvidenceClass evidence,
        RemoteIdentification? result,
        VideoFileRow? file,
        string evidenceKey,
        CancellationToken cancellationToken)
    {
        var current = Current(video, dimension);

        if (current is not null)
        {
            if (string.Equals(current.TargetKey, target.Key, StringComparison.OrdinalIgnoreCase))
            {
                current.LastConfirmedAt = Now();
                current.TargetTitle = target.Title;
                current.TargetUrl = target.Url;
                return video;
            }

            Propose(video, dimension, target, evidence, result, file, evidenceKey);
            return video;
        }

        if (dimension == IdentificationDimension.WorkIdentification)
        {
            var establishedElsewhere = await database.IdentificationClaims
                .AsNoTracking()
                .Where(claim => claim.Dimension == dimension &&
                                claim.Status == IdentificationClaimStatus.Current &&
                                claim.TargetKey == target.Key &&
                                claim.VideoId != video.Id)
                .Select(claim => claim.VideoId)
                .FirstOrDefaultAsync(cancellationToken);

            if (establishedElsewhere != Guid.Empty)
            {
                return await MergeAsync(
                    await LoadAsync(establishedElsewhere, cancellationToken),
                    video,
                    cancellationToken);
            }
        }

        AddClaim(video, dimension, target, source, evidence, result?.MatchedBy?.ToString(), file?.Id,
            administrativeOverride: false, decidedBy: null, note: null);
        return video;
    }

    /// <summary>
    /// Merges two Videos that carry the same established work identity. The earlier Discovery Date
    /// survives, both histories are retained, and Personal State is reconciled without exposing it.
    /// </summary>
    public async Task<VideoRow> MergeAsync(
        VideoRow left,
        VideoRow right,
        CancellationToken cancellationToken)
    {
        var survivor = Survivor(left, right);
        var merged = ReferenceEquals(survivor, left) ? right : left;

        if (survivor.Id == merged.Id)
        {
            return survivor;
        }

        var files = await database.VideoFiles
            .AsTracking()
            .Where(file => file.VideoId == merged.Id)
            .ToListAsync(cancellationToken);

        foreach (var file in files)
        {
            file.PreviousVideoId = merged.Id;
            file.VideoId = survivor.Id;
        }

        var claims = await database.IdentificationClaims
            .AsTracking()
            .Where(claim => claim.VideoId == merged.Id)
            .ToListAsync(cancellationToken);
        var candidates = await database.IdentificationCandidates
            .AsTracking()
            .Where(candidate => candidate.VideoId == merged.Id)
            .ToListAsync(cancellationToken);

        foreach (var claim in claims)
        {
            claim.VideoId = survivor.Id;

            if (claim.Status == IdentificationClaimStatus.Current &&
                Current(survivor, claim.Dimension) is not null)
            {
                claim.Status = IdentificationClaimStatus.Superseded;
                claim.EndedAt = Now();
            }

            merged.IdentificationClaims.Remove(claim);
            Attach(survivor.IdentificationClaims, claim);
        }

        foreach (var candidate in candidates)
        {
            candidate.VideoId = survivor.Id;
            merged.IdentificationCandidates.Remove(candidate);
            Attach(survivor.IdentificationCandidates, candidate);
        }

        await database.IdentificationDecisions
            .Where(decision => decision.VideoId == merged.Id)
            .ExecuteUpdateAsync(
                update => update.SetProperty(decision => decision.VideoId, survivor.Id),
                cancellationToken);
        await database.PlaybackAttempts
            .Where(attempt => attempt.VideoId == merged.Id)
            .ExecuteUpdateAsync(
                update => update.SetProperty(attempt => attempt.VideoId, survivor.Id),
                cancellationToken);
        await personalState.ReconcileMergedVideoAsync(survivor.Id, merged.Id, cancellationToken);

        if (survivor.Metadata is null && merged.Metadata is not null)
        {
            var metadata = merged.Metadata;
            database.VideoMetadata.Remove(metadata);
            await database.SaveChangesAsync(cancellationToken);
            database.VideoMetadata.Add(new VideoMetadataRow
            {
                VideoId = survivor.Id,
                PrdbVideoId = metadata.PrdbVideoId,
                Title = metadata.Title,
                SiteId = metadata.SiteId,
                SiteTitle = metadata.SiteTitle,
                SiteUrl = metadata.SiteUrl,
                ActorsJson = metadata.ActorsJson,
                ArtworkUrl = metadata.ArtworkUrl,
                ReleaseDate = metadata.ReleaseDate,
                DurationMilliseconds = metadata.DurationMilliseconds,
                FetchedAt = metadata.FetchedAt,
            });
        }

        merged.SurvivingVideoId = survivor.Id;
        merged.MergedAt = Now();
        survivor.DiscoveryDate = survivor.DiscoveryDate <= merged.DiscoveryDate
            ? survivor.DiscoveryDate
            : merged.DiscoveryDate;
        survivor.CaseVersion++;
        return survivor;
    }

    public void AddClaim(
        VideoRow video,
        IdentificationDimension dimension,
        Target target,
        IdentificationSource source,
        IdentificationEvidenceClass evidence,
        string? matchedBy,
        Guid? supportingVideoFileId,
        bool administrativeOverride,
        Guid? decidedBy,
        string? note)
    {
        var now = Now();
        var claim = new IdentificationClaimRow
        {
            Id = Guid.CreateVersion7(),
            VideoId = video.Id,
            Dimension = dimension,
            Status = IdentificationClaimStatus.Current,
            TargetKey = target.Key,
            TargetTitle = target.Title,
            TargetUrl = target.Url,
            Source = source,
            EvidenceClass = evidence,
            MatchedBy = matchedBy,
            IsAdministrativeOverride = administrativeOverride,
            SupportingVideoFileId = supportingVideoFileId,
            DecidedByAccountId = decidedBy,
            Note = note,
            EstablishedAt = now,
            LastConfirmedAt = now,
        };
        video.IdentificationClaims.Add(claim);
        video.CaseVersion++;
    }

    public void Propose(
        VideoRow video,
        IdentificationDimension dimension,
        Target target,
        IdentificationEvidenceClass evidence,
        RemoteIdentification? result,
        VideoFileRow? file,
        string evidenceKey)
    {
        var current = Current(video, dimension);
        var reason = current is null
            ? IdentificationReviewReason.SuggestiveEvidence
            : current.IsAdministrativeOverride
                ? IdentificationReviewReason.ConflictsWithAdministrativeOverride
                : evidence == IdentificationEvidenceClass.Conclusive
                    ? IdentificationReviewReason.ConflictingConclusiveEvidence
                    : IdentificationReviewReason.SuggestiveEvidence;
        var existing = video.IdentificationCandidates
            .DistinctBy(candidate => candidate.Id)
            .Where(candidate => candidate.Dimension == dimension &&
                                string.Equals(
                                    candidate.TargetKey,
                                    target.Key,
                                    StringComparison.OrdinalIgnoreCase) &&
                                candidate.EvidenceKey == evidenceKey)
            .ToArray();

        if (existing.Any(candidate => candidate.Status == IdentificationCandidateStatus.Pending))
        {
            return;
        }

        var rejected = existing
            .Where(candidate => candidate.Status == IdentificationCandidateStatus.Rejected)
            .OrderByDescending(candidate => candidate.ResolvedAt)
            .FirstOrDefault();

        if (rejected is not null && evidence <= rejected.EvidenceClass)
        {
            return;
        }

        var candidateRow = new IdentificationCandidateRow
        {
            Id = Guid.CreateVersion7(),
            VideoId = video.Id,
            Dimension = dimension,
            Status = IdentificationCandidateStatus.Pending,
            TargetKey = target.Key,
            TargetTitle = target.Title,
            TargetUrl = target.Url,
            EvidenceClass = evidence,
            Reason = reason,
            MatchedBy = result?.MatchedBy?.ToString(),
            Confidence = result?.Confidence.ToString(),
            EvidenceKey = evidenceKey,
            SupportingVideoFileId = file?.Id,
            PriorRejectionId = rejected?.Id,
            CreatedAt = Now(),
        };
        video.IdentificationCandidates.Add(candidateRow);
        video.CaseVersion++;
    }

    public async Task<VideoRow> LoadAsync(Guid videoId, CancellationToken cancellationToken) =>
        await database.Videos
            .AsTracking()
            .Include(video => video.Metadata)
            .Include(video => video.IdentificationClaims)
            .Include(video => video.IdentificationCandidates)
            .SingleAsync(video => video.Id == videoId, cancellationToken);

    public static IdentificationClaimRow? Current(
        VideoRow video,
        IdentificationDimension dimension) =>
        video.IdentificationClaims
            .Where(claim => claim.Dimension == dimension &&
                            claim.Status == IdentificationClaimStatus.Current)
            .DistinctBy(claim => claim.Id)
            .SingleOrDefault();

    private static void Attach<T>(ICollection<T> collection, T row)
    {
        if (!collection.Contains(row))
        {
            collection.Add(row);
        }
    }

    public static IdentificationReviewStatus ReviewStatusOf(
        VideoRow video,
        IdentificationDimension dimension) =>
        video.IdentificationCandidates.Any(candidate =>
            candidate.Dimension == dimension &&
            candidate.Status == IdentificationCandidateStatus.Pending)
            ? IdentificationReviewStatus.ReviewNeeded
            : IdentificationReviewStatus.Clear;

    private void RetainMetadata(VideoRow video, RemoteIdentification result)
    {
        var work = result.Work;
        var current = Current(video, IdentificationDimension.WorkIdentification);

        if (work is null ||
            current is null ||
            !string.Equals(current.TargetKey, work.PrdbVideoId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var metadata = video.Metadata;

        if (metadata is null)
        {
            metadata = new VideoMetadataRow
            {
                VideoId = video.Id,
                PrdbVideoId = work.PrdbVideoId,
                Title = work.Title,
            };
            database.VideoMetadata.Add(metadata);
            video.Metadata = metadata;
        }

        metadata.PrdbVideoId = work.PrdbVideoId;
        metadata.Title = work.Title;
        metadata.SiteId = work.Site?.Id;
        metadata.SiteTitle = work.Site?.Title;
        metadata.SiteUrl = work.Site?.Url;
        metadata.ActorsJson = work.Actors.Count == 0 ? null : JsonSerializer.Serialize(work.Actors);
        metadata.ArtworkUrl = work.ArtworkUrl;
        metadata.ReleaseDate = work.ReleaseDate;
        metadata.DurationMilliseconds = work.DurationMilliseconds;
        metadata.FetchedAt = Now();
    }

    private static VideoRow Survivor(VideoRow left, VideoRow right)
    {
        if (left.DiscoveryDate != right.DiscoveryDate)
        {
            return left.DiscoveryDate < right.DiscoveryDate ? left : right;
        }

        return left.Id.CompareTo(right.Id) <= 0 ? left : right;
    }

    private static Target WorkTarget(RemoteIdentification result, string prdbVideoId) =>
        new(prdbVideoId,
            result.Work?.Title ?? $"prdb video {prdbVideoId}",
            result.Work is null ? null : $"https://prdb.net/videos/{prdbVideoId}");

    private static IEnumerable<Target> ProposedWorkTargets(RemoteIdentification result)
    {
        if (result.PrdbVideoId is not null)
        {
            yield return WorkTarget(result, result.PrdbVideoId);
            yield break;
        }

        foreach (var candidate in result.Candidates.Take(10))
        {
            yield return WorkTarget(result, candidate);
        }
    }

    private static string EvidenceKeyOf(RemoteIdentification result, VideoFileRow file) =>
        result.MatchedBy switch
        {
            RemoteMatchKind.OsHash => $"OsHash:{file.OsHash}",
            RemoteMatchKind.PerceptualHash => $"PerceptualHash:{file.PerceptualHash}",
            RemoteMatchKind.Filename => $"Filename:{Path.GetFileName(file.RelativePath)}",
            RemoteMatchKind.ReleaseName => $"ReleaseName:{Path.GetFileName(file.RelativePath)}",
            RemoteMatchKind.Site => $"Site:{Path.GetFileName(file.RelativePath)}",
            _ => "None",
        };

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
}
