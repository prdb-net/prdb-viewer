import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useSearchParams } from 'react-router'

import {
  api,
  type Account,
  type IdentificationCase,
  type IdentificationConsequence,
  type IdentificationDecisionAction,
} from '../api/client'
import { candidateOrigin, friendlyState, provenanceLabel } from '../lib/format'
import { queryKeys } from '../queryKeys'
import { Field, Notice, PageHeading, RequestError } from '../ui'

export function IdentificationPage({ account }: { account: Account }) {
  const queryClient = useQueryClient()
  const queue = useQuery({
    queryKey: queryKeys.identificationQueue,
    queryFn: api.identificationQueue,
    refetchInterval: 15_000,
  })
  // ADR 0004: an administrative work item is linkable, so which case is open belongs in the
  // address rather than in this component. A colleague can then be sent the case itself.
  const [parameters, setParameters] = useSearchParams()
  const openCandidate = parameters.get('candidate')
  const selected = queue.data?.find((item) => item.candidate.id === openCandidate)
  const [pending, setPending] = useState<IdentificationDecisionAction>()
  const [consequence, setConsequence] = useState<IdentificationConsequence>()
  const [note, setNote] = useState('')
  const [target, setTarget] = useState({ key: '', title: '' })
  const [separated, setSeparated] = useState<string[]>([])
  const [outcome, setOutcome] = useState<string>()
  const openCase = useQuery({
    queryKey: queryKeys.identificationCase(selected?.videoId ?? 'none'),
    queryFn: () => api.identificationCase(selected!.videoId),
    enabled: selected !== undefined,
  })

  const reset = () => {
    setPending(undefined)
    setConsequence(undefined)
    setNote('')
    setSeparated([])
  }

  const open = (candidateId: string | undefined) => {
    setParameters((current) => {
      const next = new URLSearchParams(current)
      if (candidateId) next.set('candidate', candidateId)
      else next.delete('candidate')
      return next
    }, { replace: true })
  }

  const decide = useMutation({
    mutationFn: ({ action, confirm }: { action: IdentificationDecisionAction; confirm: boolean }) =>
      api.decideIdentification(
        selected!.videoId,
        {
          action,
          dimension: selected!.dimension,
          caseVersion: openCase.data?.caseVersion ?? selected!.caseVersion,
          confirm,
          candidateId: action === 'AcceptCandidate' || action === 'RejectCandidate'
            ? selected!.candidate.id
            : null,
          targetKey: target.key || null,
          targetTitle: target.title || null,
          note: note || null,
          separatedVideoFileIds: separated.length > 0 ? separated : null,
          retainPersonalStateWithContinuing: true,
        },
        account.csrfToken,
      ),
    onSuccess: (result, variables) => {
      setConsequence(result.consequence ?? undefined)
      if (result.verdict === 'Preview') {
        setPending(variables.action)
        return
      }
      if (result.verdict === 'Applied') {
        setOutcome(`${friendlyState(variables.action)} applied.`)
        open(undefined)
        reset()
        void queryClient.invalidateQueries({ queryKey: queryKeys.identificationQueue })
        void queryClient.invalidateQueries({ queryKey: ['videos'] })
        void queryClient.invalidateQueries({ queryKey: ['video'] })
        return
      }
      if (result.verdict === 'Stale') {
        setOutcome('The case changed while it was open. Review the refreshed comparison.')
        reset()
        void queryClient.invalidateQueries({ queryKey: queryKeys.identificationQueue })
        void openCase.refetch()
      }
      if (result.verdict === 'NoteRequired') setOutcome('This decision needs a note.')
      if (result.verdict === 'ActionUnavailable') {
        setOutcome('Correct the Work Identification instead of creating a second site truth.')
      }
      if (result.verdict === 'InvalidTarget') setOutcome('Provide a valid target for this decision.')
    },
  })

  const act = (action: IdentificationDecisionAction, confirm: boolean) =>
    decide.mutate({ action, confirm })

  return (
    <>
      <PageHeading
        eyebrow="Administrator"
        title="Identification review"
        actions={<span className="muted">{queue.data?.length ?? 0} open</span>}
      >
        Candidates and conflicts wait here. Nothing under review reaches ordinary browsing.
      </PageHeading>

      {outcome && <Notice kind="success">{outcome}</Notice>}
      {queue.data?.length === 0 && <p className="muted">No identification decision is waiting.</p>}

      <div className="review-queue">
        {queue.data?.map((item) => (
          <article className="review-item" key={item.candidate.id}>
            <div>
              <strong>{item.displayLabel}</strong>
              <small>
                {friendlyState(item.dimension)} · {friendlyState(item.candidate.evidenceClass)} ·
                {' '}{candidateOrigin(item.candidate.source)} ·
                {' '}proposes “{item.candidate.targetTitle}”
              </small>
              <small>{item.reason}</small>
            </div>
            <button
              className="quiet-button"
              onClick={() => { open(item.candidate.id); reset(); setOutcome(undefined) }}
            >Review</button>
          </article>
        ))}
      </div>

      {selected && openCase.data && (
        <div className="review-case">
          <div className="section-heading">
            <strong>{openCase.data.displayLabel}</strong>
            <button className="quiet-button" onClick={() => { open(undefined); reset() }}>Back to queue</button>
          </div>
          <p>{openCase.data.explanation}</p>
          <div className="comparison">
            <div>
              <span className="eyebrow">Current</span>
              <p>
                {reviewedClaim(openCase.data, selected.dimension).resolution === 'Established'
                  ? `Established “${reviewedClaim(openCase.data, selected.dimension).targetTitle}” · ${provenanceLabel(reviewedClaim(openCase.data, selected.dimension).source)}`
                  : 'Unknown'}
              </p>
              <small>{openCase.data.videoFiles.length} Video File(s)</small>
            </div>
            <div>
              <span className="eyebrow">Proposed</span>
              <p>{selected.candidate.targetTitle}</p>
              <small>{selected.candidate.evidenceSummary}</small>
            </div>
          </div>
          <ul className="case-files">
            {openCase.data.videoFiles.map((file) => (
              <li key={file.id}>
                {openCase.data!.videoFiles.length > 1 && (
                  <label>
                    <input
                      type="checkbox"
                      aria-label={`Separate ${file.relativePath}`}
                      checked={separated.includes(file.id)}
                      onChange={(event) => setSeparated((current) => event.target.checked
                        ? [...current, file.id]
                        : current.filter((id) => id !== file.id))}
                    />
                    <code>{file.relativePath}</code>
                  </label>
                )}
                {openCase.data!.videoFiles.length === 1 && <code>{file.relativePath}</code>}
                <small>
                  {file.containerFormat} · {file.videoCodec} · {friendlyState(file.hashState)}
                  {file.osHashSummary ? ` · osHash ${file.osHashSummary}` : ''}
                </small>
              </li>
            ))}
          </ul>
          <div className="assign-target">
            <Field name="targetKey" label="Target identifier" value={target.key} onChange={(event) => setTarget((current) => ({ ...current, key: event.target.value }))} />
            <Field name="targetTitle" label="Target title" value={target.title} onChange={(event) => setTarget((current) => ({ ...current, title: event.target.value }))} />
          </div>
          <div className="row-actions">
            <button onClick={() => act('AcceptCandidate', false)} disabled={decide.isPending}>Accept candidate</button>
            <button onClick={() => act('RejectCandidate', false)} disabled={decide.isPending}>Reject candidate</button>
            <button onClick={() => act('AssignDirectly', false)} disabled={decide.isPending}>Assign directly</button>
            <button onClick={() => act('ReplaceClaim', false)} disabled={decide.isPending}>Replace claim</button>
            <button onClick={() => act('RevokeClaim', false)} disabled={decide.isPending}>Revoke claim</button>
            {openCase.data.videoFiles.length > 1 && (
              <button onClick={() => act('SplitVideo', false)} disabled={decide.isPending}>Split Video</button>
            )}
          </div>

          {pending && consequence && (
            <div className="confirmation" role="group" aria-label="Consequence preview">
              <p>{consequence.claimTransition}</p>
              <p>{consequence.candidateTransition}</p>
              {consequence.mergeSummary && <p>{consequence.mergeSummary}</p>}
              <small>
                Affects {consequence.affectedVideoFileCount} Video File(s) · review becomes{' '}
                {friendlyState(consequence.resultingReviewStatus)}
              </small>
              {consequence.requiresNote && (
                <label className="field">
                  <span>Decision note</span>
                  <textarea value={note} onChange={(event) => setNote(event.target.value)} required />
                </label>
              )}
              <button
                className="primary-button"
                onClick={() => act(pending, true)}
                disabled={decide.isPending || (consequence.requiresNote && note.trim().length === 0)}
              >Confirm {friendlyState(pending).toLowerCase()}</button>
            </div>
          )}
        </div>
      )}
      {(queue.isError || openCase.isError || decide.isError) && <RequestError />}
    </>
  )
}

/// The claim the open case is actually about. Reviewing a proposed Site next to the current Work
/// Identification would compare two different questions.
function reviewedClaim(open: IdentificationCase, dimension: string) {
  return dimension === 'SiteRecognition' ? open.identification.site : open.identification.work
}
