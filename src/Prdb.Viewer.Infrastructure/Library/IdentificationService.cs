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
    VideoProjection projection,
    TimeProvider timeProvider)
{
    public sealed record Target(string Key, string Title, string? Url);

    /// <summary>How a locally recognised Site was matched, as the review surfaces name it.</summary>
    public const string LocalSiteMatchedBy = "the file's own path";

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
            // The answer that produced these proposals carried the details of at most one work.
            // Whichever target names it gets them; the others are an identifier and a title, and
            // the review says so rather than showing one work's Actors beside another's name.
            var described = await RetainProposedWorkAsync(result.Work, cancellationToken);

            foreach (var candidate in ProposedWorkTargets(result))
            {
                Propose(video, IdentificationDimension.WorkIdentification, candidate, workEvidence,
                    result, file, evidenceKey,
                    proposedWorkId: described is not null && string.Equals(
                        described.PrdbVideoId,
                        candidate.Key,
                        StringComparison.OrdinalIgnoreCase)
                        ? described.Id
                        : null);
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

        // The claim, the retained metadata and the file's own association have all just moved, so
        // the discovery projection is rebuilt inside the same transaction (ADR 0013).
        await projection.RefreshTrackedAsync(cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Turns what a Video File's own path says about its originating site into Shared Library
    /// Knowledge. A path that maps uniquely to one known site may establish an Unknown Site
    /// Recognition; an ambiguous or weak reading only proposes. Neither replaces an Established
    /// claim, so a prdb-established Site keeps its place and a disagreement goes to review.
    /// </summary>
    public async Task ApplyLocalSiteRecognitionAsync(
        Guid videoFileId,
        LocalSiteRecognition recognition,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var file = await database.VideoFiles
            .AsTracking()
            .SingleOrDefaultAsync(row => row.Id == videoFileId, cancellationToken);

        if (file is null)
        {
            return;
        }

        var video = await LoadAsync(file.VideoId, cancellationToken);

        switch (recognition.Evidence)
        {
            case IdentificationEvidenceClass.Conclusive:
                var match = recognition.Matches[0];
                await EstablishOrConflictAsync(
                    video,
                    IdentificationDimension.SiteRecognition,
                    SiteTarget(match),
                    IdentificationSource.LocalInference,
                    IdentificationEvidenceClass.Conclusive,
                    result: null,
                    file,
                    LocalEvidenceKey(match),
                    cancellationToken,
                    LocalSiteMatchedBy);
                break;

            case IdentificationEvidenceClass.Suggestive:
                foreach (var proposal in recognition.Matches.DistinctBy(
                             candidate => candidate.Site.Key,
                             StringComparer.OrdinalIgnoreCase))
                {
                    Propose(
                        video,
                        IdentificationDimension.SiteRecognition,
                        SiteTarget(proposal),
                        IdentificationEvidenceClass.Suggestive,
                        result: null,
                        file,
                        LocalEvidenceKey(proposal),
                        IdentificationSource.LocalInference,
                        LocalSiteMatchedBy);
                }

                break;
        }

        // The path has been read as it stands now, whatever it produced. A later rename is a
        // different path and is read again; an unchanged one is not read twice.
        file.SiteRecognisedPath = file.RelativePath;
        file.SiteRecognisedAt = Now();

        await projection.RefreshTrackedAsync(cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// The material evidence behind a local proposal is the name the path actually used. Rejecting
    /// it suppresses that name for that site; a differently named path is new evidence.
    /// </summary>
    private static string LocalEvidenceKey(LocalSiteMatch match) => $"LocalSiteName:{match.Alias}";

    private static IdentificationService.Target SiteTarget(LocalSiteMatch match) =>
        new(match.Site.Key, match.Site.Title, match.Site.Url);

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
        CancellationToken cancellationToken,
        string? matchedBy = null)
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

            if (IdentificationEvidenceRule.SupersedesAutomatically(
                    current.Source,
                    current.IsAdministrativeOverride,
                    source,
                    evidence))
            {
                current.Status = IdentificationClaimStatus.Superseded;
                current.EndedAt = Now();
                AddClaim(video, dimension, target, source, evidence,
                    matchedBy ?? result?.MatchedBy?.ToString(), file?.Id,
                    administrativeOverride: false, decidedBy: null, note: null);
                return video;
            }

            Propose(video, dimension, target, evidence, result, file, evidenceKey, source, matchedBy);
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

        AddClaim(video, dimension, target, source, evidence,
            matchedBy ?? result?.MatchedBy?.ToString(), file?.Id,
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
        string evidenceKey,
        IdentificationSource source = IdentificationSource.PrdbIdentification,
        string? matchedBy = null,
        Guid? proposedWorkId = null)
    {
        var current = Current(video, dimension);

        // A proposal naming what is already established is the same answer arriving twice, not a
        // decision. Local recognition reads a Video File's own path whether or not prdb has
        // answered the same question, so this is ordinary rather than exceptional — and a
        // candidate made of it can never be accepted: where the Site came with the Work
        // Identification the review refuses every decision that would establish one. It is
        // therefore recorded the way agreeing evidence is recorded everywhere else, by confirming
        // the claim it agrees with. Its own words are not written over the claim's: the proposal
        // agrees with what is established, it does not rename it.
        if (current is not null &&
            string.Equals(current.TargetKey, target.Key, StringComparison.OrdinalIgnoreCase))
        {
            current.LastConfirmedAt = Now();
            return;
        }

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
            Source = source,
            MatchedBy = matchedBy ?? result?.MatchedBy?.ToString(),
            Confidence = result?.Confidence.ToString(),
            EvidenceKey = evidenceKey,
            SupportingVideoFileId = file?.Id,
            ProposedWorkId = proposedWorkId,
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

    /// <summary>
    /// Keeps what prdb said about a work a candidate proposes, so that the review can compare the
    /// Video against the work rather than a file name against a title.
    /// </summary>
    /// <remarks>
    /// The facts are keyed by the remote work, not by the candidate: two Videos proposing the same
    /// one share them, and one picture is fetched rather than two. A later answer refreshes them.
    /// A changed picture is asked for again, and until it arrives nothing is served: the retained
    /// file is still there, but it is no longer the picture prdb offers for this work, and a review
    /// that says a picture has not arrived is more use than one showing the wrong one.
    /// </remarks>
    private async Task<ProposedWorkRow?> RetainProposedWorkAsync(
        RemoteWork? work,
        CancellationToken cancellationToken)
    {
        if (work is null)
        {
            return null;
        }

        var retained = await database.ProposedWorks
            .AsTracking()
            .SingleOrDefaultAsync(row => row.PrdbVideoId == work.PrdbVideoId, cancellationToken);

        if (retained is null)
        {
            retained = new ProposedWorkRow
            {
                Id = Guid.CreateVersion7(),
                PrdbVideoId = work.PrdbVideoId,
                Title = work.Title,
            };
            database.ProposedWorks.Add(retained);
        }

        var changedArtwork = !string.Equals(
            retained.ArtworkUrl,
            work.ArtworkUrl,
            StringComparison.Ordinal);

        retained.Title = work.Title;
        retained.SiteTitle = work.Site?.Title;
        retained.SiteUrl = work.Site?.Url;
        retained.ActorsJson = Retained(work.Actors);
        retained.ArtworkUrl = work.ArtworkUrl;
        retained.ReleaseDate = work.ReleaseDate;
        retained.DurationMilliseconds = work.DurationMilliseconds;
        retained.FetchedAt = Now();

        if (changedArtwork)
        {
            retained.ArtworkState = work.ArtworkUrl is null
                ? ProposedWorkArtworkState.None
                : ProposedWorkArtworkState.Pending;
        }

        return retained;
    }

    /// <summary>
    /// Rewrites what is retained about a Video's Established Work from a fresh prdb answer, and
    /// records that the work has been asked about in its own right.
    /// </summary>
    /// <remarks>
    /// It is the Enrichment lane's way in, and it decides nothing: an answer about a work this
    /// Video is no longer identified as is dropped rather than applied, no claim moves, no
    /// candidate is proposed, and no evidence is weighed. The facts are refreshed or they are not.
    /// </remarks>
    public async Task<bool> RefreshRetainedWorkAsync(
        Guid videoId,
        RemoteWork work,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var video = await LoadAsync(videoId, cancellationToken);

        if (!RetainMetadata(video, work))
        {
            return false;
        }

        video.Metadata!.EnrichedAt = Now();

        // The retained facts feed the discovery projection — the Actor Credits above all — so it
        // is rebuilt inside the same transaction, as every other write path does (ADR 0013).
        await projection.RefreshTrackedAsync(cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private void RetainMetadata(VideoRow video, RemoteIdentification result)
    {
        if (result.Work is { } work)
        {
            RetainMetadata(video, work);
        }
    }

    /// <summary>
    /// Retains what prdb says about a work, but only while the Video is still identified as that
    /// work. A corrected identification must not keep collecting the superseded work's facts.
    /// </summary>
    private bool RetainMetadata(VideoRow video, RemoteWork work)
    {
        var current = Current(video, IdentificationDimension.WorkIdentification);

        if (current is null ||
            !string.Equals(current.TargetKey, work.PrdbVideoId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
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
        metadata.ActorsJson = Retained(work.Actors);
        metadata.ArtworkUrl = work.ArtworkUrl;
        metadata.ReleaseDate = work.ReleaseDate;
        metadata.DurationMilliseconds = work.DurationMilliseconds;
        metadata.FetchedAt = Now();
        return true;
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

    /// <summary>
    /// The Actor Credits of a remote work as a retained document holds them: the name prdb spells
    /// and the identity it sent with it, and nothing prdb's Actor Profile is the place for.
    /// </summary>
    private static string? Retained(IReadOnlyList<RemoteActor> actors) =>
        RetainedActors.Serialize(
            actors.Select(actor => new RetainedActor(actor.Name, actor.Id)).ToArray());

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
}
