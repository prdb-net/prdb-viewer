using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Configuration;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// Feed-led refresh of locally held IDs. One slice probes a boundary, repairs <=50 local IDs,
/// or reads one <=1000-row feed page. Four catch-up pages exhaust a five-minute request budget.
/// </summary>
public sealed class VideoCatalogueRefresh(ViewerDbContext database, IPrdbVideoFeedClient feed,
    IPrdbWorkDetailClient details, IdentificationService identification, TimeProvider timeProvider, PrdbEndpoint endpoint)
{
    public async Task<bool> RunNextSliceAsync(CancellationToken cancellationToken = default)
    {
        var configuration = await database.InstallationConfigurations.AsTracking().SingleAsync(cancellationToken);
        if (configuration.BackgroundWorkPaused ||
            (!await database.VideoMetadata.AnyAsync(cancellationToken) && !await database.ProposedWorks.AnyAsync(cancellationToken))) return false;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var state = await database.CatalogueFeedStates.AsTracking().SingleOrDefaultAsync(row => row.Feed == "videos", cancellationToken);
        if (state is null)
        {
            var previousGeneration = await database.VideoMetadata.Select(row => (int?)row.CatalogueRepairGeneration)
                .Union(database.ProposedWorks.Select(row => (int?)row.CatalogueRepairGeneration))
                .MaxAsync(cancellationToken) ?? 0;
            state = new CatalogueFeedStateRow
            {
                Feed = "videos",
                Since = CatalogueCursor.Baseline.Since,
                RepairGeneration = previousGeneration
            };
            database.CatalogueFeedStates.Add(state);
        }
        var credential = configuration.ActivePrdbCredential;
        if (string.IsNullOrEmpty(credential))
        {
            state.Phase = "Waiting for a verified installation credential";
            await database.SaveChangesAsync(cancellationToken);
            return false;
        }
        var authority = CatalogueCursor.Authority(endpoint.BaseUrl, credential);
        if (state.AuthorityFingerprint != authority ||
            (state.LastAppliedAt is { } last && now - last >= TimeSpan.FromDays(90)))
        {
            state.AuthorityFingerprint = authority; state.RepairGeneration++;
            state.BaselineComplete = false; state.Since = CatalogueCursor.Baseline.Since; state.SinceId = null;
            state.NextAttemptAt = null; state.ServerTime = null; state.BurstPages = 0;
            state.Phase = "Establishing the settled Video feed boundary";
            await database.SaveChangesAsync(cancellationToken);
        }
        if (state.Phase == "Credential rejected" ||
            (state.NextAttemptAt > now && state.Phase is not "Current" && !state.Phase.StartsWith("Catch-up budget", StringComparison.Ordinal))) return false;
        if (state.ServerTime is null)
        {
            // A deliberately future lower bound returns an empty page and the authoritative
            // settled server boundary. No remote Video baseline is requested or persisted.
            var boundary = await feed.FetchAsync(credential, CatalogueCursor.At(DateTimeOffset.MaxValue, null), cancellationToken);
            if (boundary.Status != WorkDetailFetchStatus.Fetched || boundary.ServerTime is null)
                return await DeferAsync(state, configuration, boundary.Status, boundary.RetryAfter, now, cancellationToken);
            await database.Entry(configuration).ReloadAsync(cancellationToken);
            if (configuration.ActivePrdbCredential != credential || configuration.BackgroundWorkPaused) return true;
            state.Since = CatalogueCursor.At(boundary.ServerTime.Value, null).Since;
            state.ServerTime = state.Since; state.SinceId = null; state.LastAppliedAt = now;
            state.Phase = "Repairing locally retained work IDs";
            state.NextAttemptAt = boundary.RetryAfter is { } delay ? now + delay : null;
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }
        var generation = state.RepairGeneration;
        var repair = await database.VideoMetadata.Where(row => row.CatalogueRepairGeneration < generation)
            .Select(row => row.PrdbVideoId)
            .Union(database.ProposedWorks.Where(row => row.CatalogueRepairGeneration < generation).Select(row => row.PrdbVideoId))
            .OrderBy(id => id).Take(details.BatchLimit).ToArrayAsync(cancellationToken);
        if (repair.Length > 0)
        {
            var answer = await details.FetchAsync(credential, repair, cancellationToken);
            if (answer.Status != WorkDetailFetchStatus.Fetched)
                return await DeferAsync(state, configuration, answer.Status, null, now, cancellationToken);
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            await database.Entry(state).ReloadAsync(cancellationToken);
            await database.Entry(configuration).ReloadAsync(cancellationToken);
            if (state.RepairGeneration != generation || configuration.ActivePrdbCredential != credential || configuration.BackgroundWorkPaused) return true;
            foreach (var work in answer.Works.Where(work => repair.Contains(work.PrdbVideoId)))
                await ApplyWorkAsync(work, cancellationToken);
            // Silence from a detail batch is never deletion. Mark the attempt so one missing ID
            // cannot starve the other local IDs or create an immediate request loop.
            await database.VideoMetadata.Where(row => repair.Contains(row.PrdbVideoId)).ExecuteUpdateAsync(update => update
                .SetProperty(row => row.CatalogueRepairGeneration, generation).SetProperty(row => row.EnrichedAt, now), cancellationToken);
            await database.ProposedWorks.Where(row => repair.Contains(row.PrdbVideoId)).ExecuteUpdateAsync(update => update
                .SetProperty(row => row.CatalogueRepairGeneration, generation), cancellationToken);
            state.Phase = "Repairing locally retained work IDs"; state.LastAppliedAt = now;
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        if (state.NextAttemptAt > now) return false;
        state.BaselineComplete = true;
        var cursor = new CatalogueCursor(state.Since, state.SinceId);
        var page = await feed.FetchAsync(credential, cursor, cancellationToken);
        if (page.Status != WorkDetailFetchStatus.Fetched || page.Cursor is null)
            return await DeferAsync(state, configuration, page.Status, page.RetryAfter, now, cancellationToken);
        await using (var transaction = await database.Database.BeginTransactionAsync(cancellationToken))
        {
            await database.Entry(state).ReloadAsync(cancellationToken);
            await database.Entry(configuration).ReloadAsync(cancellationToken);
            if (state.Since != cursor.Since || state.SinceId != cursor.SinceId || configuration.ActivePrdbCredential != credential || configuration.BackgroundWorkPaused) return true;
            var ids = page.Changes.Select(change => change.Id).ToArray();
            var held = (await database.VideoMetadata.Where(row => ids.Contains(row.PrdbVideoId)).Select(row => row.PrdbVideoId)
                .Union(database.ProposedWorks.Where(row => ids.Contains(row.PrdbVideoId)).Select(row => row.PrdbVideoId))
                .ToListAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var change in page.Changes.Where(change => held.Contains(change.Id)))
            {
                if (change.Work is { } work) await ApplyWorkAsync(work, cancellationToken);
                await ObserveAsync(change, now, cancellationToken);
            }
            state.Since = page.Cursor.Since; state.SinceId = page.Cursor.SinceId;
            state.ServerTime = page.ServerTime is { } server ? CatalogueCursor.At(server, null).Since : state.ServerTime;
            state.LastAppliedAt = now; state.CatchUpPending = page.HasMore; state.BaselineComplete = true;
            state.BurstPages++;
            state.Phase = page.HasMore ? "Catching up with Video changes" : "Current";
            if (!page.HasMore) { state.CompletedAt = now; state.BurstPages = 0; }
            state.NextAttemptAt = page.RetryAfter is { } delay ? now + delay :
                !page.HasMore ? now + TimeSpan.FromMinutes(15) :
                state.BurstPages >= 4 ? now + TimeSpan.FromMinutes(5) : null;
            if (state.BurstPages >= 4) { state.BurstPages = 0; state.Phase = "Catch-up budget reached; detail repair remains available"; }
            if (configuration.PrdbConnectionStatus == PrdbConnectionStatus.Degraded)
                configuration.PrdbConnectionStatus = PrdbConnectionStatus.Verified;
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        return true;
    }

    private async Task ApplyWorkAsync(RemoteWork work, CancellationToken cancellationToken)
    {
        var videos = await database.VideoMetadata.Where(row => row.PrdbVideoId == work.PrdbVideoId)
            .Select(row => row.VideoId).ToListAsync(cancellationToken);
        foreach (var video in videos) await identification.RefreshRetainedWorkAsync(video, work, cancellationToken);
        if (await database.ProposedWorks.AnyAsync(row => row.PrdbVideoId == work.PrdbVideoId, cancellationToken))
            await identification.RetainProposedWorkAsync(work, cancellationToken);
    }

    private async Task ObserveAsync(RemoteWorkChange change, DateTime now, CancellationToken cancellationToken)
    {
        var state = change.IsDeleted ? change.MergedIntoId is null ? RemoteCatalogueState.Deleted : RemoteCatalogueState.Merged : RemoteCatalogueState.Active;
        await database.VideoMetadata.Where(row => row.PrdbVideoId == change.Id).ExecuteUpdateAsync(update => update
            .SetProperty(row => row.RemoteState, state).SetProperty(row => row.RemoteObservedAt, now)
            .SetProperty(row => row.RemoteUpdatedAt, change.UpdatedAt.UtcDateTime)
            .SetProperty(row => row.RemoteDeletedAt, change.DeletedAt.HasValue ? change.DeletedAt.Value.UtcDateTime : (DateTime?)null)
            .SetProperty(row => row.RemoteMergedIntoId, change.MergedIntoId), cancellationToken);
        await database.ProposedWorks.Where(row => row.PrdbVideoId == change.Id).ExecuteUpdateAsync(update => update
            .SetProperty(row => row.RemoteState, state).SetProperty(row => row.RemoteObservedAt, now)
            .SetProperty(row => row.RemoteUpdatedAt, change.UpdatedAt.UtcDateTime)
            .SetProperty(row => row.RemoteDeletedAt, change.DeletedAt.HasValue ? change.DeletedAt.Value.UtcDateTime : (DateTime?)null)
            .SetProperty(row => row.RemoteMergedIntoId, change.MergedIntoId), cancellationToken);
    }

    private async Task<bool> DeferAsync(CatalogueFeedStateRow state, InstallationConfigurationRow configuration,
        WorkDetailFetchStatus status, TimeSpan? retry, DateTime now, CancellationToken cancellationToken)
    {
        state.Phase = status == WorkDetailFetchStatus.Rejected ? "Credential rejected" : "Waiting for prdb";
        state.NextAttemptAt = status == WorkDetailFetchStatus.Rejected ? null : now + (retry ?? TimeSpan.FromMinutes(5));
        if (status == WorkDetailFetchStatus.Rejected) configuration.PrdbConnectionStatus = PrdbConnectionStatus.Rejected;
        else if (configuration.PrdbConnectionStatus == PrdbConnectionStatus.Verified) configuration.PrdbConnectionStatus = PrdbConnectionStatus.Degraded;
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }
}

/// <summary>The local enrichment slice always gets its turn even when global feed catch-up is large.</summary>
public sealed class EnrichmentCoordinator(VideoCatalogueRefresh catalogue, EnrichmentRunner enrichment,
    ViewerDbContext database, ActorProfileRetention actors, WorkImageRetention images,
    ProposedWorkArtworkRetention proposedImages)
{
    public async Task<bool> RunNextSliceAsync(CancellationToken cancellationToken = default)
    {
        var remote = await catalogue.RunNextSliceAsync(cancellationToken);
        if (remote && await database.CatalogueFeedStates.AnyAsync(row => row.Feed == "videos" &&
                row.Phase != "Waiting for prdb" && row.Phase != "Credential rejected", cancellationToken))
        {
            await actors.EnsureActorsAsync(cancellationToken);
            await images.RetainAsync(cancellationToken);
            await proposedImages.RetainAsync(cancellationToken);
        }
        var local = await enrichment.RunNextSliceAsync(cancellationToken);
        return remote || local;
    }
}
