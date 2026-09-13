import { useState } from 'react'
import { Link } from 'react-router'

import type { Account } from '../api/client'
import { firstError, PageHeading, RequestError, submitting, values } from '../ui'
import { usePlaylists } from './usePlaylists'

/// The Playlists an Account keeps, and the three things that can be done to one from here:
/// making one, renaming one, and deleting one. What is in a Playlist is the Playlist's own page,
/// because that page is the Library narrowed to it and has everything the Library has.
export function PlaylistsPage({ account }: { account: Account }) {
  const { playlists, create, rename, remove } = usePlaylists(account)
  const [renaming, setRenaming] = useState<string>()

  if (playlists.isPending) {
    return <p role="status">Opening your Playlists…</p>
  }

  if (playlists.isError) {
    return <RequestError error={playlists.error} />
  }

  const held = playlists.data.playlists

  return (
    <>
      <PageHeading eyebrow="Yours" title="Playlists">
        Sets of Videos you arrange yourself, in the order you put them in. Only you can see these.
      </PageHeading>

      <form
        className="playlist-new"
        onSubmit={submitting((form) => {
          const { name } = values<{ name: string | null }>(form, ['name'])
          if (!name?.trim()) return
          create.mutate(name.trim())
          form.reset()
        })}
      >
        <label className="field">
          <span>New Playlist</span>
          <input name="name" maxLength={120} placeholder="What is this one for?" />
        </label>
        <button className="primary-button" type="submit" disabled={create.isPending}>
          {create.isPending ? 'Working…' : 'Create'}
        </button>
      </form>

      {held.length === 0
        ? (
          <div className="empty-library">
            <strong>No Playlists yet</strong>
            <p>
              A Playlist is a set of Videos in an order you choose. Make one above, then add Videos
              to it from any Video’s own page.
            </p>
          </div>
          )
        : (
          <ul className="playlist-list">
            {held.map((playlist) => (
              <li key={playlist.id} className="playlist-row">
                {renaming === playlist.id
                  ? (
                    <form
                      className="playlist-rename"
                      onSubmit={submitting((form) => {
                        const { name } = values<{ name: string | null }>(form, ['name'])
                        if (name?.trim()) {
                          rename.mutate({ playlistId: playlist.id, name: name.trim() })
                        }
                        setRenaming(undefined)
                      })}
                    >
                      <label className="field">
                        <span className="visually-hidden">Rename {playlist.name}</span>
                        <input name="name" defaultValue={playlist.name} maxLength={120} autoFocus />
                      </label>
                      <button className="primary-button" type="submit">Save</button>
                      <button
                        className="quiet-button"
                        type="button"
                        onClick={() => setRenaming(undefined)}
                      >
                        Cancel
                      </button>
                    </form>
                    )
                  : (
                    <>
                      <Link className="playlist-name" to={`/playlists/${playlist.id}`}>
                        {playlist.name}
                      </Link>
                      <span className="muted">
                        {playlist.videoCount} {playlist.videoCount === 1 ? 'Video' : 'Videos'}
                      </span>
                      <button
                        className="quiet-button"
                        onClick={() => setRenaming(playlist.id)}
                      >
                        Rename<span className="visually-hidden"> {playlist.name}</span>
                      </button>
                      {/* Deleting a Playlist deletes the arrangement. The Videos, what was said
                          about them and every other list they are on are untouched, and the
                          confirmation says so rather than asking "are you sure?" about nothing in
                          particular. */}
                      <button
                        className="quiet-button danger"
                        onClick={() => {
                          const confirmed = window.confirm(
                            `Delete the Playlist “${playlist.name}”? The ${playlist.videoCount} ` +
                            'Videos in it stay in your library, along with everything else you ' +
                            'have said about them.',
                          )
                          if (confirmed) remove.mutate(playlist.id)
                        }}
                      >
                        Delete<span className="visually-hidden"> {playlist.name}</span>
                      </button>
                    </>
                    )}
              </li>
            ))}
          </ul>
          )}

      {(create.isError || rename.isError || remove.isError) && (
        <RequestError error={firstError(create.error, rename.error, remove.error)} />
      )}
    </>
  )
}
