import { useState, type FormEvent, type ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import {
  api,
  type Account,
  type AccountSummary,
  type BootstrapRequest,
  type LibraryDirectoryStage,
  type RecoverRequest,
  type RegistrationRequest,
  type SignInRequest,
} from './api/client'

const queryKeys = {
  state: ['access-state'] as const,
  account: ['account'] as const,
  accounts: ['accounts'] as const,
  configuration: ['configuration'] as const,
  libraryDirectoryCandidates: ['library-directory-candidates'] as const,
  backgroundWork: ['background-work'] as const,
}

export function App() {
  const state = useQuery({ queryKey: queryKeys.state, queryFn: api.state, retry: false })
  const account = useQuery({
    queryKey: queryKeys.account,
    queryFn: api.me,
    enabled: state.data?.signedIn === true,
    staleTime: Number.POSITIVE_INFINITY,
    retry: false,
  })

  if (state.isPending || (state.data?.signedIn && account.isPending)) {
    return <CenteredCard><p role="status">Opening your library…</p></CenteredCard>
  }

  if (state.isError || account.isError) {
    return <CenteredCard><Notice kind="error">The viewer could not reach its API. Try again shortly.</Notice></CenteredCard>
  }

  if (!state.data.claimed) {
    return <BootstrapPanel />
  }

  if (!state.data.signedIn || !account.data) {
    return <AccessPanel />
  }

  return <Library account={account.data} />
}

function BootstrapPanel() {
  const queryClient = useQueryClient()
  const mutation = useMutation({
    mutationFn: api.bootstrap,
    onSuccess: (result) => {
      if (result.account) {
        queryClient.setQueryData(queryKeys.account, result.account)
        queryClient.setQueryData(queryKeys.state, { claimed: true, signedIn: true })
      }
    },
  })

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    mutation.mutate(values<BootstrapRequest>(event.currentTarget, ['authorization', 'username', 'password', 'email']))
  }

  return (
    <CenteredCard>
      <Brand />
      <h2>Claim this installation</h2>
      <p>Use the one-time authorization written by the operator command, then create the first Administrator.</p>
      <form onSubmit={submit}>
        <Field name="authorization" label="One-time authorization" autoComplete="off" required />
        <Field name="username" label="Administrator username" autoComplete="username" required />
        <Field name="email" label="Email (optional)" type="email" autoComplete="email" />
        <Field name="password" label="Password" type="password" autoComplete="new-password" minLength={12} required />
        <SubmitButton pending={mutation.isPending}>Create Administrator</SubmitButton>
      </form>
      {mutation.data && !mutation.data.account && (
        <Notice kind="error">{bootstrapMessage(mutation.data.verdict)}</Notice>
      )}
      {mutation.isError && <RequestError />}
    </CenteredCard>
  )
}

type AccessMode = 'sign-in' | 'register' | 'recover'

function AccessPanel() {
  const [mode, setMode] = useState<AccessMode>('sign-in')
  const queryClient = useQueryClient()
  const signIn = useMutation({
    mutationFn: api.signIn,
    onSuccess: (result) => {
      if (result.account) {
        queryClient.setQueryData(queryKeys.account, result.account)
        queryClient.setQueryData(queryKeys.state, { claimed: true, signedIn: true })
      }
    },
  })
  const register = useMutation({ mutationFn: api.register })
  const recover = useMutation({ mutationFn: api.recover })

  function submitSignIn(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    signIn.mutate(values<SignInRequest>(event.currentTarget, ['username', 'password']))
  }

  function submitRegistration(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    register.mutate(values<RegistrationRequest>(event.currentTarget, ['username', 'password', 'email']))
  }

  function submitRecovery(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    recover.mutate(values<RecoverRequest>(event.currentTarget, ['username', 'recoveryCode', 'newPassword']))
  }

  return (
    <CenteredCard>
      <Brand />
      <div className="tabs" aria-label="Account access">
        <Tab active={mode === 'sign-in'} onClick={() => setMode('sign-in')}>Sign in</Tab>
        <Tab active={mode === 'register'} onClick={() => setMode('register')}>Request access</Tab>
        <Tab active={mode === 'recover'} onClick={() => setMode('recover')}>Recover</Tab>
      </div>

      {mode === 'sign-in' && (
        <form onSubmit={submitSignIn}>
          <Field name="username" label="Username" autoComplete="username" required />
          <Field name="password" label="Password" type="password" autoComplete="current-password" required />
          <SubmitButton pending={signIn.isPending}>Sign in</SubmitButton>
          {signIn.data && !signIn.data.account && <Notice kind="error">{signInMessage(signIn.data.verdict)}</Notice>}
          {signIn.isError && <RequestError />}
        </form>
      )}

      {mode === 'register' && (
        <form onSubmit={submitRegistration}>
          <p>Ask an Administrator to approve your request after submitting it.</p>
          <Field name="username" label="Username" autoComplete="username" required />
          <Field name="email" label="Email (optional)" type="email" autoComplete="email" />
          <Field name="password" label="Password" type="password" autoComplete="new-password" minLength={12} required />
          <SubmitButton pending={register.isPending}>Submit request</SubmitButton>
          {register.data?.verdict === 'Submitted' && <Notice kind="success">Request submitted. Access begins only after approval.</Notice>}
          {register.data?.verdict === 'InvalidInput' && <Notice kind="error">Check the username, email, and password.</Notice>}
          {register.isError && <RequestError />}
        </form>
      )}

      {mode === 'recover' && (
        <form onSubmit={submitRecovery}>
          <Field name="username" label="Username" autoComplete="username" required />
          <Field name="recoveryCode" label="Recovery code" autoComplete="off" required />
          <Field name="newPassword" label="New password" type="password" autoComplete="new-password" minLength={12} required />
          <SubmitButton pending={recover.isPending}>Replace password</SubmitButton>
          {recover.data?.verdict === 'PasswordReplaced' && <Notice kind="success">Password replaced. You can now sign in.</Notice>}
          {recover.data && recover.data.verdict !== 'PasswordReplaced' && <Notice kind="error">The recovery code or account details are invalid.</Notice>}
          {recover.isError && <RequestError />}
        </form>
      )}
    </CenteredCard>
  )
}

function Library({ account }: { account: Account }) {
  const queryClient = useQueryClient()
  const signOut = useMutation({
    mutationFn: () => api.signOut(account.csrfToken),
    onSuccess: () => {
      queryClient.setQueryData(queryKeys.state, { claimed: true, signedIn: false })
      queryClient.removeQueries({ queryKey: queryKeys.account })
    },
  })

  return (
    <main className="app-shell">
      <header className="app-header">
        <Brand compact />
        <div className="account-menu">
          <span>{account.username}</span>
          <button className="quiet-button" onClick={() => signOut.mutate()} disabled={signOut.isPending}>Sign out</button>
        </div>
      </header>
      <section className="workspace">
        <div>
          <span className="eyebrow">Library</span>
          <h2>Your collection starts here</h2>
          <p>Account access is ready. Active Library Directories will appear here as scanning discovers playable Videos.</p>
        </div>
        {account.authority === 'Administrator' && (
          <>
            <InstallationSetup account={account} />
            <BackgroundWorkPanel account={account} />
            <AccountAdministration account={account} />
          </>
        )}
      </section>
    </main>
  )
}

function BackgroundWorkPanel({ account }: { account: Account }) {
  const configuration = useQuery({ queryKey: queryKeys.configuration, queryFn: api.configuration })
  const status = useQuery({
    queryKey: queryKeys.backgroundWork,
    queryFn: api.backgroundWork,
    refetchInterval: 2_000,
  })
  const queryClient = useQueryClient()
  const scan = useMutation({
    mutationFn: (libraryDirectoryId: string) =>
      api.queueLibraryScan(libraryDirectoryId, account.csrfToken),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: queryKeys.backgroundWork }),
  })

  return (
    <section className="admin-panel" aria-labelledby="work-title">
      <div className="section-heading">
        <div><span className="eyebrow">Administrator</span><h3 id="work-title">Background work</h3></div>
        {status.isFetching && <span className="muted">Refreshing…</span>}
      </div>
      <p>Library Scans and technical inspection resume from durable checkpoints after a restart.</p>
      <div className="scan-actions">
        {configuration.data?.libraryDirectories.map((directory) => (
          <button
            className="quiet-button"
            key={directory.id}
            onClick={() => scan.mutate(directory.id)}
            disabled={scan.isPending}
          >
            Scan {directory.name}
          </button>
        ))}
      </div>
      {status.data?.work.length === 0 && <p className="muted">No Background Work has run yet.</p>}
      {status.data?.work.map((work) => (
        <article className="work-row" key={work.id}>
          <div>
            <strong>{friendlyState(work.category)}</strong>
            <small>{work.libraryDirectoryName} · {friendlyState(work.state)}</small>
          </div>
          <span>{work.completedItemCount}/{work.discoveredCandidateCount}</span>
        </article>
      ))}
      {status.data?.issues.map((issue) => (
        <div className="work-issue" key={issue.id}>
          <strong>{friendlyState(issue.cause)} · {issue.affectedScope}</strong>
          <p>{issue.impact} {issue.requiredAction}</p>
        </div>
      ))}
      {(configuration.isError || status.isError || scan.isError) && <RequestError />}
    </section>
  )
}

function InstallationSetup({ account }: { account: Account }) {
  const configuration = useQuery({ queryKey: queryKeys.configuration, queryFn: api.configuration })
  const candidates = useQuery({
    queryKey: queryKeys.libraryDirectoryCandidates,
    queryFn: api.libraryDirectoryCandidates,
  })
  const queryClient = useQueryClient()
  const [stage, setStage] = useState<LibraryDirectoryStage>()
  const verify = useMutation({
    mutationFn: (credential: string) => api.verifyPrdb(credential, account.csrfToken),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: queryKeys.configuration }),
  })
  const retry = useMutation({
    mutationFn: () => api.retryPrdb(account.csrfToken),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: queryKeys.configuration }),
  })
  const stageDirectory = useMutation({
    mutationFn: ({ name, containerPath }: { name: string; containerPath: string }) =>
      api.stageLibraryDirectory(name, containerPath, account.csrfToken),
    onSuccess: (result) => {
      setStage(result)
      void queryClient.invalidateQueries({ queryKey: queryKeys.configuration })
    },
  })
  const activate = useMutation({
    mutationFn: (stageId: string) => api.activateLibraryDirectory(stageId, account.csrfToken),
    onSuccess: () => {
      setStage(undefined)
      void queryClient.invalidateQueries({ queryKey: queryKeys.configuration })
    },
  })

  function submitCredential(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = event.currentTarget
    verify.mutate(new FormData(form).get('credential')?.toString() ?? '', {
      onSuccess: (result) => {
        if (result.verdict === 'Verified') form.reset()
      },
    })
  }

  function submitDirectory(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    stageDirectory.mutate(values<{ name: string; containerPath: string }>(
      event.currentTarget,
      ['name', 'containerPath'],
    ))
  }

  const current = configuration.data
  const connectionReady = current?.prdbConnectionStatus === 'Verified'
  const connectionRetryable = current?.prdbConnectionStatus === 'VerificationPending' ||
    current?.prdbConnectionStatus === 'Degraded'

  return (
    <section className="admin-panel setup-panel" aria-labelledby="setup-title">
      <div className="section-heading">
        <div><span className="eyebrow">Installation</span><h3 id="setup-title">Configuration</h3></div>
        {current && <span className={`state-badge ${current.status === 'Configured' ? 'ready' : ''}`}>{friendlyState(current.status)}</span>}
      </div>

      {configuration.isPending && <p role="status">Loading configuration…</p>}
      {current && (
        <div className="setup-grid">
          <div className="setup-step">
            <div className="step-title"><span>1</span><div><strong>prdb connection</strong><small>{friendlyState(current.prdbConnectionStatus)}</small></div></div>
            <p>The API key is verified once and is never returned by the application.</p>
            <form onSubmit={submitCredential}>
              <Field name="credential" label={current.hasPrdbCredential ? 'Replacement API key' : 'API key'} type="password" autoComplete="off" required />
              <SubmitButton pending={verify.isPending}>{connectionReady ? 'Verify replacement' : 'Verify connection'}</SubmitButton>
            </form>
            {connectionRetryable && <button className="quiet-button inline-button" onClick={() => retry.mutate()} disabled={retry.isPending}>Retry verification</button>}
            {verify.data?.verdict === 'Verified' && <Notice kind="success">The prdb connection is verified.</Notice>}
            {verify.data?.verdict === 'Rejected' && <Notice kind="error">prdb rejected this API key. The previously verified key, if any, remains active.</Notice>}
            {verify.data?.verdict === 'VerificationPending' && <Notice kind="error">prdb is temporarily unavailable. The key is staged for a visible retry.</Notice>}
          </div>

          <div className="setup-step">
            <div className="step-title"><span>2</span><div><strong>Library Directory</strong><small>{current.libraryDirectories.length > 0 ? 'Active' : 'Required'}</small></div></div>
            <p>Select a readable directory mounted beneath <code>{current.libraryMountRoot}</code>. The container mount remains the Installation Operator's responsibility.</p>
            <form onSubmit={submitDirectory}>
              <Field name="name" label="Display name" placeholder="Main Library" required />
              <Field name="containerPath" label="Container path" list="library-directory-candidates" placeholder={`${current.libraryMountRoot}/main`} required />
              <datalist id="library-directory-candidates">
                {candidates.data?.containerPaths.map((path) => <option key={path} value={path} />)}
              </datalist>
              <SubmitButton pending={stageDirectory.isPending}>Validate directory</SubmitButton>
            </form>
            {stage?.verdict === 'Staged' && stage.stageId && (
              <div className="confirmation">
                <p><strong>{stage.name}</strong><br /><code>{stage.containerPath}</code></p>
                <button className="primary-button" onClick={() => activate.mutate(stage.stageId!)} disabled={activate.isPending}>Activate validated directory</button>
              </div>
            )}
            {stage && stage.verdict !== 'Staged' && <Notice kind="error">{directoryStageMessage(stage.verdict)}</Notice>}
            {current.libraryDirectories.map((directory) => (
              <div className="configured-directory" key={directory.id}>
                <strong>{directory.name}</strong><code>{directory.containerPath}</code>
              </div>
            ))}
          </div>
        </div>
      )}
      {(configuration.isError || candidates.isError || verify.isError || retry.isError || stageDirectory.isError || activate.isError) && <RequestError />}
    </section>
  )
}

function AccountAdministration({ account }: { account: Account }) {
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

  return (
    <section className="admin-panel" aria-labelledby="accounts-title">
      <div className="section-heading">
        <div><span className="eyebrow">Administrator</span><h3 id="accounts-title">Account requests</h3></div>
        {accounts.isFetching && <span className="muted">Refreshing…</span>}
      </div>
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
      {(accounts.isError || action.isError) && <RequestError />}
    </section>
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

function CenteredCard({ children }: { children: ReactNode }) {
  return <main className="shell"><section className="card">{children}</section></main>
}

function Brand({ compact = false }: { compact?: boolean }) {
  return <div className={compact ? 'brand compact' : 'brand'}><span aria-hidden="true">▶</span><h1>prdb-viewer</h1></div>
}

function Field({ label, ...props }: React.InputHTMLAttributes<HTMLInputElement> & { label: string }) {
  return <label className="field"><span>{label}</span><input {...props} /></label>
}

function Tab({ active, children, onClick }: { active: boolean; children: ReactNode; onClick: () => void }) {
  return <button type="button" className={active ? 'tab active' : 'tab'} aria-pressed={active} onClick={onClick}>{children}</button>
}

function SubmitButton({ pending, children }: { pending: boolean; children: ReactNode }) {
  return <button className="primary-button" type="submit" disabled={pending}>{pending ? 'Working…' : children}</button>
}

function Notice({ kind, children }: { kind: 'error' | 'success'; children: ReactNode }) {
  return <div className={`notice ${kind}`} role={kind === 'error' ? 'alert' : 'status'}>{children}</div>
}

function RequestError() {
  return <Notice kind="error">The request could not be completed. Try again.</Notice>
}

function values<T>(form: HTMLFormElement, keys: string[]): T {
  const data = new FormData(form)
  return Object.fromEntries(keys.map((key) => [key, data.get(key)?.toString() || null])) as T
}

function bootstrapMessage(verdict: string) {
  if (verdict === 'InvalidAuthorization') return 'The one-time authorization is invalid or expired.'
  if (verdict === 'AlreadyClaimed') return 'This installation has already been claimed.'
  return 'Check the authorization and account details.'
}

function signInMessage(verdict: string) {
  if (verdict === 'ApprovalPending') return 'Your request is waiting for Administrator approval.'
  if (verdict === 'Disabled') return 'This account has been disabled.'
  return 'The username or password is incorrect.'
}

function friendlyState(state: string) {
  return state.replace(/([a-z])([A-Z])/g, '$1 $2')
}

function directoryStageMessage(verdict: string) {
  if (verdict === 'OutsideMountArea') return 'Choose a directory beneath the documented library mount area.'
  if (verdict === 'Missing') return 'The directory is not mounted or no longer exists.'
  if (verdict === 'Unreadable') return 'The application identity cannot read this directory.'
  if (verdict === 'AlreadyConfigured') return 'This Library Directory is already active.'
  return 'Check the display name and container path.'
}
