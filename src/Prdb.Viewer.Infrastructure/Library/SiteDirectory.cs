using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

public enum SiteDirectoryRefreshVerdict
{
    Refreshed,
    NoCredential,
    Refused,
    Unreachable,
}

public sealed record SiteDirectoryRefreshResult(
    SiteDirectoryRefreshVerdict Verdict,
    int SiteCount,
    string? Detail = null);

/// <summary>
/// The retained Site Directory: the vocabulary local Site Recognition reads Video File paths
/// against. It is a regenerable copy of what prdb publishes, refreshed at most once a day, joined
/// with every Site this installation has already established for itself so that recognition keeps
/// working while prdb is unreachable or its credential is gone.
/// </summary>
public sealed class SiteDirectory(
    ViewerDbContext database,
    IPrdbSiteDirectoryClient client,
    TimeProvider timeProvider)
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(24);

    /// <summary>
    /// Whether the retained copy is old enough to be worth one request. A directory that has never
    /// been fetched is always stale, which is what makes the first run fetch it.
    /// </summary>
    public async Task<bool> IsStaleAsync(CancellationToken cancellationToken = default)
    {
        var fetchedAt = await database.InstallationConfigurations
            .AsNoTracking()
            .Select(configuration => configuration.SiteDirectoryFetchedAt)
            .SingleOrDefaultAsync(cancellationToken);

        return fetchedAt is null || Now() - fetchedAt.Value >= RefreshInterval;
    }

    public async Task<bool> IsEmptyAsync(CancellationToken cancellationToken = default) =>
        !await database.SiteDirectoryEntries.AsNoTracking().AnyAsync(cancellationToken);

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

        var result = await client.FetchAsync(credential, cancellationToken);

        if (result.Status != SiteDirectoryFetchStatus.Fetched)
        {
            return new SiteDirectoryRefreshResult(
                result.Status == SiteDirectoryFetchStatus.Rejected
                    ? SiteDirectoryRefreshVerdict.Refused
                    : SiteDirectoryRefreshVerdict.Unreachable,
                0,
                result.Detail);
        }

        var now = Now();
        var entries = result.Sites
            .Where(site => !string.IsNullOrWhiteSpace(site.Title))
            .DistinctBy(site => site.Id, StringComparer.OrdinalIgnoreCase)
            .Select(site => new SiteDirectoryEntryRow
            {
                SiteKey = site.Id,
                Title = site.Title,
                Url = site.Url,
            })
            .ToArray();

        await using var transaction = await database.Database
            .BeginTransactionAsync(cancellationToken);
        await database.SiteDirectoryEntries.ExecuteDeleteAsync(cancellationToken);
        database.SiteDirectoryEntries.AddRange(entries);
        configuration.SiteDirectoryFetchedAt = now;
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new SiteDirectoryRefreshResult(
            SiteDirectoryRefreshVerdict.Refreshed,
            entries.Length);
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
            .Select(entry => new SiteVocabularyEntry(entry.SiteKey, entry.Title, entry.Url))
            .ToListAsync(cancellationToken);
        var established = await database.IdentificationClaims
            .AsNoTracking()
            .Where(claim => claim.Dimension == IdentificationDimension.SiteRecognition &&
                            claim.Status == IdentificationClaimStatus.Current)
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

        directory.AddRange(established
            .Where(entry => known.Add(entry.Key))
            .Select(entry => new SiteVocabularyEntry(entry.Key, entry.Title, entry.Url)));

        return SiteVocabulary.From(directory);
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
}
