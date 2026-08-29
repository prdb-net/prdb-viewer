import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { api, type Account, type LibraryDirectoryStage } from '../api/client'
import { directoryStageMessage, friendlyState } from '../lib/format'
import { queryKeys } from '../queryKeys'
import { Field, Notice, PageHeading, RequestError, SubmitButton, submitting, values } from '../ui'

export function SetupPage({ account }: { account: Account }) {
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
            <form onSubmit={submitting((form) => verify.mutate(
              new FormData(form).get('credential')?.toString() ?? '',
              { onSuccess: (result) => { if (result.verdict === 'Verified') form.reset() } },
            ))}>
              <Field
                name="credential"
                label={current.hasPrdbCredential ? 'Replacement API key' : 'API key'}
                type="password"
                autoComplete="off"
                required
              />
              <SubmitButton pending={verify.isPending}>
                {connectionReady ? 'Verify replacement' : 'Verify connection'}
              </SubmitButton>
            </form>
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
              <div className="configured-directory" key={directory.id}>
                <strong>{directory.name}</strong><code>{directory.containerPath}</code>
              </div>
            ))}
          </section>
        </div>
      )}
      {(configuration.isError || candidates.isError || verify.isError || retry.isError || stageDirectory.isError || activate.isError) && <RequestError />}
    </>
  )
}
