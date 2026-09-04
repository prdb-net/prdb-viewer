using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// Keeps each Video's discovery projection in step with the facts it is derived from, per ADR
/// 0013. It is called by whatever changed those facts, inside the same unit of work, so discovery
/// can never disagree with the Video page about what a Video is called or whether it is playable.
/// </summary>
public sealed class VideoProjection(ViewerDbContext database, TimeProvider timeProvider)
{
    /// <summary>
    /// Recomputes one Video from its own graph. The caller supplies a tracked Video whose files,
    /// claims and metadata are loaded; nothing here reads authority it was not given.
    /// </summary>
    public void Refresh(VideoRow video)
    {
        var label = VideoPresentation.DisplayLabel(video);
        var work = IdentificationService.Current(video, IdentificationDimension.WorkIdentification);
        var site = IdentificationService.Current(video, IdentificationDimension.SiteRecognition);
        var credits = VideoPresentation.ActorCredits(video);
        var actors = credits.Select(actor => actor.Name).ToArray();
        var files = video.VideoFiles.ToArray();
        var available = files
            .Where(file => file.Availability == VideoFileAvailability.Available)
            .ToArray();
        var title = work is null ? null : VideoPresentation.ClaimView(
            video,
            IdentificationDimension.WorkIdentification).TargetTitle;

        video.DisplayLabel = label;
        video.HasEstablishedWork = work is not null;
        video.EstablishedSite = site?.TargetTitle;
        video.NormalizedSite = site is null ? null : LibrarySearchRule.Normalize(site.TargetTitle);
        video.ReviewNeeded =
            IdentificationService.ReviewStatusOf(video, IdentificationDimension.WorkIdentification)
                == IdentificationReviewStatus.ReviewNeeded ||
            IdentificationService.ReviewStatusOf(video, IdentificationDimension.SiteRecognition)
                == IdentificationReviewStatus.ReviewNeeded;
        video.BestClassification = BestClassificationOf(
            available.Select(file => file.DirectPlayClassification));
        video.Quality = VideoQualityRule.Best(
            available.Select(file => VideoQualityRule.For(file.Width, file.Height)));
        video.Availability = AvailabilityOf(files);
        video.SearchText = SearchTextOf(label, title, video.EstablishedSite, actors, files);
        video.ProjectedAt = timeProvider.GetUtcNow().UtcDateTime;
        RefreshActors(video, credits);
    }

    /// <summary>
    /// Loads a Video with everything the projection reads and refreshes it. Used by callers that
    /// changed a fact without the whole graph in hand.
    /// </summary>
    public async Task RefreshAsync(Guid videoId, CancellationToken cancellationToken = default)
    {
        var video = await Tracked().SingleOrDefaultAsync(
            row => row.Id == videoId,
            cancellationToken);

        if (video is not null)
        {
            Refresh(video);
        }
    }

    public async Task RefreshAsync(IEnumerable<Guid> videoIds, CancellationToken cancellationToken = default)
    {
        var ids = videoIds.Distinct().ToArray();

        if (ids.Length == 0)
        {
            return;
        }

        foreach (var video in await Tracked()
                     .Where(row => ids.Contains(row.Id))
                     .ToListAsync(cancellationToken))
        {
            Refresh(video);
        }
    }

    /// <summary>
    /// Refreshes every Video whose projected facts this unit of work has changed, found from the
    /// change tracker rather than from each caller's memory of what it touched. ADR 0013 names
    /// forgetting a write path as the way this projection goes wrong; this is what makes
    /// forgetting impossible for anything that goes through the same context.
    /// </summary>
    public async Task RefreshTrackedAsync(CancellationToken cancellationToken = default)
    {
        var ids = new HashSet<Guid>();
        var unsaved = new List<VideoRow>();

        foreach (var entry in database.ChangeTracker.Entries().ToArray())
        {
            if (entry.State is EntityState.Detached or EntityState.Unchanged)
            {
                continue;
            }

            switch (entry.Entity)
            {
                // A Video this unit of work has just created is not in the database yet, so it is
                // projected from the graph in hand rather than looked for and missed.
                case VideoRow added when entry.State == EntityState.Added:
                    unsaved.Add(added);
                    break;
                case VideoRow video:
                    ids.Add(video.Id);
                    break;
                case VideoFileRow file:
                    ids.Add(file.VideoId);

                    if (file.PreviousVideoId is { } previous)
                    {
                        ids.Add(previous);
                    }

                    break;
                case IdentificationClaimRow claim:
                    ids.Add(claim.VideoId);
                    break;
                case VideoMetadataRow metadata:
                    ids.Add(metadata.VideoId);
                    break;
            }
        }

        foreach (var video in unsaved)
        {
            ids.Remove(video.Id);
            Refresh(video);
        }

        await RefreshAsync(ids, cancellationToken);
    }

    /// <summary>
    /// Rebuilds projections that have never been computed, in bounded batches. It is how an
    /// upgrade fills the new columns and how a Restore rebuilds them, without a startup that
    /// blocks on the whole library. Returns whether anything was left to do.
    /// </summary>
    public async Task<bool> RefreshOutstandingAsync(
        int batchSize = 200,
        CancellationToken cancellationToken = default)
    {
        var outstanding = await Tracked()
            .Where(video => video.ProjectedAt == null)
            .OrderBy(video => video.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (outstanding.Count == 0)
        {
            return false;
        }

        foreach (var video in outstanding)
        {
            Refresh(video);
        }

        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    private IQueryable<VideoRow> Tracked() =>
        database.Videos
            .AsTracking()
            .Include(video => video.Metadata)
            .Include(video => video.VideoFiles)
            .Include(video => video.IdentificationClaims)
            .Include(video => video.ProjectedActors);

    /// <summary>
    /// Projects one Video's Actor Credits, matching an existing row by the Actor it resolves to
    /// where there is one and by the name where there is not. prdb spelling a known Actor
    /// differently must move the row rather than leave two of them for one person, and a credit
    /// that resolves to nobody keeps behaving exactly as it did before Actors had an identity.
    /// </summary>
    private void RefreshActors(VideoRow video, IReadOnlyList<RetainedActor> credits)
    {
        var wanted = credits
            .Select(actor => (
                actor.Name,
                actor.ActorId,
                Normalized: LibrarySearchRule.Normalize(actor.Name)))
            .Where(actor => actor.Normalized.Length > 0)
            .DistinctBy(actor => actor.ActorId ?? actor.Normalized)
            // One Video crediting two Actors under one name is a name this projection cannot tell
            // apart, and the facet is keyed by the name. The first of them stands for both.
            .DistinctBy(actor => actor.Normalized)
            .ToArray();

        foreach (var existing in video.ProjectedActors.ToArray())
        {
            if (!wanted.Any(actor => Matches(actor.ActorId, actor.Normalized, existing)))
            {
                video.ProjectedActors.Remove(existing);
                database.VideoActors.Remove(existing);
            }
        }

        foreach (var actor in wanted)
        {
            var existing = video.ProjectedActors
                .FirstOrDefault(row => Matches(actor.ActorId, actor.Normalized, row));

            if (existing is null)
            {
                video.ProjectedActors.Add(new VideoActorRow
                {
                    Id = Guid.CreateVersion7(),
                    VideoId = video.Id,
                    PrdbActorId = actor.ActorId,
                    Name = actor.Name,
                    NormalizedName = actor.Normalized,
                });
                continue;
            }

            existing.PrdbActorId = actor.ActorId;
            existing.Name = actor.Name;
            existing.NormalizedName = actor.Normalized;
        }
    }

    /// <summary>
    /// Whether a projected row stands for this credit. An identity answers on its own; without
    /// one, the name does, and a row that already carries a different identity is not this credit
    /// however it is spelled.
    /// </summary>
    private static bool Matches(string? actorId, string normalized, VideoActorRow row) =>
        actorId is not null
            ? string.Equals(row.PrdbActorId, actorId, StringComparison.OrdinalIgnoreCase) ||
              (row.PrdbActorId is null && row.NormalizedName == normalized)
            : row.PrdbActorId is null && row.NormalizedName == normalized;

    private static string SearchTextOf(
        string label,
        string? title,
        string? site,
        IReadOnlyList<string> actors,
        IEnumerable<VideoFileRow> files)
    {
        var facts = new List<string> { label };

        if (title is not null)
        {
            facts.Add(title);
        }

        if (site is not null)
        {
            facts.Add(site);
        }

        facts.AddRange(actors);
        facts.AddRange(files.Select(file => Path.GetFileNameWithoutExtension(file.RelativePath)));

        return LibrarySearchRule.Normalize(string.Join(' ', facts));
    }

    /// <summary>
    /// The most playable classification among a Video's Available occurrences. One Unsupported
    /// variant beside a playable one must not make the Video look unplayable, and a Video with no
    /// Available occurrence claims nothing.
    /// </summary>
    private static DirectPlayClassification BestClassificationOf(
        IEnumerable<DirectPlayClassification> available)
    {
        var best = DirectPlayClassification.Unsupported;

        foreach (var classification in available)
        {
            if (Preference(classification) < Preference(best))
            {
                best = classification;
            }
        }

        return best;
    }

    private static int Preference(DirectPlayClassification classification) =>
        classification switch
        {
            DirectPlayClassification.BaselineCandidate => 0,
            DirectPlayClassification.ClientDependent => 1,
            DirectPlayClassification.Undetermined => 2,
            _ => 3,
        };

    public static VideoAvailability AvailabilityOf(IReadOnlyCollection<VideoFileRow> files)
    {
        if (files.Any(file => file.Availability == VideoFileAvailability.Available))
        {
            return VideoAvailability.Available;
        }

        return files.Count > 0 && files.All(file => file.Availability == VideoFileAvailability.Removed)
            ? VideoAvailability.Removed
            : VideoAvailability.Unavailable;
    }
}
