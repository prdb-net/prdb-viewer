import { useMutation, useMutationState, useQueryClient } from '@tanstack/react-query'

import { api, type Account, type VideoSummary } from '../api/client'
import { queryKeys } from '../queryKeys'

export type PersonalActionKind = 'favourite' | 'watch-later' | 'rating' | 'dismiss'

export type PersonalAction = (
  kind: PersonalActionKind,
  video: VideoSummary,
  value?: boolean | number | null,
) => void

/// Whether this Video in particular is waiting on an action of its own.
export type PersonalPending = (videoId: string) => boolean

type PersonalActionVariables = {
  kind: PersonalActionKind
  video: VideoSummary
  selected?: boolean
  rating?: number | null
}

/// One Account's private organisation of a Video, from wherever it is offered.
///
/// Every screen that shows a Video offers the same four, so they are written once and invalidate
/// the same server state — the Library page, the addressed Video, and the personal shelves — from
/// one place, rather than each screen learning how to refresh the others.
///
/// What is in flight is tracked per Video rather than per screen. A grid of sixty cards used to
/// disable all of them while one card's Favourite was saving, which reads as an application that
/// has stopped rather than as one card that is busy.
export function usePersonalActions(account: Account) {
  const queryClient = useQueryClient()
  const mutation = useMutation({
    mutationKey: queryKeys.personalAction,
    mutationFn: ({ kind, video, selected, rating }: PersonalActionVariables) => {
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

  /// Which Videos are saving, asked of the mutation cache rather than tracked beside it. Several of
  /// these can be in flight at once — two cards, or two actions on one card — and only the cache
  /// knows about all of them; a hook's own `isPending` describes whichever went last.
  const saving = useMutationState({
    filters: { mutationKey: queryKeys.personalAction, status: 'pending' },
    select: (entry) => (entry.state.variables as PersonalActionVariables | undefined)?.video.id,
  })

  const act: PersonalAction = (kind, video, value) => mutation.mutate({
    kind,
    video,
    selected: typeof value === 'boolean' ? value : undefined,
    rating: typeof value === 'number' || value === null ? value : undefined,
  })

  const pending: PersonalPending = (videoId) => saving.includes(videoId)

  return { act, pending, failed: mutation.isError, error: mutation.error }
}
