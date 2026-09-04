import { useMutation, useQueryClient } from '@tanstack/react-query'

import { api, type Account } from '../api/client'

/// Making an Actor a Favourite, from wherever they are shown.
///
/// It is its own hook rather than part of `usePersonalActions`, which is about a Video and answers
/// with that Video's Personal State. An Actor's answer is whether this installation knows them at
/// all, so the screens read the change back from the queries it invalidates.
export function useFavouriteActor(account: Account) {
  const queryClient = useQueryClient()
  const favourite = useMutation({
    mutationKey: ['favourite-actor'],
    mutationFn: ({ actorId, selected }: { actorId: string; selected: boolean }) =>
      api.setFavouriteActor(actorId, selected, account.csrfToken),
    onSuccess: (_result, { actorId }) => {
      void queryClient.invalidateQueries({ queryKey: ['actors'] })
      void queryClient.invalidateQueries({ queryKey: ['actor', actorId] })
    },
  })

  return {
    act: (actorId: string, selected: boolean) => favourite.mutate({ actorId, selected }),
    /// Only the Actor with something in flight is busy, so one card can be saving without the
    /// screen being.
    pending: (actorId: string) =>
      favourite.isPending && favourite.variables?.actorId === actorId,
    failed: favourite.isError,
    error: favourite.error,
  }
}
