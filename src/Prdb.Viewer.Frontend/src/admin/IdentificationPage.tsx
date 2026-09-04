import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router'

import {
  api,
  type Account,
  type IdentificationCandidate,
  type IdentificationCase,
  type IdentificationConsequence,
  type IdentificationDecisionAction,
  type IdentificationDecisionOutlook,
  type IdentificationProposal,
  type IdentificationQueueItem,
} from '../api/client'
import { candidateOrigin, formatDay, friendlyState, provenanceLabel } from '../lib/format'
import { formatRuntime } from '../lib/quality'
import { withReturnTo } from '../lib/returnTo'
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

  // The case as the server describes it, which is where the decisions and their consequences come
  // from. The queue's copy of the candidate is the same proposal without them.
  const candidate = openCase.data?.openCandidates
    .find((row) => row.id === selected?.candidate.id) ?? selected?.candidate
  // What the open case is actually asking for, worked out once for the sentence that says it.
  const advice = selected && openCase.data && candidate
    ? guidance(openCase.data, selected, candidate)
    : undefined
  const showing = selected !== undefined && openCase.data !== undefined

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

      {/* An open case takes the queue's place rather than standing under it. The queue listed the
          case and then the case repeated it directly underneath, so the same Video appeared twice
          with two different sets of controls — and the second copy is the one that decides. */}
      {!showing && (
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
      )}

      {showing && candidate && (
        <div className="review-case">
          <div className="section-heading">
            <strong>{openCase.data!.displayLabel}</strong>
            <button className="quiet-button" onClick={() => { open(undefined); reset() }}>Back to queue</button>
          </div>
          <p>{openCase.data!.explanation}</p>

          {/* Deciding whether this file is that work means looking at both, which is why each side
              of the comparison leads with its picture. The words underneath say what the picture
              cannot: where each one came from and what is established about it. */}
          <div className="comparison">
            <div className="compared">
              <span className="eyebrow">This Video</span>
              <Picture
                url={openCase.data!.previewUrl}
                alt={`Preview frame of ${openCase.data!.displayLabel}`}
                absent="No preview frame has been generated for this Video yet."
              />
              <p>
                {reviewedClaim(openCase.data!, selected!.dimension).resolution === 'Established'
                  ? `Established “${reviewedClaim(openCase.data!, selected!.dimension).targetTitle}” · ${provenanceLabel(reviewedClaim(openCase.data!, selected!.dimension).source)}`
                  : 'Unknown'}
              </p>
              {/* The same facts as the proposal states, in the same order, so a Site stands above a
                  Site and a cast above a cast. Two readings of one thing are compared by finding
                  each fact in the same place on both, not by reading a paragraph for it. */}
              <KnownFacts open={openCase.data!} dimension={selected!.dimension} />
              {/* The Video's own page owns playback and every fallback decision, so this is a link
                  to it rather than a second player. It carries the way back, so watching enough of
                  the file to decide does not cost the case that was open. */}
              <Link
                className="quiet-button"
                to={withReturnTo(
                  `/videos/${openCase.data!.videoId}`,
                  `/admin/identification?candidate=${candidate.id}`,
                )}
              >Open this Video</Link>
            </div>
            <div className="compared">
              <span className="eyebrow">Proposed</span>
              <Proposal proposal={candidate.proposal} title={candidate.targetTitle} />
              <p>
                {candidate.targetUrl
                  ? <a href={candidate.targetUrl} target="_blank" rel="noreferrer">{candidate.targetTitle}</a>
                  : candidate.targetTitle}
              </p>
              <ProposedFacts proposal={candidate.proposal} />
              <small>{candidate.evidenceSummary}</small>
              {/* A proposal that repeats what is established is the one an Administrator reads
                  twice: the two columns say the same thing, and nothing on the screen used to
                  admit that this is why. */}
              {alreadyEstablished(
                reviewedClaim(openCase.data!, selected!.dimension).resolution,
                reviewedClaim(openCase.data!, selected!.dimension).targetTitle,
                candidate.targetTitle,
              ) && (
                <small className="already-established">This is what is already established.</small>
              )}
            </div>
          </div>

          <ul className="case-files">
            {openCase.data!.videoFiles.map((file) => (
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
              which of them are still alive. A case that refuses every decision but one has already
              made the choice, and one whose candidate repeats what is established has nothing to
              add either way; both reached an Administrator as a row of buttons that named no
              answer, so a case with one way out looked like an open question. */}
          {advice && (
            <p className="decision-guidance">
              <strong>{advice.lead}</strong> {advice.rest}
            </p>
          )}

          {/* Each decision with what it leaves behind, or with the reason it cannot be taken. The
              five controls used to say only what they do to the candidate, and the reasons the
              locked ones were locked sat under the whole row, where they read as a remark about
              the case rather than as the explanation of one control. */}
          <div className="decisions">
            {candidate.decisions.map((offered) => {
              const refusal = offered.refusal ?? tickRefusal(offered, openCase.data!, separated)
              const style = appearance(offered.action)
              return (
                <div className={refusal ? 'decision unavailable' : 'decision'} key={offered.action}>
                  <button
                    className={refusal ? `${style.appearance} unavailable` : style.appearance}
                    onClick={() => begin(offered.action)}
                    disabled={decide.isPending || refusal !== undefined}
                  >{style.label}</button>
                  <p>{refusal ?? offered.outcome}</p>
                </div>
              )
            })}
          </div>

          {/* Where a target belongs: with the decision that reads one. The two fields used to sit
              above every button, so a case whose only sensible answer was to reject a candidate
              still opened with a form, and the decision they belong to was not named anywhere. */}
          {targeting && (
            <div className="assign-target-form">
              <span className="eyebrow">{friendlyState(targeting)}</span>
              <p>{targetPrompt(targeting, selected!.dimension)}</p>
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

/// One side of the comparison's picture, or the reason there is none.
///
/// The preview lane has normally run by the time a candidate exists, but not always successfully,
/// and a picture that is merely missing looks like a picture that failed to load. Saying which is
/// the difference between a screen that is waiting and a screen that is broken.
function Picture({ url, alt, absent }: { url: string | null; alt: string; absent: string }) {
  if (!url) {
    return (
      <div className="compared-art">
        <div className="video-placeholder large" aria-hidden="true">▶</div>
        <small className="muted">{absent}</small>
      </div>
    )
  }

  return (
    <div className="compared-art">
      <img className="video-preview large" src={url} alt={alt} />
    </div>
  )
}

/// The picture prdb offers for the proposed work, once this installation holds it.
///
/// It is served from here rather than from prdb, so a review case is one origin and the catalogue
/// never learns which installation opened which case. A picture that has not arrived says so.
function Proposal({ proposal, title }: { proposal: IdentificationProposal | null; title: string }) {
  if (!proposal) {
    return (
      <Picture
        url={null}
        alt=""
        absent="prdb answered with an identifier for this work and no details to compare against."
      />
    )
  }

  return (
    <Picture
      url={proposal.artworkUrl}
      alt={`Artwork prdb holds for ${title}`}
      absent={proposal.artworkState === 'Pending'
        ? 'prdb offers a picture for this work; it has not been retained yet.'
        : proposal.artworkState === 'Unavailable'
          ? 'prdb offers a picture for this work, and it could not be retrieved.'
          : 'prdb holds no picture for this work.'}
    />
  )
}

/// What this installation already knows about the Video, in the terms the proposal states its own.
///
/// The claim under review is stated above this, so it is not repeated here: a Site Recognition
/// being reviewed appears once, as the thing the decision is about.
function KnownFacts({ open, dimension }: { open: IdentificationCase; dimension: string }) {
  const site = open.identification.site
  const runtime = formatRuntime(Number(open.videoFiles[0]?.durationMilliseconds ?? 0))
  const facts = [
    dimension !== 'SiteRecognition' && site.resolution === 'Established' && site.targetTitle
      ? { term: 'Site', value: site.targetTitle }
      : undefined,
    open.identification.actors.length > 0
      ? { term: 'Actors', value: open.identification.actors.join(', ') }
      : undefined,
    runtime ? { term: 'Runtime', value: runtime } : undefined,
  ].filter((fact) => fact !== undefined)

  return facts.length === 0 ? null : <Facts facts={facts} />
}

/// What prdb says the proposed work is, beside what the Video File says it is.
function ProposedFacts({ proposal }: { proposal: IdentificationProposal | null }) {
  if (!proposal) return null

  const runtime = formatRuntime(Number(proposal.durationMilliseconds ?? 0))
  const released = formatDay(proposal.releaseDate)
  const facts = [
    proposal.siteTitle ? { term: 'Site', value: proposal.siteTitle } : undefined,
    proposal.actors.length > 0 ? { term: 'Actors', value: proposal.actors.join(', ') } : undefined,
    released ? { term: 'Released', value: released } : undefined,
    runtime ? { term: 'Runtime', value: runtime } : undefined,
  ].filter((fact) => fact !== undefined)

  return facts.length === 0 ? null : <Facts facts={facts} />
}

/// A short list of named facts, drawn the same way on both sides of the comparison.
function Facts({ facts }: { facts: { term: string; value: string }[] }) {
  return (
    <dl className="compared-facts">
      {facts.map((fact) => (
        <div key={fact.term}>
          <dt>{fact.term}</dt>
          <dd>{fact.value}</dd>
        </div>
      ))}
    </dl>
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
///
/// What each decision then leaves behind is said beside the decision itself, so this names the
/// answer and stops rather than describing an outcome a second time.
function guidance(
  open: IdentificationCase,
  item: IdentificationQueueItem,
  candidate: IdentificationCandidate,
) {
  const claim = reviewedClaim(open, item.dimension)
  // A split is not refused so much as unfinished: it waits for its Video Files to be ticked. So it
  // counts as a decision the case offers whenever there is more than one file to separate, and the
  // sentence does not claim to be the only way out while a real second one is a tick away.
  const available = candidate.decisions.filter((offered) => offered.refusal === null)
  const only = available.length === 1 ? available[0] : undefined
  const repeats = alreadyEstablished(claim.resolution, claim.targetTitle, candidate.targetTitle)

  if (!only && !repeats) return undefined

  return {
    lead: only
      ? `“${appearance(only.action).label}” is the only decision this case offers.`
      : '“Reject candidate” is the decision this case asks for.',
    rest: repeats
      ? `The candidate proposes the ${friendlyState(item.dimension)} that is already established, ` +
        'so accepting it would establish nothing new.'
      : '',
  }
}

/// How each decision is drawn. Accepting what was proposed is the one an open case is normally
/// closed with, so it leads; withdrawing knowledge the library has already established is the one
/// that takes something away, so it is coloured like it. The order the case offers them in is the
/// server's, because the sentences beneath them are too.
function appearance(action: IdentificationDecisionAction) {
  const styles: Record<IdentificationDecisionAction, { label: string; appearance: string }> = {
    AcceptCandidate: { label: 'Accept candidate', appearance: 'primary-button' },
    RejectCandidate: { label: 'Reject candidate', appearance: 'quiet-button' },
    AssignDirectly: { label: 'Assign directly', appearance: 'quiet-button' },
    ReplaceClaim: { label: 'Replace claim', appearance: 'quiet-button' },
    RevokeClaim: { label: 'Revoke claim', appearance: 'danger-button' },
    SplitVideo: { label: 'Split Video', appearance: 'quiet-button' },
  }

  return styles[action]
}

/// The one refusal the server cannot make, because it turns on what is ticked here rather than on
/// anything the case knows. Everything else a decision would be refused for is decided where the
/// rules live and arrives with the case.
function tickRefusal(
  offered: IdentificationDecisionOutlook,
  open: IdentificationCase,
  separated: string[],
) {
  if (offered.action !== 'SplitVideo') return undefined
  if (separated.length === 0) return 'Tick the Video Files that belong to a Video of their own.'
  if (separated.length === open.videoFiles.length) {
    return 'A split leaves at least one Video File with this Video.'
  }

  return undefined
}

/// The two decisions that establish an identification nobody proposed, and so read the target
/// fields. The other four already have their subject: a candidate, the current claim, or the
/// Video Files that were ticked.
type TargetedAction = Extract<IdentificationDecisionAction, 'AssignDirectly' | 'ReplaceClaim'>

function needsTarget(action: IdentificationDecisionAction): action is TargetedAction {
  return action === 'AssignDirectly' || action === 'ReplaceClaim'
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
