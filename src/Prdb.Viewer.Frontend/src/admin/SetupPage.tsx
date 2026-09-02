import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import {
  api,
  type Account,
  type LibraryDirectoryStage,
  type LibraryDirectorySummary,
} from '../api/client'
import { directoryStageMessage, exactTime, friendlyState, nextScanDue, timeAgo } from '../lib/format'
import { queryKeys } from '../queryKeys'
import { Field, firstError, Notice, PageHeading, RequestError, SubmitButton, submitting, values } from '../ui'

export function SetupPage({ account }: { account: Account }) {
  const configuration = useQuery({ queryKey: queryKeys.configuration, queryFn: api.configuration })
  const candidates = useQuery({
    queryKey: queryKeys.libraryDirectoryCandidates,
    queryFn: api.libraryDirectoryCandidates,
  })
  const queryClient = useQueryClient()
  const [stage, setStage] = useState<LibraryDirectoryStage>()
  /// Whether the Administrator asked to replace a key that is already verified.
  const [replacing, setReplacing] = useState(false)
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

  const current = configuration.data
  const connectionReady = current?.prdbConnectionStatus === 'Verified'
  const connectionRetryable = current?.prdbConnectionStatus === 'VerificationPending' ||
    current?.prdbConnectionStatus === 'Degraded'

  return (
    <>
      <PageHeading
        eyebrow="Installation"
        title="Configuration"
        actions={current && (
          <span className={`state-badge ${current.status === 'Configured' ? 'ready' : ''}`}>
            {friendlyState(current.status)}
          </span>
        )}
      >
        What this installation needs before it can build a library: a verified prdb connection and
        at least one Library Directory it may read.
      </PageHeading>

      {configuration.isPending && <p role="status">Loading configuration…</p>}
      {current && (
        <div className="setup-grid">
          <section className="setup-step" id="prdb-connection">
            <div className="step-title">
              <span>1</span>
              <div><strong>prdb connection</strong><small>{friendlyState(current.prdbConnectionStatus)}</small></div>
            </div>
            <p>The API key is verified once and is never returned by the application.</p>
            {/* A verified connection needs nothing done to it. Leaving the replacement field open
                made the rarest action on this screen — and the one that can cost the installation
                its identification — look like the thing the step was there for. */}
            {connectionReady && !replacing
              ? (
                <button
                  className="quiet-button inline-button"
                  onClick={() => setReplacing(true)}
                >Replace the API key</button>
                )
              : (
                <form onSubmit={submitting((form) => verify.mutate(
                  new FormData(form).get('credential')?.toString() ?? '',
                  {
                    onSuccess: (result) => {
                      if (result.verdict !== 'Verified') return
                      form.reset()
                      setReplacing(false)
                    },
                  },
                ))}>
                  <Field
                    name="credential"
                    label={current.hasPrdbCredential ? 'Replacement API key' : 'API key'}
                    type="password"
                    autoComplete="off"
                    required
                  />
                  <div className="row-actions">
                    <SubmitButton pending={verify.isPending}>
                      {connectionReady ? 'Verify replacement' : 'Verify connection'}
                    </SubmitButton>
                    {connectionReady && (
                      <button
                        type="button"
                        className="quiet-button"
                        onClick={() => setReplacing(false)}
                      >Cancel</button>
                    )}
                  </div>
                </form>
                )}
            {connectionRetryable && (
              <button
                className="quiet-button inline-button"
                onClick={() => retry.mutate()}
                disabled={retry.isPending}
              >Retry verification</button>
            )}
            {verify.data?.verdict === 'Verified' && <Notice kind="success">The prdb connection is verified.</Notice>}
            {verify.data?.verdict === 'Rejected' && <Notice kind="error">prdb rejected this API key. The previously verified key, if any, remains active.</Notice>}
            {verify.data?.verdict === 'VerificationPending' && <Notice kind="error">prdb is temporarily unavailable. The key is staged for a visible retry.</Notice>}
          </section>

          <section className="setup-step" id="library-directory">
            <div className="step-title">
              <span>2</span>
              <div><strong>Library Directory</strong><small>{current.libraryDirectories.length > 0 ? 'Active' : 'Required'}</small></div>
            </div>
            <p>Select a readable directory mounted beneath <code>{current.libraryMountRoot}</code>. The container mount remains the Installation Operator's responsibility.</p>
            <form onSubmit={submitting((form) => stageDirectory.mutate(
              values<{ name: string; containerPath: string }>(form, ['name', 'containerPath']),
            ))}>
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
                <button
                  className="primary-button"
                  onClick={() => activate.mutate(stage.stageId!)}
                  disabled={activate.isPending}
                >Activate validated directory</button>
              </div>
            )}
            {stage && stage.verdict !== 'Staged' && <Notice kind="error">{directoryStageMessage(stage.verdict)}</Notice>}
            {current.libraryDirectories.map((directory) => (
              <ConfiguredDirectory
                key={directory.id}
                directory={directory}
                account={account}
                onRemoved={() => {
                  void queryClient.invalidateQueries({ queryKey: queryKeys.configuration })
                  void queryClient.invalidateQueries({ queryKey: ['videos'] })
                  void queryClient.invalidateQueries({ queryKey: queryKeys.backgroundWork })
                }}
              />
            ))}
          </section>
        </div>
      )}
      {(configuration.isError || candidates.isError || verify.isError || retry.isError ||
        stageDirectory.isError || activate.isError) && (
        <RequestError
          error={firstError(
            configuration.error,
            candidates.error,
            verify.error,
            retry.error,
            stageDirectory.error,
            activate.error,
          )}
        />
      )}
    </>
  )
}

/// One configured Library Directory, as more than its name and its path.
///
/// This is the screen an Operator opens when the library is empty, so it is where the directory has
/// to answer for itself: whether it is reachable, when a Scan last read it, what that Scan found,
/// and how many Video Files are available beneath it now. "Configured" and "holds Videos" are
/// different facts, and only saying the first left the second nowhere to be read.
function ConfiguredDirectory({ directory, account, onRemoved }: {
  directory: LibraryDirectorySummary
  account: Account
  onRemoved: () => void
}) {
  const [confirming, setConfirming] = useState(false)
  const remove = useMutation({
    mutationFn: () => api.removeLibraryDirectory(directory.id, account.csrfToken),
    onSuccess: (result) => {
      if (result.verdict === 'Removed') {
        setConfirming(false)
        onRemoved()
      }
    },
  })
  const healthy = directory.health === 'Healthy'

  return (
    <div className="configured-directory">
      <div className="configured-directory-identity">
        <strong>{directory.name}</strong>
        <code>{directory.containerPath}</code>
      </div>

      <p className={healthy ? 'muted' : 'directory-unhealthy'}>
        {friendlyState(directory.state)} · {friendlyState(directory.health)} ·{' '}
        {directory.availableVideoFileCount} Video File
        {Number(directory.availableVideoFileCount) === 1 ? '' : 's'} available
      </p>

      <p className="muted"><ScanOutcome directory={directory} /></p>

      {confirming ? (
        <div className="confirmation">
          <p>
            Removing <strong>{directory.name}</strong> withdraws its Video Files from the active
            library. Everything established about them is kept — identification, history, and every
            Account's own viewing and organisation — and a Video also backed by another Library
            Directory stays available. Any Scan of this directory stops.
          </p>
          <div className="row-actions">
            <button
              className="primary-button"
              onClick={() => remove.mutate()}
              disabled={remove.isPending}
            >
              {remove.isPending ? 'Removing…' : 'Remove it'}
            </button>
            <button
              className="quiet-button"
              onClick={() => setConfirming(false)}
              disabled={remove.isPending}
            >Keep it</button>
          </div>
        </div>
      ) : (
        <button className="quiet-button" onClick={() => setConfirming(true)}>
          Remove this directory
        </button>
      )}

      {remove.isError && <RequestError error={remove.error} />}
      {remove.data?.verdict === 'AlreadyRemoved' && (
        <Notice kind="error">This Library Directory was already removed.</Notice>
      )}
      {remove.data?.verdict === 'NotFound' && (
        <Notice kind="error">This Library Directory no longer exists.</Notice>
      )}
    </div>
  )
}

/// What the last completed Scan of this directory found, in a sentence.
///
/// `Completed · 0/0` is true and says nothing. An Operator staring at an empty library needs the
/// difference between "read it, there is nothing there" and "nothing has read it yet".
function ScanOutcome({ directory }: { directory: LibraryDirectorySummary }) {
  const at = directory.lastScanCompletedAt
  const due = nextScanDue(directory.nextScanDueAt)
  // When the directory is read again without anyone asking. It belongs beside the last Scan
  // because that is where the question "does anything happen if I do nothing?" is asked.
  const next = due
    ? (
      <>
        {' '}The next one is due{' '}
        <time dateTime={directory.nextScanDueAt ?? undefined} title={exactTime(directory.nextScanDueAt)}>
          {due}
        </time>.
      </>
    )
    : null

  if (!at) {
    return <>No Library Scan of this directory has finished yet.{next}</>
  }

  const candidates = Number(directory.lastScanCandidateCount)
  const found = candidates === 0
    ? 'found no Video File Candidates'
    : `found ${candidates} Video File Candidate${candidates === 1 ? '' : 's'}`
  const coverage = directory.lastScanCoveredEverything
    ? ''
    : ' It did not cover the whole directory.'

  return (
    <>
      Last Scan finished <time dateTime={at} title={exactTime(at)}>{timeAgo(at)}</time> and {found}.
      {coverage}
      {next}
    </>
  )
}
