import { useState } from 'react'
import { Link } from 'react-router'

import type { Account, VideoSummary } from '../api/client'
import { RequestError, submitting, values } from '../ui'
import { usePlaylists } from './usePlaylists'

/// Which of this Account's Playlists hold this Video, and the control that changes that.
///
/// Every Playlist is listed with a checkbox rather than hidden behind a picker, because the
/// question a reader has here is "where is this filed?" at least as often as "file this
/// somewhere". A new Playlist can be made from here too: needing to leave the Video, make the
/// list, and come back is exactly the kind of errand that stops anybody from filing anything.
export function PlaylistMembership({ account, video }: { account: Account; video: VideoSummary }) {
  const { playlists, create, setMembership } = usePlaylists(account, video.id)
  const [making, setMaking] = useState(false)

  if (playlists.isError) {
    return <RequestError error={playlists.error} />
  }

  const held = playlists.data?.playlists ?? []
  const busy = setMembership.isPending || create.isPending

  return (
    <div className="playlist-membership">
      <span className="reaction-caption" aria-hidden="true">Playlists</span>
      {playlists.isPending
        ? <p role="status">Reading your Playlists…</p>
        : held.length === 0 && !making
          ? <p className="muted">You have no Playlists yet.</p>
          : (
            <ul className="playlist-choices">
              {held.map((playlist) => (
                <li key={playlist.id}>
                  <label>
                    <input
                      type="checkbox"
                      checked={playlist.contains}
                      disabled={busy}
                      onChange={() => setMembership.mutate({
                        playlistId: playlist.id,
                        video: video.id,
                        member: !playlist.contains,
                      })}
                    />
                    <span>{playlist.name}</span>
                  </label>
                </li>
              ))}
            </ul>
            )}

      {making
        ? (
          <form
            className="playlist-rename"
            onSubmit={submitting((form) => {
              const { name } = values<{ name: string | null }>(form, ['name'])
              if (name?.trim()) {
                // The Video goes into the Playlist that was just made for it. Making an empty one
                // from a Video's page would be answering a question nobody asked here.
                void create
                  .mutateAsync(name.trim())
                  .then((result) => result.playlist && setMembership.mutate({
                    playlistId: result.playlist.id,
                    video: video.id,
                    member: true,
                  }))
              }
              setMaking(false)
            })}
          >
            <label className="field">
              <span className="visually-hidden">Name the new Playlist</span>
              <input name="name" maxLength={120} placeholder="New Playlist" autoFocus />
            </label>
            <button className="primary-button" type="submit">Add</button>
            <button className="quiet-button" type="button" onClick={() => setMaking(false)}>
              Cancel
            </button>
          </form>
          )
        : (
          <div className="playlist-membership-actions">
            <button className="quiet-button" onClick={() => setMaking(true)} disabled={busy}>
              New Playlist
            </button>
            {held.length > 0 && <Link className="quiet-button" to="/playlists">Manage</Link>}
          </div>
          )}

      {(create.isError || setMembership.isError) && (
        <RequestError error={create.error ?? setMembership.error} />
      )}
    </div>
  )
}
