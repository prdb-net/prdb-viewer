using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Personal;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Personal;

/// <summary>
/// One Account's Playlists and what is in them.
///
/// Every method takes the Account and narrows by it before anything else, so a Playlist that
/// belongs to somebody else answers the way one that does not exist answers. There is no
/// Administrator path in here at all: an Administrator has authority over the installation, not
/// over how a User files their own Videos.
/// </summary>
public sealed class PlaylistService(ViewerDbContext database, TimeProvider timeProvider)
{
    /// <summary>
    /// This Account's Playlists, alphabetically, with how many Videos each holds. Where a Video is
    /// named, each one also says whether it is in there, which is what a screen offering "add to
    /// a Playlist" reads.
    /// </summary>
    public async Task<IReadOnlyList<PlaylistSummary>> ListAsync(
        Guid accountId,
        Guid? videoId = null,
        CancellationToken cancellationToken = default) =>
        await database.Playlists
            .AsNoTracking()
            .Where(playlist => playlist.AccountId == accountId)
            .OrderBy(playlist => playlist.Name)
            .ThenBy(playlist => playlist.Id)
            .Select(playlist => new PlaylistSummary(
                playlist.Id,
                playlist.Name,
                playlist.Entries.Count,
                videoId != null &&
                    playlist.Entries.Any(entry => entry.VideoId == videoId),
                new DateTimeOffset(DateTime.SpecifyKind(playlist.CreatedAt, DateTimeKind.Utc)),
                new DateTimeOffset(DateTime.SpecifyKind(playlist.UpdatedAt, DateTimeKind.Utc))))
            .ToListAsync(cancellationToken);

    public async Task<PlaylistResult> CreateAsync(
        Guid accountId,
        string? name,
        CancellationToken cancellationToken = default)
    {
        if (PlaylistRule.Normalize(name) is not { } normalized)
        {
            return new PlaylistResult(PlaylistVerdict.InvalidName, null);
        }

        var now = UtcNow();
        var playlist = new PlaylistRow
        {
            Id = Guid.CreateVersion7(),
            AccountId = accountId,
            Name = normalized,
            CreatedAt = now,
            UpdatedAt = now,
        };
        database.Playlists.Add(playlist);
        await database.SaveChangesAsync(cancellationToken);

        return new PlaylistResult(
            PlaylistVerdict.Updated,
            new PlaylistSummary(
                playlist.Id,
                playlist.Name,
                0,
                false,
                AsOffset(now),
                AsOffset(now)));
    }

    public async Task<PlaylistResult> RenameAsync(
        Guid accountId,
        Guid playlistId,
        string? name,
        CancellationToken cancellationToken = default)
    {
        if (PlaylistRule.Normalize(name) is not { } normalized)
        {
            return new PlaylistResult(PlaylistVerdict.InvalidName, null);
        }

        var playlist = await OwnedAsync(accountId, playlistId, cancellationToken);

        if (playlist is null)
        {
            return new PlaylistResult(PlaylistVerdict.NotFound, null);
        }

        playlist.Name = normalized;
        playlist.UpdatedAt = UtcNow();
        await database.SaveChangesAsync(cancellationToken);

        return await SummarizeAsync(accountId, playlistId, cancellationToken);
    }

    /// <summary>
    /// Deletes the organisation and nothing else. The Videos, their Personal Reactions, their
    /// viewing history and every other list they are on are untouched.
    /// </summary>
    public async Task<PlaylistVerdict> DeleteAsync(
        Guid accountId,
        Guid playlistId,
        CancellationToken cancellationToken = default)
    {
        var playlist = await OwnedAsync(accountId, playlistId, cancellationToken);

        if (playlist is null)
        {
            return PlaylistVerdict.NotFound;
        }

        database.Playlists.Remove(playlist);
        await database.SaveChangesAsync(cancellationToken);

        return PlaylistVerdict.Updated;
    }

    /// <summary>
    /// Puts a Video at the end of a Playlist. Adding one that is already there changes nothing,
    /// including its place: an accidental second add must not move somebody's arrangement.
    /// </summary>
    public async Task<PlaylistResult> AddAsync(
        Guid accountId,
        Guid playlistId,
        Guid videoId,
        CancellationToken cancellationToken = default)
    {
        var playlist = await OwnedAsync(accountId, playlistId, cancellationToken);

        if (playlist is null)
        {
            return new PlaylistResult(PlaylistVerdict.NotFound, null);
        }

        if (!await database.Videos.AnyAsync(video => video.Id == videoId, cancellationToken))
        {
            return new PlaylistResult(PlaylistVerdict.VideoNotFound, null);
        }

        var held = await database.PlaylistEntries.AnyAsync(
            entry => entry.PlaylistId == playlistId && entry.VideoId == videoId,
            cancellationToken);

        if (!held)
        {
            var last = await database.PlaylistEntries
                .Where(entry => entry.PlaylistId == playlistId)
                .MaxAsync(entry => (int?)entry.Position, cancellationToken);
            var now = UtcNow();
            database.PlaylistEntries.Add(new PlaylistEntryRow
            {
                PlaylistId = playlistId,
                VideoId = videoId,
                Position = (last ?? -1) + 1,
                AddedAt = now,
            });
            playlist.UpdatedAt = now;
            await database.SaveChangesAsync(cancellationToken);
        }

        return await SummarizeAsync(accountId, playlistId, cancellationToken, videoId);
    }

    public async Task<PlaylistResult> RemoveAsync(
        Guid accountId,
        Guid playlistId,
        Guid videoId,
        CancellationToken cancellationToken = default)
    {
        var playlist = await OwnedAsync(accountId, playlistId, cancellationToken);

        if (playlist is null)
        {
            return new PlaylistResult(PlaylistVerdict.NotFound, null);
        }

        var entry = await database.PlaylistEntries
            .AsTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.PlaylistId == playlistId && candidate.VideoId == videoId,
                cancellationToken);

        if (entry is not null)
        {
            database.PlaylistEntries.Remove(entry);
            playlist.UpdatedAt = UtcNow();
            await database.SaveChangesAsync(cancellationToken);
            await RenumberAsync(playlistId, cancellationToken);
        }

        return await SummarizeAsync(accountId, playlistId, cancellationToken, videoId);
    }

    /// <summary>
    /// Moves one Video to a place in the Playlist's own order, counted from zero over the whole
    /// Playlist rather than over whatever a screen happens to be showing.
    /// </summary>
    /// <remarks>
    /// The destination is an absolute position for a reason. A move expressed as "before that
    /// other Video" is the same request when the caller is looking at a filtered list, and it
    /// silently rearranges the entries between the two that the caller could not see. A position
    /// over the whole Playlist cannot mean that, and a screen that is narrowed does not offer the
    /// control at all.
    /// </remarks>
    public async Task<PlaylistResult> MoveAsync(
        Guid accountId,
        Guid playlistId,
        Guid videoId,
        int toPosition,
        CancellationToken cancellationToken = default)
    {
        var playlist = await OwnedAsync(accountId, playlistId, cancellationToken);

        if (playlist is null)
        {
            return new PlaylistResult(PlaylistVerdict.NotFound, null);
        }

        var entries = await database.PlaylistEntries
            .AsTracking()
            .Where(entry => entry.PlaylistId == playlistId)
            .OrderBy(entry => entry.Position)
            .ThenBy(entry => entry.AddedAt)
            .ToListAsync(cancellationToken);
        var moving = entries.SingleOrDefault(entry => entry.VideoId == videoId);

        if (moving is null)
        {
            return new PlaylistResult(PlaylistVerdict.VideoNotFound, null);
        }

        entries.Remove(moving);
        entries.Insert(Math.Clamp(toPosition, 0, entries.Count), moving);

        for (var position = 0; position < entries.Count; position++)
        {
            entries[position].Position = position;
        }

        playlist.UpdatedAt = UtcNow();
        await database.SaveChangesAsync(cancellationToken);

        return await SummarizeAsync(accountId, playlistId, cancellationToken, videoId);
    }

    /// <summary>
    /// How many of this Account's Playlists hold each of the named Videos. It is the shape the
    /// recommender asks in — one question for a page of candidates rather than one per Video — and
    /// what it does with the number is <see cref="PlaylistRule"/>'s business rather than this
    /// service's.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, int>> MembershipsAsync(
        Guid accountId,
        IReadOnlyCollection<Guid> videoIds,
        CancellationToken cancellationToken = default)
    {
        if (videoIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        return await database.PlaylistEntries
            .AsNoTracking()
            .Where(entry =>
                entry.Playlist.AccountId == accountId && videoIds.Contains(entry.VideoId))
            .GroupBy(entry => entry.VideoId)
            .ToDictionaryAsync(group => group.Key, group => group.Count(), cancellationToken);
    }

    /// <summary>
    /// Keeps the positions contiguous from zero after a removal, so that a later move counts in
    /// the same units the reader does.
    /// </summary>
    private async Task RenumberAsync(Guid playlistId, CancellationToken cancellationToken)
    {
        var entries = await database.PlaylistEntries
            .AsTracking()
            .Where(entry => entry.PlaylistId == playlistId)
            .OrderBy(entry => entry.Position)
            .ThenBy(entry => entry.AddedAt)
            .ToListAsync(cancellationToken);

        for (var position = 0; position < entries.Count; position++)
        {
            entries[position].Position = position;
        }

        await database.SaveChangesAsync(cancellationToken);
    }

    private Task<PlaylistRow?> OwnedAsync(
        Guid accountId,
        Guid playlistId,
        CancellationToken cancellationToken) =>
        database.Playlists
            .AsTracking()
            .SingleOrDefaultAsync(
                playlist => playlist.Id == playlistId && playlist.AccountId == accountId,
                cancellationToken);

    private async Task<PlaylistResult> SummarizeAsync(
        Guid accountId,
        Guid playlistId,
        CancellationToken cancellationToken,
        Guid? videoId = null)
    {
        var summaries = await ListAsync(accountId, videoId, cancellationToken);
        var summary = summaries.SingleOrDefault(playlist => playlist.Id == playlistId);

        return summary is null
            ? new PlaylistResult(PlaylistVerdict.NotFound, null)
            : new PlaylistResult(PlaylistVerdict.Updated, summary);
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private static DateTimeOffset AsOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
