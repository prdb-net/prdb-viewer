import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'

import { api, type Account } from '../api/client'
import { LibraryPage } from '../library/LibraryPage'
import { queryKeys } from '../queryKeys'
import { RequestError } from '../ui'

/// One Playlist, which is the Library narrowed to it.
///
/// The screen exists to answer one thing the Library cannot: what this Playlist is called. A
/// Playlist somebody else owns, or one that has been deleted, is not found here rather than
/// showing as an empty Library — an empty answer would be a different, wrong statement.
export function PlaylistPage({ account }: { account: Account }) {
  const { playlistId } = useParams()
  const playlists = useQuery({
    queryKey: queryKeys.playlists(),
    queryFn: () => api.playlists(),
  })

  if (playlists.isPending) {
    return <p role="status">Opening your library…</p>
  }

  if (playlists.isError) {
    return <RequestError error={playlists.error} />
  }

  const playlist = playlists.data.playlists.find((candidate) => candidate.id === playlistId)

  if (!playlist) {
    return (
      <div className="empty-library">
        <strong>No such Playlist</strong>
        <p>
          It may have been deleted. <Link to="/playlists">Your Playlists</Link> are still here.
        </p>
      </div>
    )
  }

  return <LibraryPage account={account} playlist={{ id: playlist.id, name: playlist.name }} />
}
