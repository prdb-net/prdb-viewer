using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

public enum SiteDirectoryRefreshVerdict
{
    Refreshed,
    Pending,
    NoCredential,
    Refused,
    Unreachable,
}

public sealed record SiteDirectoryRefreshResult(
    SiteDirectoryRefreshVerdict Verdict,
    int SiteCount,
    string? Detail = null,
    TimeSpan? RetryAfter = null);

/// <summary>
/// The retained Site Directory: the vocabulary local Site Recognition reads Video File paths
/// against. It is a regenerable copy of what prdb publishes, refreshed at most once a day, joined
/// with every Site this installation has already established for itself so that recognition keeps
/// working while prdb is unreachable or its credential is gone.
/// </summary>
public sealed class SiteDirectory(
    ViewerDbContext database,
    IPrdbSiteDirectoryClient client,
    TimeProvider timeProvider,
    Prdb.Viewer.Infrastructure.Configuration.PrdbEndpoint? endpoint = null)
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(24);

    /// <summary>
    /// Whether the retained copy is old enough to be worth one request. A directory that has never
    /// been fetched is always stale, which is what makes the first run fetch it.
    /// </summary>
    public async Task<bool> IsStaleAsync(CancellationToken cancellationToken = default)
    {
        var configuration = await database.InstallationConfigurations.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var state = await database.CatalogueFeedStates.AsNoTracking().SingleOrDefaultAsync(row => row.Feed == "sites", cancellationToken);
        if (state is not null && (!state.BaselineComplete ||
            (!string.IsNullOrEmpty(configuration?.ActivePrdbCredential) && state.AuthorityFingerprint !=
                CatalogueCursor.Authority((endpoint ?? new Prdb.Viewer.Infrastructure.Configuration.PrdbEndpoint()).BaseUrl,
                    configuration.ActivePrdbCredential)))) return true;
        var fetchedAt = configuration?.SiteDirectoryFetchedAt;
        return fetchedAt is null || Now() - fetchedAt.Value >= RefreshInterval;
    }

    public async Task<bool> IsEmptyAsync(CancellationToken cancellationToken = default) =>
        !await database.SiteDirectoryEntries.AsNoTracking().AnyAsync(row => !row.IsDeleted, cancellationToken);

    /// <summary>
    /// Replaces the retained copy with what prdb currently publishes. A refusal or an outage keeps
    /// the copy that is already there, because a stale vocabulary recognises more than none.
    /// </summary>
    public async Task<SiteDirectoryRefreshResult> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        var configuration = await database.InstallationConfigurations
            .AsTracking()
            .SingleAsync(cancellationToken);
        var credential = configuration.ActivePrdbCredential;

        if (string.IsNullOrEmpty(credential))
        {
            return new SiteDirectoryRefreshResult(SiteDirectoryRefreshVerdict.NoCredential, 0);
        }

        var now = Now();
        var state = await database.CatalogueFeedStates.AsTracking()
            .SingleOrDefaultAsync(row => row.Feed == "sites", cancellationToken);
        var authority = CatalogueCursor.Authority((endpoint ?? new Prdb.Viewer.Infrastructure.Configuration.PrdbEndpoint()).BaseUrl, credential);
        if (state?.AuthorityFingerprint == authority && state.NextAttemptAt is { } due && due > now)
            return new(SiteDirectoryRefreshVerdict.Pending, 0, "The Site feed is waiting for its next permitted request.", due - now);
        await using (var preparation = await database.Database.BeginTransactionAsync(cancellationToken))
        {
            if (state is null)
            {
                state = new CatalogueFeedStateRow { Feed = "sites", Since = CatalogueCursor.Baseline.Since, AuthorityFingerprint = authority };
                database.CatalogueFeedStates.Add(state);
                await database.SiteDirectoryStaging.ExecuteDeleteAsync(cancellationToken);
            }
            else if (state.AuthorityFingerprint != authority ||
                     (state.LastAppliedAt is { } applied && now - applied >= TimeSpan.FromDays(90)))
            {
                state.Since = CatalogueCursor.Baseline.Since; state.SinceId = null; state.AuthorityFingerprint = authority;
                state.BaselineComplete = false; state.AuthorityFingerprint = authority;
                await database.SiteDirectoryStaging.ExecuteDeleteAsync(cancellationToken);
            }
            await database.SaveChangesAsync(cancellationToken);
            await preparation.CommitAsync(cancellationToken);
        }
        var starting = new CatalogueCursor(state.Since, state.SinceId);
        var result = await client.FetchPageAsync(credential, starting, cancellationToken);
        // Never hold a SQLite write transaction across the network. Reject a page another lane
        // has already advanced, or whose credential was replaced while the request was running.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Entry(state).ReloadAsync(cancellationToken);
        await database.Entry(configuration).ReloadAsync(cancellationToken);
        if (state.Since != starting.Since || state.SinceId != starting.SinceId ||
            configuration.ActivePrdbCredential != credential)
            return new(SiteDirectoryRefreshVerdict.Pending, 0, "Another slice advanced the Site feed.");
        if (result.Status != SiteDirectoryFetchStatus.Fetched)
        {
            state.Phase = result.Status == SiteDirectoryFetchStatus.Rejected ? "Credential rejected" : "Waiting for prdb";
            state.NextAttemptAt = result.Status == SiteDirectoryFetchStatus.Rejected ? null : now + (result.RetryAfter ?? TimeSpan.FromMinutes(5));
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(result.Status == SiteDirectoryFetchStatus.Rejected
                ? SiteDirectoryRefreshVerdict.Refused : SiteDirectoryRefreshVerdict.Unreachable,
                0, result.Detail, result.RetryAfter);
        }
        var deleted = result.DeletedIds ?? [];
        foreach (var key in deleted.Concat(result.Sites.Select(site => site.Id)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var site = result.Sites.LastOrDefault(site => site.Id == key);
            var isDeleted = deleted.Contains(key);
            if (!state.BaselineComplete)
            {
                var staged = await database.SiteDirectoryStaging.AsTracking().SingleOrDefaultAsync(row => row.SiteKey == key, cancellationToken);
                if (staged is null) { staged = new SiteDirectoryStagingRow { SiteKey = key }; database.SiteDirectoryStaging.Add(staged); }
                staged.IsDeleted = isDeleted;
                staged.FactsJson = site is null ? null : JsonSerializer.Serialize(site, RetainedWorkFacts.Json);
            }
            else
            {
                var row = await database.SiteDirectoryEntries.AsTracking().SingleOrDefaultAsync(row => row.SiteKey == key, cancellationToken);
                if (row is null) { row = new SiteDirectoryEntryRow { SiteKey = key, Title = "" }; database.SiteDirectoryEntries.Add(row); }
                row.IsDeleted = isDeleted;
                if (site is not null) Apply(row, site);
            }
        }
        if (result.Cursor is { } cursor) { state.Since = cursor.Since; state.SinceId = cursor.SinceId; }
        state.LastAppliedAt = now;
        state.Phase = result.HasMore ? "Building or refreshing the Site Directory" : "Current";
        state.NextAttemptAt = result.RetryAfter is { } retry ? now + retry : null;
        await database.SaveChangesAsync(cancellationToken);
        if (!result.HasMore)
        {
            if (!state.BaselineComplete)
            {
                await database.SiteDirectoryEntries.ExecuteDeleteAsync(cancellationToken);
                // Activation copies the complete staged baseline in SQL rather than loading a
                // growing catalogue into application memory. Tombstones suppress claim fallback.
                await database.Database.ExecuteSqlRawAsync("""
                    INSERT INTO site_directory_entry
                    (SiteKey, Title, Url, PlatformId, PlatformKey, PlatformTitle, AccountHandle, LinksJson, IsDeleted)
                    SELECT SiteKey, coalesce(json_extract(FactsJson, '$.title'), ''),
                    json_extract(FactsJson, '$.url'), json_extract(FactsJson, '$.platformId'),
                    json_extract(FactsJson, '$.platformKey'), json_extract(FactsJson, '$.platformTitle'),
                    json_extract(FactsJson, '$.accountHandle'), json_extract(FactsJson, '$.links'), IsDeleted
                    FROM SiteDirectoryStaging
                    """, cancellationToken);
                await database.SiteDirectoryStaging.ExecuteDeleteAsync(cancellationToken);
            }
            state.BaselineComplete = true;
            state.CompletedAt = now; state.ServerTime = state.Since;
            configuration.SiteDirectoryFetchedAt = now;
        }
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var count = state.BaselineComplete
            ? await database.SiteDirectoryEntries.CountAsync(row => !row.IsDeleted, cancellationToken)
            : await database.SiteDirectoryStaging.CountAsync(row => !row.IsDeleted, cancellationToken);
        return new(result.HasMore ? SiteDirectoryRefreshVerdict.Pending : SiteDirectoryRefreshVerdict.Refreshed,
            count, result.HasMore ? $"Building or refreshing the Site Directory: {count} Sites retained; more pages remain." : null,
            result.RetryAfter);
    }

    private static void Apply(SiteDirectoryEntryRow row, RemoteSite site)
    {
        row.Title = site.Title; row.Url = site.Url; row.PlatformId = site.PlatformId;
        row.PlatformKey = site.PlatformKey; row.PlatformTitle = site.PlatformTitle; row.AccountHandle = site.AccountHandle;
        row.LinksJson = JsonSerializer.Serialize(site.Links, RetainedWorkFacts.Json);
    }

    /// <summary>
    /// The sites local Site Recognition may recognise: the retained directory together with every
    /// Site this installation has already established, so a site prdb identified on one Video File
    /// can be recognised on the next one even with no directory at all.
    /// </summary>
    public async Task<SiteVocabulary> ReadAsync(CancellationToken cancellationToken = default)
    {
        var directory = await database.SiteDirectoryEntries
            .AsNoTracking()
            .Where(entry => !entry.IsDeleted)
            .Select(entry => new SiteVocabularyEntry(entry.SiteKey, entry.Title, entry.Url,
                entry.PlatformId, entry.PlatformKey, entry.PlatformTitle, entry.AccountHandle))
            .ToListAsync(cancellationToken);
        var established = await database.IdentificationClaims
            .AsNoTracking()
            .Where(claim => claim.Dimension == IdentificationDimension.SiteRecognition &&
                            claim.Status == IdentificationClaimStatus.Current &&
                            !database.SiteDirectoryEntries.Any(entry => entry.SiteKey == claim.TargetKey && entry.IsDeleted))
            .GroupBy(claim => claim.TargetKey)
            .Select(group => new
            {
                Key = group.Key,
                Title = group.Max(claim => claim.TargetTitle)!,
                Url = group.Max(claim => claim.TargetUrl),
            })
            .ToListAsync(cancellationToken);
        var known = directory
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var retainedMetadata = await database.VideoMetadata.AsNoTracking()
            .Where(row => row.SiteFactsJson != null).Select(row => row.SiteFactsJson)
            .ToListAsync(cancellationToken);
        var retainedSites = retainedMetadata.Select(RetainedWorkFacts.Site).OfType<RemoteSite>()
            .DistinctBy(site => site.Id).ToDictionary(site => site.Id, StringComparer.OrdinalIgnoreCase);
        directory.AddRange(established
            .Where(entry => known.Add(entry.Key))
            .Select(entry => retainedSites.TryGetValue(entry.Key, out var site)
                ? new SiteVocabularyEntry(entry.Key, entry.Title, entry.Url,
                    site.PlatformId, site.PlatformKey, site.PlatformTitle, site.AccountHandle)
                : new SiteVocabularyEntry(entry.Key, entry.Title, entry.Url)));

        return SiteVocabulary.From(directory);
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
}
