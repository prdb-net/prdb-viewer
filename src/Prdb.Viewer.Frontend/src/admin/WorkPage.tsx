import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router'

import {
  api,
  type Account,
  type BackgroundWorkStatus,
  type BackgroundWorkSummary,
  type LibraryDirectorySummary,
  type WorkIssueAction,
  type WorkIssueSummary,
} from '../api/client'
import { exactTime, friendlyState, nextScanDue, timeAgo } from '../lib/format'
import { queryKeys } from '../queryKeys'
import { firstError, PageHeading, RequestError, Tab } from '../ui'

/// How often the screen looks while a lane is running, and while none is.
const runningPollMilliseconds = 2_000
const settledPollMilliseconds = 30_000

/// Whether anything is happening that the screen would want to show changing.
///
/// A run that has settled cannot move on its own; one that is running, waiting for its next
/// attempt, or being cancelled can. An unresolved issue counts too, because automatic recovery
/// resolves some of them without anyone acting.
function busy(status: BackgroundWorkStatus | undefined) {
  if (!status) return true

  return status.work.some((work) => work.phase !== settledPhase || work.cancellationRequested) ||
    status.issues.some((issue) => issue.remediationOwner === 'AutomaticRecovery')
}

/// The phase a run reports once it can no longer move on its own.
const settledPhase = 'Settled'

/// What the right-hand side of a lane row says about how far the lane got.
///
/// It used to be a bare `completed/discovered`, and two numbers with no unit beside them were not
/// answerable: `0/0` on a lane that had nothing left to do read as though nothing had ever
/// happened, and the two kinds of lane do not even mean the same thing by the pair. A Library Scan
/// discovers its own scope as it walks, so its denominator is only ever what it has found so far
/// and a ratio against it is meaningless. A derived lane knows how many admitted Video Files it
/// still has to advance, so while it runs a ratio is the honest answer. Neither is what an
/// Administrator wants from a lane that has settled: that one is not making progress, and its
/// result is the thing to say.
function progress(work: BackgroundWorkSummary) {
  // A queued lane has no counts to report, and the state beside it already says it is waiting.
  if (work.state === 'Queued') return null

  const found = Number(work.discoveredCandidateCount)
  const done = Number(work.completedItemCount)
  const settled = work.phase === settledPhase

  if (work.category === 'LibraryScan') {
    if (found === 0) return settled ? 'no files found' : 'looking for files'
    return settled ? `${files(found)} found` : `${files(found)} found so far`
  }

  if (found === 0) return settled ? 'nothing to do' : 'nothing to do yet'
  if (!settled) return `${done} of ${files(found)}`

  return done >= found ? `${files(found)} done` : `${done} of ${files(found)} done`
}

function files(count: number) {
  return count === 1 ? '1 file' : `${count} files`
}

export function WorkPage({ account }: { account: Account }) {
  const configuration = useQuery({ queryKey: queryKeys.configuration, queryFn: api.configuration })
  const status = useQuery({
    queryKey: queryKeys.backgroundWork,
    queryFn: api.backgroundWork,
    // Watch closely while something is actually happening, and stop watching closely when it is
    // not. An installation whose lanes have all settled was being asked thirty times a minute for
    // an answer that could not change without someone pressing something on this screen.
    refetchInterval: (query) => (busy(query.state.data) ? runningPollMilliseconds : settledPollMilliseconds),
  })
  const queryClient = useQueryClient()
  const refresh = () => void queryClient.invalidateQueries({ queryKey: queryKeys.backgroundWork })
  const [owner, setOwner] = useState<string>('All')
  const scan = useMutation({
    mutationFn: (libraryDirectoryId: string) =>
      api.queueLibraryScan(libraryDirectoryId, account.csrfToken),
    onSuccess: refresh,
  })
  const pause = useMutation({
    mutationFn: (paused: boolean) => api.pauseBackgroundWork(paused, account.csrfToken),
    onSuccess: refresh,
  })
  const cancel = useMutation({
    mutationFn: (workId: string) => api.cancelBackgroundWork(workId, account.csrfToken),
    onSuccess: refresh,
  })
  const issues = (status.data?.issues ?? []).filter(
    (issue) => owner === 'All' || issue.remediationOwner === owner,
  )

  return (
    <>
      <PageHeading
        eyebrow="Administrator"
        title="Background work"
        actions={status.isFetching ? <span className="muted">Refreshing…</span> : undefined}
      >
        Library Scans and every derived lane resume from durable checkpoints after a restart.
        {status.data?.paused && ' Background work is paused installation-wide.'}
      </PageHeading>

      {status.data?.operationalAttention && (
        <p className="attention-banner" role="status">
          <strong>Operational attention</strong>
          <span>
            {status.data.operationalAttentionCount} issue
            {Number(status.data.operationalAttentionCount) === 1 ? ' blocks' : 's block'} work until
            someone acts.
          </span>
        </p>
      )}

      {/* Scanning is what this screen is opened to do, so it leads while work is running. Pausing
          every lane is the rarer and heavier action, and the two used to be drawn as the same kind
          of thing; while work is paused, starting it again is the one that leads instead. */}
      <div className="scan-actions">
        {configuration.data?.libraryDirectories.map((directory) => (
          <button
            className={status.data?.paused ? 'quiet-button' : 'primary-button'}
            key={directory.id}
            onClick={() => scan.mutate(directory.id)}
            disabled={scan.isPending}
          >
            Scan {directory.name}
          </button>
        ))}
        <button
          className={status.data?.paused ? 'primary-button' : 'quiet-button'}
          onClick={() => pause.mutate(!status.data?.paused)}
          disabled={pause.isPending || !status.data}
        >
          {status.data?.paused ? 'Resume background work' : 'Pause background work'}
        </button>
      </div>

      {/* A row of buttons is an instruction, and read on its own it says that pressing one is how
          a new file is ever found. It is not: every Library Directory falls due on its own, and
          the button is only for the file that was copied in a minute ago. */}
      <Scheduled directories={configuration.data?.libraryDirectories ?? []} paused={status.data?.paused} />

      <section className="panel">
        <div className="section-heading"><h3>Lanes</h3></div>
        {status.data && status.data.work.length === 0 && (
          <p className="muted">No Background Work has run yet.</p>
        )}
        {lanes(status.data?.work ?? []).map((work) => (
          <article className="work-row" key={work.id}>
            <div>
              <strong>{friendlyState(work.category)}</strong>
              <small>
                {work.libraryDirectoryName} · {friendlyState(work.state)}
                {work.phase === settledPhase ? '' : ` · ${work.phase}`}
                {work.waitingReason ? ` · ${work.waitingReason}` : ''}
                {' · '}
                <LaneTime work={work} />
              </small>
            </div>
            <div className="row-actions">
              <span>{progress(work)}</span>
              {work.cancellable && (
                <button
                  className="quiet-button"
                  onClick={() => cancel.mutate(work.id)}
                  disabled={cancel.isPending}
                >
                  Cancel
                </button>
              )}
            </div>
          </article>
        ))}
      </section>

      {(status.data?.issues.length ?? 0) > 0 && (
        <div className="issue-filter">
          <span className="muted">Remediation owner</span>
          {['All', 'AutomaticRecovery', 'Administrator', 'InstallationOperator'].map((value) => (
            <Tab key={value} active={owner === value} onClick={() => setOwner(value)}>
              {friendlyState(value)}
            </Tab>
          ))}
        </div>
      )}
      {issues.map((issue) => (
        <WorkIssueCard key={issue.id} issue={issue} account={account} refresh={refresh} />
      ))}

      {(configuration.isError || status.isError || scan.isError || pause.isError ||
        cancel.isError) && (
        <RequestError
          error={firstError(
            configuration.error,
            status.error,
            scan.error,
            pause.error,
            cancel.error,
          )}
        />
      )}
    </>
  )
}

/// When each Library Directory is next walked without anyone asking.
///
/// The point of the line is the promise, not the times: an Administrator who has just copied a
/// file in should be able to read that leaving the screen alone is also an answer. A pause is the
/// one case where the promise is suspended, and it says so rather than naming a moment that will
/// come and go with nothing happening.
function Scheduled({ directories, paused }: {
  directories: LibraryDirectorySummary[]
  paused: boolean | undefined
}) {
  const scheduled = directories.filter((directory) => directory.nextScanDueAt)

  if (scheduled.length === 0) return null

  return (
    <p className="muted scan-schedule">
      {paused
        ? 'Scans also run on their own, and resume where the pause left them.'
        : (
          <>
            Scans also run on their own —{' '}
            {scheduled.map((directory, index) => (
              <span key={directory.id}>
                {index > 0 && ', '}
                {directory.name}{' '}
                <time
                  dateTime={directory.nextScanDueAt ?? undefined}
                  title={exactTime(directory.nextScanDueAt)}
                >
                  {nextScanDue(directory.nextScanDueAt)}
                </time>
              </span>
            ))}
            .
          </>
        )}
    </p>
  )
}

/// One row per lane, rather than one per run.
///
/// The endpoint answers with the last fifty runs, newest first — a history, which is the right
/// answer to give. Shown as it arrives it was the wrong thing to call Lanes: a second Scan of the
/// same Library Directory added a second row for every lane, and nothing on either row said which
/// of them was the current one. A lane is one category within one Library Directory, so what the
/// lane is doing is its newest run; the rest is history, and an older run that went wrong is
/// already spoken for by the issues below.
function lanes(work: BackgroundWorkSummary[]): BackgroundWorkSummary[] {
  const newest = new Map<string, BackgroundWorkSummary>()

  for (const run of work) {
    const lane = `${run.libraryDirectoryId}:${run.category}`
    const held = newest.get(lane)
    if (!held || Date.parse(run.requestedAt) > Date.parse(held.requestedAt)) {
      newest.set(lane, run)
    }
  }

  return [...newest.values()]
}

/// When this lane last did something, so a run that just happened is not mistaken for one from
/// yesterday. Which instant that is depends on how far the run got.
function LaneTime({ work }: { work: BackgroundWorkSummary }) {
  const at = work.finishedAt ?? work.lastActivityAt ?? work.startedAt ?? work.requestedAt
  const relative = timeAgo(at)

  if (!relative) return null

  return <time dateTime={at} title={exactTime(at)}>{relative}</time>
}

function WorkIssueCard({ issue, account, refresh }: {
  issue: WorkIssueSummary
  account: Account
  refresh: () => void
}) {
  const [showItems, setShowItems] = useState(false)
  const [copied, setCopied] = useState(false)
  const navigate = useNavigate()
  const items = useQuery({
    queryKey: queryKeys.workIssueItems(issue.id),
    queryFn: () => api.workIssueItems(issue.id),
    enabled: showItems,
  })
  const advance = useMutation({
    mutationFn: (action: WorkIssueAction) =>
      api.advanceWorkIssue(issue.id, action, issue.version, account.csrfToken),
    onSuccess: refresh,
  })

  return (
    <div className={`work-issue severity-${issue.severity}`}>
      <strong>{issue.summary}</strong>
      <p>{issue.detail}</p>
      <p className="muted">
        {issue.reference} · {friendlyState(issue.cause)} · {friendlyState(issue.category)} ·{' '}
        {issue.affectedScope}
        {issue.containerPath ? ` · ${issue.containerPath}` : ''} · owner{' '}
        {friendlyState(issue.remediationOwner)} · {issue.occurrenceCount} occurrence
        {Number(issue.occurrenceCount) === 1 ? '' : 's'}
        {Number(issue.affectedItemCount) > 1 ? ` across ${issue.affectedItemCount} items` : ''}
      </p>
      <p>{issue.impact} {issue.requiredAction}</p>
      {advance.data?.verdict === 'Stale' && (
        <p className="muted">This issue changed while it was displayed. The action was refused.</p>
      )}
      <div className="row-actions">
        {issue.actions.map((action) => (
          <button
            key={action}
            className="quiet-button"
            disabled={advance.isPending}
            onClick={() => {
              if (action === 'ViewAffectedItems') {
                setShowItems((shown) => !shown)
                return
              }

              if (action === 'CopyOperatorHandoff') {
                void navigator.clipboard?.writeText(issue.operatorHandoff ?? '')
                setCopied(true)
                return
              }

              // An issue that names what to correct now leads there, rather than scrolling to a
              // section that happened to be on the same page.
              if (action === 'OpenPrdbSettings') {
                void navigate('/admin/setup#prdb-connection')
                return
              }

              if (action === 'OpenLibraryDirectory') {
                void navigate('/admin/setup#library-directory')
                return
              }

              advance.mutate(action)
            }}
          >
            {issueActionLabel(action)}
          </button>
        ))}
      </div>
      {copied && <p className="muted">The operator handoff was copied.</p>}
      {showItems && (
        <ul className="issue-items">
          {items.data?.map((item) => (
            <li key={item.scope}>{item.scope}</li>
          ))}
        </ul>
      )}
      {advance.isError && <RequestError error={advance.error} />}
    </div>
  )
}

function issueActionLabel(action: WorkIssueAction) {
  switch (action) {
    case 'RetryNow': return 'Retry now'
    case 'CheckAgain': return 'Check again'
    case 'OpenPrdbSettings': return 'Open prdb settings'
    case 'OpenLibraryDirectory': return 'Open library directory'
    case 'ViewAffectedItems': return 'View affected items'
    default: return 'Copy operator handoff'
  }
}
