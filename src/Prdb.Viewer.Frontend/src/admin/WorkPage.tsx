import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router'

import {
  api,
  type Account,
  type BackgroundWorkSummary,
  type WorkIssueAction,
  type WorkIssueSummary,
} from '../api/client'
import { exactTime, friendlyState, timeAgo } from '../lib/format'
import { queryKeys } from '../queryKeys'
import { firstError, PageHeading, RequestError, Tab } from '../ui'

export function WorkPage({ account }: { account: Account }) {
  const configuration = useQuery({ queryKey: queryKeys.configuration, queryFn: api.configuration })
  const status = useQuery({
    queryKey: queryKeys.backgroundWork,
    queryFn: api.backgroundWork,
    refetchInterval: 2_000,
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

      <div className="scan-actions">
        <button
          className={status.data?.paused ? 'primary-button inline-button' : 'quiet-button'}
          onClick={() => pause.mutate(!status.data?.paused)}
          disabled={pause.isPending || !status.data}
        >
          {status.data?.paused ? 'Resume background work' : 'Pause background work'}
        </button>
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
                {work.libraryDirectoryName} · {friendlyState(work.state)} · {work.phase}
                {work.waitingReason ? ` · ${work.waitingReason}` : ''}
                {' · '}
                <LaneTime work={work} />
              </small>
            </div>
            <div className="row-actions">
              <span>
                {work.completedPercent === null || work.completedPercent === undefined
                  ? `${work.completedItemCount}/${work.discoveredCandidateCount}`
                  : `${work.completedPercent}%`}
              </span>
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
