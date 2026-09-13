import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { api, type Account } from '../api/client'
import { queryKeys } from '../queryKeys'

/// One Account's Playlists, and everything that changes one.
///
/// The mutations are written once and invalidate the same server state from one place — the list
/// itself, and the Library, because narrowing to a Playlist is a Library question whose answer a
/// membership change makes stale.
export function usePlaylists(account: Account, videoId?: string) {
  const queryClient = useQueryClient()
  const playlists = useQuery({
    queryKey: queryKeys.playlists(videoId ?? ''),
    queryFn: () => api.playlists(videoId),
  })

  const settled = () => {
    void queryClient.invalidateQueries({ queryKey: ['playlists'] })
    void queryClient.invalidateQueries({ queryKey: ['videos'] })
    void queryClient.invalidateQueries({ queryKey: ['library-facets'] })
    // Playlist membership is a positive input to the ranking, so a page of recommendations no
    // longer says what the evidence says once it changes.
    void queryClient.invalidateQueries({ queryKey: ['recommendations'] })
  }

  const create = useMutation({
    mutationFn: (name: string) => api.createPlaylist(name, account.csrfToken),
    onSuccess: settled,
  })
  const rename = useMutation({
    mutationFn: ({ playlistId, name }: { playlistId: string; name: string }) =>
      api.renamePlaylist(playlistId, name, account.csrfToken),
    onSuccess: settled,
  })
  const remove = useMutation({
    mutationFn: (playlistId: string) => api.deletePlaylist(playlistId, account.csrfToken),
    onSuccess: settled,
  })
  /// The Video is named per call rather than taken from the hook: a Video's own page asks about
  /// one Video, and a Playlist's page asks about whichever card was pressed.
  const setMembership = useMutation({
    mutationFn: ({ playlistId, video, member }: {
      playlistId: string
      video: string
      member: boolean
    }) => api.setPlaylistMembership(playlistId, video, member, account.csrfToken),
    onSuccess: settled,
  })
  const move = useMutation({
    mutationFn: ({ playlistId, video, position }: {
      playlistId: string
      video: string
      position: number
    }) => api.movePlaylistEntry(playlistId, video, position, account.csrfToken),
    onSuccess: settled,
  })

  return { playlists, create, rename, remove, setMembership, move }
}
