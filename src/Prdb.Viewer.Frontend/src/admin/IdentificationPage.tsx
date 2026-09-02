import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useSearchParams } from 'react-router'

import {
  api,
  type Account,
  type IdentificationCase,
  type IdentificationConsequence,
  type IdentificationDecisionAction,
  type IdentificationQueueItem,
} from '../api/client'
import { candidateOrigin, friendlyState, provenanceLabel } from '../lib/format'
import { queryKeys } from '../queryKeys'
import { Field, firstError, Notice, PageHeading, RequestError } from '../ui'

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
  const [targeting, setTargeting] = useState<TargetedAction>()
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
    setTarget({ key: '', title: '' })
    setTargeting(undefined)
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
          // A target belongs to the two decisions that read one. Sending it alongside an accepted
          // candidate said, in the request, that the typed fields had something to do with it.
          targetKey: needsTarget(action) ? target.key.trim() || null : null,
          targetTitle: needsTarget(action) ? target.title.trim() || null : null,
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

  /// What a decision button does when it is pressed. The two decisions that establish an
  /// identification of the Administrator's own need to be told which one first, so they ask before
  /// they are previewed; every other decision already knows everything it needs.
  const begin = (action: IdentificationDecisionAction) => {
    setOutcome(undefined)
    // A preview belongs to the decision it was asked for. Leaving the last one on screen offered a
    // Confirm button under a heading naming a decision nobody had asked for again.
    setPending(undefined)
    setConsequence(undefined)
    if (needsTarget(action)) {
      setTargeting(action)
      return
    }
    setTargeting(undefined)
    act(action, false)
  }

  // What the open case is actually asking for, worked out once for the sentence that says it.
  const advice = selected && openCase.data ? guidance(openCase.data, selected) : undefined

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
      {queue.data?.length === 0 && (
        <div className="empty-library">
          <strong>Nothing to review</strong>
          <p>No identification decision is waiting.</p>
        </div>
      )}

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
              {alreadyEstablished(item.currentResolution, item.currentTargetTitle, item.candidate.targetTitle) && (
                <small className="already-established">Proposes what is already established here.</small>
              )}
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
              <small>{files(openCase.data.videoFiles.length)}</small>
            </div>
            <div>
              <span className="eyebrow">Proposed</span>
              <p>{selected.candidate.targetTitle}</p>
              <small>{selected.candidate.evidenceSummary}</small>
              {/* A proposal that repeats what is established is the one an Administrator reads
                  twice: the two columns say the same thing, and nothing on the screen used to
                  admit that this is why. */}
              {alreadyEstablished(
                reviewedClaim(openCase.data, selected.dimension).resolution,
                reviewedClaim(openCase.data, selected.dimension).targetTitle,
                selected.candidate.targetTitle,
              ) && (
                <small className="already-established">This is what is already established.</small>
              )}
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
          {/* What the case is asking for, said before the decisions rather than left to be read off
              which of them are still alive. A case that refuses four of its five decisions has
              already made the choice, and one whose candidate repeats what is established has
              nothing to add either way; both reached an Administrator as a row of buttons that
              named no answer, so a case with one way out looked like an open question. */}
          {advice && (
            <p className="decision-guidance">
              <strong>{advice.lead}</strong> {advice.rest}
            </p>
          )}
          {/* The decisions this case offers, in the shapes they are. Accepting what was proposed is
              the one an open case is normally closed with, so it leads; withdrawing knowledge the
              library has already established is the one that takes something away, so it is
              coloured like it. They arrived here as unstyled browser buttons, which said nothing
              about any of that and looked like nothing else in the application.

              A decision this case would refuse is now refused here rather than by the request:
              offering all five as though any of them were available made the screen a place to
              find out what it does not do by pressing things. */}
          <div className="row-actions">
            {decisions
              .filter(({ action }) => action !== 'SplitVideo' || openCase.data!.videoFiles.length > 1)
              .map(({ action, label, appearance }) => {
                const refusal = refusalOf(openCase.data!, selected.dimension, action, separated)
                return (
                  <button
                    key={action}
                    className={refusal ? `${appearance} unavailable` : appearance}
                    title={refusal}
                    onClick={() => begin(action)}
                    disabled={decide.isPending || refusal !== undefined}
                  >{label}</button>
                )
              })}
          </div>
          {refusals(openCase.data, selected.dimension, separated).map((refusal) => (
            <p className="muted" key={refusal}>{refusal}</p>
          ))}

          {/* Where a target belongs: with the decision that reads one. The two fields used to sit
              above every button, so a case whose only sensible answer was to reject a candidate
              still opened with a form, and the decision they belong to was not named anywhere. */}
          {targeting && (
            <div className="assign-target-form">
              <span className="eyebrow">{friendlyState(targeting)}</span>
              <p>{targetPrompt(targeting, selected.dimension)}</p>
              <div className="assign-target">
                <Field name="targetKey" label="Target identifier" value={target.key} onChange={(event) => setTarget((current) => ({ ...current, key: event.target.value }))} />
                <Field name="targetTitle" label="Target title" value={target.title} onChange={(event) => setTarget((current) => ({ ...current, title: event.target.value }))} />
              </div>
              <div className="row-actions">
                <button
                  className="primary-button"
                  onClick={() => act(targeting, false)}
                  disabled={decide.isPending || !target.key.trim() || !target.title.trim()}
                >Preview {friendlyState(targeting).toLowerCase()}</button>
                <button
                  className="quiet-button"
                  onClick={() => { setTargeting(undefined); setPending(undefined); setConsequence(undefined) }}
                >Cancel</button>
              </div>
            </div>
          )}

          {pending && consequence && (
            <div className="confirmation" role="group" aria-label="Consequence preview">
              <p>{consequence.claimTransition}</p>
              <p>{consequence.candidateTransition}</p>
              {consequence.mergeSummary && <p>{consequence.mergeSummary}</p>}
              <small>
                Affects {files(Number(consequence.affectedVideoFileCount))} · review becomes{' '}
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
      {(queue.isError || openCase.isError || decide.isError) && (
        <RequestError error={firstError(queue.error, openCase.error, decide.error)} />
      )}
    </>
  )
}

/// A count of Video Files, in the plural it actually has. "1 Video File(s)" made the reader do
/// the agreement the sentence would not.
function files(count: number) {
  return `${count} Video File${count === 1 ? '' : 's'}`
}

/// The claim the open case is actually about. Reviewing a proposed Site next to the current Work
/// Identification would compare two different questions.
function reviewedClaim(open: IdentificationCase, dimension: string) {
  return dimension === 'SiteRecognition' ? open.identification.site : open.identification.work
}

/// Whether a proposal says what its subject already knows. Local recognition reads a Video File's
/// path whether or not prdb already answered the same question, so a candidate that repeats an
/// established identification is ordinary rather than exceptional — and it is the one that reads
/// as a screen asking for a decision it does not need.
function alreadyEstablished(
  resolution: string | null | undefined,
  established: string | null | undefined,
  proposed: string | null | undefined,
) {
  return resolution === 'Established' && Boolean(proposed) && established === proposed
}

/// What the open case is asking of the Administrator, in a sentence.
///
/// Two cases answer themselves. One refuses every decision but a single remaining one, and the only
/// thing on the screen that said so was the colour of four disabled buttons. The other has a
/// candidate proposing the identification that is already established, so there is nothing for
/// accepting it to establish. Both are ordinary — local recognition reads a Video File's own path
/// whether or not prdb has answered the same question — and both used to ask for a judgement the
/// evidence had already made, without saying what pressing anything would do.
function guidance(open: IdentificationCase, item: IdentificationQueueItem) {
  const dimension = item.dimension
  const claim = reviewedClaim(open, dimension)
  // A split is not refused so much as unfinished: it waits for its Video Files to be ticked. So it
  // counts as a decision the case offers whenever there is more than one file to separate, and the
  // sentence does not claim to be the only way out while a real second one is a tick away.
  const available = decisions.filter(({ action }) => action === 'SplitVideo'
    ? open.videoFiles.length > 1
    : refusalOf(open, dimension, action, []) === undefined)
  const only = available.length === 1 ? available[0] : undefined
  const repeats = alreadyEstablished(claim.resolution, claim.targetTitle, item.candidate.targetTitle)

  if (!only && !repeats) return undefined

  const rejecting = only === undefined || only.action === 'RejectCandidate'
  const waiting = open.openCandidates
    .filter((candidate) => candidate.id !== item.candidate.id).length
  const standing = claim.resolution === 'Established'
    ? `established as “${claim.targetTitle}”`
    : 'Unknown'
  const because = repeats
    ? `The candidate proposes the ${friendlyState(dimension)} that is already established, so ` +
      'accepting it would establish nothing new. '
    : ''
  const after = rejecting
    ? `Rejecting it leaves the ${friendlyState(dimension)} ${standing}; ` + (waiting === 0
      ? 'nothing else on this Video then waits for a decision.'
      : `${waiting} other candidate${waiting === 1 ? '' : 's'} on this Video still ` +
        `${waiting === 1 ? 'waits' : 'wait'} for a decision.`)
    : ''

  return {
    lead: only
      ? `“${only.label}” is the only decision this case offers.`
      : '“Reject candidate” is the decision this case asks for.',
    rest: `${because}${after}`.trim(),
  }
}

/// The decisions a case can offer, in the order they are offered.
const decisions: { action: IdentificationDecisionAction; label: string; appearance: string }[] = [
  { action: 'AcceptCandidate', label: 'Accept candidate', appearance: 'primary-button' },
  { action: 'RejectCandidate', label: 'Reject candidate', appearance: 'quiet-button' },
  { action: 'AssignDirectly', label: 'Assign directly', appearance: 'quiet-button' },
  { action: 'ReplaceClaim', label: 'Replace claim', appearance: 'quiet-button' },
  { action: 'RevokeClaim', label: 'Revoke claim', appearance: 'danger-button' },
  { action: 'SplitVideo', label: 'Split Video', appearance: 'quiet-button' },
]

/// The two decisions that establish an identification nobody proposed, and so read the target
/// fields. The other four already have their subject: a candidate, the current claim, or the
/// Video Files that were ticked.
type TargetedAction = Extract<IdentificationDecisionAction, 'AssignDirectly' | 'ReplaceClaim'>

function needsTarget(action: IdentificationDecisionAction): action is TargetedAction {
  return action === 'AssignDirectly' || action === 'ReplaceClaim'
}

/// Why this case would refuse a decision, said before it is made rather than after.
///
/// Every reason here is one the server checks too — it remains the authority, and a case that
/// changes under an open screen is still caught by its version. What the screen owes the reader is
/// that a decision it cannot make does not look like one it can.
function refusalOf(
  open: IdentificationCase,
  dimension: string,
  action: IdentificationDecisionAction,
  separated: string[],
): string | undefined {
  if (dimension === 'SiteRecognition' && open.unavailableSiteActions.includes(action)) {
    return 'This Site Recognition came with the Work Identification. Correct that instead of establishing a second site truth.'
  }

  const established = reviewedClaim(open, dimension).resolution === 'Established'
  if (action === 'AssignDirectly' && established) {
    return 'Something is already established here. Replace claim is the decision that changes it.'
  }
  if (action === 'ReplaceClaim' && !established) {
    return 'Nothing is established here yet. Assign directly is the decision that establishes one.'
  }
  if (action === 'RevokeClaim' && !established) {
    return 'Nothing is established here to withdraw.'
  }
  if (action === 'SplitVideo' && separated.length === 0) {
    return 'Tick the Video Files that belong to a Video of their own.'
  }
  if (action === 'SplitVideo' && separated.length === open.videoFiles.length) {
    return 'A split leaves at least one Video File with this Video.'
  }

  return undefined
}

/// The reasons the offered decisions were refused, each said once. Four buttons turned off for one
/// reason are one sentence, not four.
function refusals(open: IdentificationCase, dimension: string, separated: string[]) {
  const offered = decisions.filter(({ action }) =>
    action !== 'SplitVideo' || open.videoFiles.length > 1)
  return Array.from(new Set(offered
    .map(({ action }) => refusalOf(open, dimension, action, separated))
    .filter((refusal) => refusal !== undefined)))
}

/// What the target fields are being asked for, in the words of the decision that asked.
function targetPrompt(action: TargetedAction, dimension: string) {
  const subject = dimension === 'SiteRecognition'
    ? 'the Site this Video came from'
    : 'the work this Video is'
  const override = 'It is established as an Administrative Override, which conflicting automation cannot silently replace.'
  return action === 'AssignDirectly'
    ? `Name ${subject}. ${override}`
    : `Name ${subject} in place of what is established. ${override}`
}
