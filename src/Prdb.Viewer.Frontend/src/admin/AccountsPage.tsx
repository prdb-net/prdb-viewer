import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { api, type Account, type AccountSummary } from '../api/client'
import { queryKeys } from '../queryKeys'
import { firstError, Notice, PageHeading, RequestError } from '../ui'

export function AccountsPage({ account }: { account: Account }) {
  const accounts = useQuery({ queryKey: queryKeys.accounts, queryFn: api.accounts })
  const queryClient = useQueryClient()
  const [issuedCode, setIssuedCode] = useState<string>()
  const action = useMutation({
    mutationFn: ({ kind, target }: { kind: 'approve' | 'disable' | 'recover'; target: string }) => {
      if (kind === 'approve') return api.approve(target, account.csrfToken)
      if (kind === 'disable') return api.disable(target, account.csrfToken)
      return api.recoveryCode(target, account.csrfToken)
    },
    onSuccess: (result) => {
      if ('recoveryCode' in result && typeof result.recoveryCode === 'string') {
        setIssuedCode(result.recoveryCode)
      }
      void queryClient.invalidateQueries({ queryKey: queryKeys.accounts })
    },
  })
  const waiting = accounts.data?.filter((candidate) => candidate.state === 'PendingApproval').length ?? 0

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

      <section className="panel">
        {accounts.data?.map((candidate) => (
          <AccountRow
            key={candidate.id}
            account={candidate}
            currentAccountId={account.id}
            pending={action.isPending}
            act={(kind) => action.mutate({ kind, target: candidate.id })}
          />
        ))}
        {issuedCode && <Notice kind="success">One-time recovery code: <code>{issuedCode}</code></Notice>}
      </section>

      {(accounts.isError || action.isError) && (
        <RequestError error={firstError(accounts.error, action.error)} />
      )}
    </>
  )
}

function AccountRow({ account, currentAccountId, pending, act }: {
  account: AccountSummary
  currentAccountId: string
  pending: boolean
  act: (kind: 'approve' | 'disable' | 'recover') => void
}) {
  return (
    <article className="account-row">
      <div><strong>{account.username}</strong><small>{account.authority} · {account.state}</small></div>
      <div className="row-actions">
        {account.state === 'PendingApproval' && <button onClick={() => act('approve')} disabled={pending}>Approve</button>}
        {account.state === 'Approved' && <button onClick={() => act('recover')} disabled={pending}>Recovery code</button>}
        {account.state !== 'Disabled' && account.id !== currentAccountId && <button className="danger-button" onClick={() => act('disable')} disabled={pending}>Disable</button>}
      </div>
    </article>
  )
}
