import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { api, emptyFilters, type Account } from '../api/client'
import { friendlyState } from '../lib/format'
import { PageHeading, RequestError } from '../ui'

/// Who you are here, and the standing choices that follow you rather than one view.
///
/// The preference below is Personal State, not a filter: it changes what Ordinary Discovery admits
/// for this Account everywhere, which is why it lives with the Account rather than beside the
/// facets that narrow a single page.
export function AccountPage({ account }: { account: Account }) {
  const queryClient = useQueryClient()
  // The Library reports the preference along with its page rather than answering for it
  // separately, so the smallest page there is is what asks for the current value.
  const preference = useQuery({
    queryKey: ['videos', 'preference'] as const,
    queryFn: () => api.videos(emptyFilters, 0, 1),
  })
  const includeNotReady = useMutation({
    mutationFn: (included: boolean) => api.setIncludeNotReady(included, account.csrfToken),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['videos'] })
      void queryClient.invalidateQueries({ queryKey: ['video'] })
    },
  })

  return (
    <>
      <PageHeading eyebrow="Account" title={account.username}>
        Your viewing, your organisation and the evidence your browser produced are private to this
        Account. An Administrator never sees them as activity.
      </PageHeading>

      <section className="panel">
        <div className="section-heading"><h3>Identity</h3></div>
        <dl className="fact-list">
          <div><dt>Username</dt><dd>{account.username}</dd></div>
          <div><dt>Authority</dt><dd>{friendlyState(account.authority)}</dd></div>
          {account.email && <div><dt>Email</dt><dd>{account.email}</dd></div>}
        </dl>
      </section>

      <section className="panel">
        <div className="section-heading"><h3>Library preferences</h3></div>
        <p>
          Ordinary results normally hold only what this browser is ready to play directly. Widening
          them shows everything it has not ruled out, and everything it cannot play at all.
        </p>
        <label className="preference">
          <input
            type="checkbox"
            checked={preference.data?.includesNotReadyForDirectPlay === true}
            disabled={includeNotReady.isPending || preference.isPending}
            onChange={(event) => includeNotReady.mutate(event.target.checked)}
          />
          <span>Show unsupported Videos in ordinary results</span>
        </label>
      </section>

      {(preference.isError || includeNotReady.isError) && <RequestError />}
    </>
  )
}
