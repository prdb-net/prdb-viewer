import { useMutation, useQueryClient } from '@tanstack/react-query'

import { api, type Account, type VideoSummary } from '../api/client'
import { queryKeys } from '../queryKeys'

export type PersonalActionKind = 'favourite' | 'watch-later' | 'rating' | 'dismiss'

export type PersonalAction = (
  kind: PersonalActionKind,
  video: VideoSummary,
  value?: boolean | number | null,
) => void

/// One Account's private organisation of a Video, from wherever it is offered.
///
/// Every screen that shows a Video offers the same four, so they are written once and invalidate
/// the same server state — the Library page, the addressed Video, and the personal shelves — from
/// one place, rather than each screen learning how to refresh the others.
export function usePersonalActions(account: Account) {
  const queryClient = useQueryClient()
  const mutation = useMutation({
    mutationFn: ({ kind, video, selected, rating }: {
      kind: PersonalActionKind
      video: VideoSummary
      selected?: boolean
      rating?: number | null
    }) => {
      if (kind === 'favourite') {
        return api.setFavourite(video.id, selected === true, account.csrfToken)
      }
      if (kind === 'watch-later') {
        return api.setWatchLater(video.id, selected === true, account.csrfToken)
      }
      if (kind === 'rating') {
        return api.setRating(video.id, rating ?? null, account.csrfToken)
      }
      return api.dismissContinueWatching(video.id, account.csrfToken)
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['videos'] })
      void queryClient.invalidateQueries({ queryKey: ['video'] })
      void queryClient.invalidateQueries({ queryKey: queryKeys.personalLibrary })
    },
  })

  const act: PersonalAction = (kind, video, value) => mutation.mutate({
    kind,
    video,
    selected: typeof value === 'boolean' ? value : undefined,
    rating: typeof value === 'number' || value === null ? value : undefined,
  })

  return { act, pending: mutation.isPending, failed: mutation.isError }
}
