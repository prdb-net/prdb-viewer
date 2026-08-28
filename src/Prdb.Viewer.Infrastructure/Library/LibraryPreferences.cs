using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

public sealed record LibraryPreferencesSummary(bool IncludesNotReadyForDirectPlay);

/// <summary>
/// The Account's own discovery preferences. They widen what ordinary results contain and never
/// change what a Video is, so nothing here is Shared Library Knowledge.
/// </summary>
public sealed class LibraryPreferences(ViewerDbContext database)
{
    public async Task<LibraryPreferencesSummary> GetAsync(
        Guid accountId,
        CancellationToken cancellationToken = default) =>
        new(await database.Accounts
            .Where(account => account.Id == accountId)
            .Select(account => account.IncludesNotReadyForDirectPlay)
            .SingleOrDefaultAsync(cancellationToken));

    public async Task<LibraryPreferencesSummary> SetIncludesNotReadyForDirectPlayAsync(
        Guid accountId,
        bool included,
        CancellationToken cancellationToken = default)
    {
        await database.Accounts
            .Where(account => account.Id == accountId)
            .ExecuteUpdateAsync(
                update => update.SetProperty(
                    account => account.IncludesNotReadyForDirectPlay,
                    included),
                cancellationToken);

        return new LibraryPreferencesSummary(included);
    }
}
