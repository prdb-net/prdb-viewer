import { useState } from 'react'
import { useMutation, useMutationState, useQuery, useQueryClient } from '@tanstack/react-query'

import { api, type Account, type AccountSummary } from '../api/client'
import { accountStateLabel, exactTime, formatDay } from '../lib/format'
import { queryKeys } from '../queryKeys'
import { firstError, Notice, PageHeading, RequestError } from '../ui'

type AccountActionKind = 'approve' | 'disable' | 'reinstate' | 'recover'

type AccountActionVariables = { kind: AccountActionKind; target: string }

export function AccountsPage({ account }: { account: Account }) {
  const accounts = useQuery({ queryKey: queryKeys.accounts, queryFn: api.accounts })
  const queryClient = useQueryClient()
  const [issuedCode, setIssuedCode] = useState<string>()
  const action = useMutation({
    mutationKey: queryKeys.accountAction,
    mutationFn: ({ kind, target }: AccountActionVariables) => {
      if (kind === 'approve') return api.approve(target, account.csrfToken)
      if (kind === 'disable') return api.disable(target, account.csrfToken)
      if (kind === 'reinstate') return api.reinstate(target, account.csrfToken)
      return api.recoveryCode(target, account.csrfToken)
    },
    onSuccess: (result) => {
      if ('recoveryCode' in result && typeof result.recoveryCode === 'string') {
        setIssuedCode(result.recoveryCode)
      }
      void queryClient.invalidateQueries({ queryKey: queryKeys.accounts })
    },
  })

  /// Which Accounts are waiting on a decision of their own, so one row being busy does not read as
  /// the whole list being unavailable.
  const busy = useMutationState({
    filters: { mutationKey: queryKeys.accountAction, status: 'pending' },
    select: (entry) => (entry.state.variables as AccountActionVariables | undefined)?.target,
  })

  const waiting = accounts.data?.filter((candidate) => candidate.state === 'PendingApproval').length ?? 0
  const refused = accounts.data?.some((candidate) => candidate.state === 'Disabled') ?? false

  return (
    <>
      <PageHeading
        eyebrow="Administrator"
        title="Accounts"
        actions={accounts.isFetching ? <span className="muted">Refreshing…</span> : undefined}
      >
        {waiting > 0
          ? `${waiting} request${waiting === 1 ? '' : 's'} waiting for approval. Access begins only after one.`
          : 'Everyone who can reach this installation, and what they may do here.'}
      </PageHeading>

      {/* How anybody else comes to be in this list. An Administrator is normally the one who tells
          them, and until now the screen listing Accounts did not say. */}
      <p className="muted">
        Someone asks for access from the sign-in screen, under <strong>Request access</strong>. Their
        request waits here until it is approved; nothing about the library is visible before that.
      </p>

      <section className="panel">
        {waitingFirst(accounts.data).map((candidate) => (
          <AccountRow
            key={candidate.id}
            account={candidate}
            currentAccountId={account.id}
            pending={busy.includes(candidate.id)}
            act={(kind) => action.mutate({ kind, target: candidate.id })}
          />
        ))}
        {issuedCode && <Notice kind="success">One-time recovery code: <code>{issuedCode}</code></Notice>}
      </section>

      {refused && (
        <p className="muted">
          A disabled Account keeps everything it established — its viewing, its organisation and its
          identity — and can be reinstated. It signs in again when it is.
        </p>
      )}

      {action.data?.verdict === 'LastAdministrator' && (
        <Notice kind="error">
          This is the only approved Administrator. Approve another one before disabling this one, or
          the installation would have nobody who can administer it.
        </Notice>
      )}
      {action.data?.verdict === 'InvalidState' && (
        <Notice kind="error">
          That Account is no longer in the state this action applies to. The list has been refreshed.
        </Notice>
      )}

      {(accounts.isError || action.isError) && (
        <RequestError error={firstError(accounts.error, action.error)} />
      )}
    </>
  )
}

/// The Accounts in the order this screen is opened for: whoever is waiting on a decision first.
///
/// The heading says how many requests are waiting, and the request itself used to sit wherever the
/// list happened to put it — third, or past the fold on an installation with more than a handful
/// of Accounts. Everything else keeps the order the API sent, which is the one the list is read in
/// when nobody is waiting.
function waitingFirst(accounts: AccountSummary[] | undefined) {
  if (!accounts) return []

  return [
    ...accounts.filter((candidate) => candidate.state === 'PendingApproval'),
    ...accounts.filter((candidate) => candidate.state !== 'PendingApproval'),
  ]
}

function AccountRow({ account, currentAccountId, pending, act }: {
  account: AccountSummary
  currentAccountId: string
  pending: boolean
  act: (kind: AccountActionKind) => void
}) {
  const self = account.id === currentAccountId

  return (
    <article className="account-row">
      <div>
        <strong>{account.username}</strong>
        <small>{account.authority} · {accountStateLabel(account.state)}{self ? ' · you' : ''}</small>
        {/* Who is asking, and since when. The decision this screen exists for was offered against
            a username alone, while the API had already answered with both of these. */}
        <small>
          <time dateTime={account.registeredAt} title={exactTime(account.registeredAt)}>
            {account.state === 'PendingApproval' ? 'Asked' : 'Registered'} {formatDay(account.registeredAt)}
          </time>
          {account.email ? ` · ${account.email}` : ''}
        </small>
      </div>
      <div className="row-actions">
        {account.state === 'PendingApproval' && (
          <button onClick={() => act('approve')} disabled={pending}>Approve</button>
        )}
        {account.state === 'Approved' && (
          <button className="quiet-button" onClick={() => act('recover')} disabled={pending}>
            Recovery code
          </button>
        )}
        {/* Disabling was a one-way door: approval needs a waiting request, and a disabled Account
            has none, so nothing could return it. */}
        {account.state === 'Disabled' && (
          <button onClick={() => act('reinstate')} disabled={pending}>Reinstate</button>
        )}
        {account.state !== 'Disabled' && !self && (
          <button className="danger-button" onClick={() => act('disable')} disabled={pending}>
            Disable
          </button>
        )}
      </div>
    </article>
  )
}
