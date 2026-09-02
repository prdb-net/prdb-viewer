import { screen, waitFor, within } from '@testing-library/react'

import { json, renderApp, signedInAs } from '../test/fixtures'

/// What an Administrator can tell about background work from the screen alone.
describe('Background work', () => {
  afterEach(() => vi.restoreAllMocks())

  it('shows each lane once, at its newest run, and says when that was', async () => {
    signedInAs('Administrator', (input) => {
      if (input === '/api/admin/background-work/') {
        return json({
          work: [
            run({
              id: 'newer',
              category: 'LibraryScan',
              requestedAt: '2026-08-29T12:00:00Z',
              finishedAt: '2026-08-29T12:00:04Z',
              completedItemCount: 7,
              discoveredCandidateCount: 7,
            }),
            run({
              id: 'older',
              category: 'LibraryScan',
              requestedAt: '2026-08-28T16:19:41Z',
              finishedAt: '2026-08-28T16:19:42Z',
            }),
            run({
              id: 'other-lane',
              category: 'Hashing',
              requestedAt: '2026-08-29T12:00:05Z',
              finishedAt: '2026-08-29T12:00:06Z',
            }),
          ],
          issues: [],
          resolvedIssues: [],
          operationalAttention: false,
          operationalAttentionCount: 0,
          paused: false,
        })
      }
      if (input === '/api/admin/configuration/') {
        return json({ status: 'Configured', libraryDirectories: [] })
      }
      return undefined
    })

    renderApp('/admin/work')

    await screen.findByRole('heading', { name: 'Background work' })
    const lanes = await screen.findByRole('heading', { name: 'Lanes' })
    const panel = lanes.closest('section')!

    // A second Scan used to add a second row for every lane, with nothing to tell them apart.
    expect(within(panel).getAllByText('Library Scan')).toHaveLength(1)
    expect(within(panel).getAllByText('Hashing')).toHaveLength(1)

    // And the row that survives is the newer run, not whichever the list happened to reach first.
    const scan = within(panel).getByText('Library Scan').closest('article')!
    expect(within(scan).getByText('7 files found')).toBeInTheDocument()

    // A settled lane with nothing outstanding used to read `0/0`, which is indistinguishable from
    // a lane that never ran. It now says which of the two it is.
    const hashing = within(panel).getByText('Hashing').closest('article')!
    expect(within(hashing).getByText('nothing to do')).toBeInTheDocument()

    // `Completed · Settled` said the same thing twice, so a settled run drops the phase.
    expect(within(panel).queryByText(/Settled/)).not.toBeInTheDocument()

    // Which run this is has to be answerable, so each lane carries when it last did something.
    const times = panel.querySelectorAll('time')
    expect(times).toHaveLength(2)
    expect(times[0]).toHaveAttribute('datetime', '2026-08-29T12:00:04Z')
  })

  it('says what a lane is doing in words, whichever kind of lane it is', async () => {
    signedInAs('Administrator', (input) => {
      if (input === '/api/admin/background-work/') {
        return json({
          work: [
            // A Library Scan discovers its own scope, so it reports what it has found.
            run({
              id: 'scanning',
              category: 'LibraryScan',
              state: 'Running',
              phase: 'Traversing directories',
              discoveredCandidateCount: 1,
              finishedAt: null,
            }),
            // A derived lane knows its denominator, so while it runs a ratio is honest.
            run({
              id: 'inspecting',
              category: 'TechnicalInspection',
              state: 'Running',
              phase: 'Inspecting candidates',
              discoveredCandidateCount: 12,
              completedItemCount: 4,
              completedPercent: 33,
              finishedAt: null,
            }),
            // A queued lane has no counts worth printing; its state already says it is waiting.
            run({ id: 'queued', category: 'Hashing', state: 'Queued', phase: 'Waiting to start' }),
            // A run that stopped short keeps both numbers, so the shortfall stays visible.
            run({
              id: 'cancelled',
              category: 'Identification',
              state: 'Cancelled',
              discoveredCandidateCount: 12,
              completedItemCount: 4,
            }),
            run({
              id: 'done',
              category: 'PreviewGeneration',
              discoveredCandidateCount: 12,
              completedItemCount: 12,
            }),
          ],
          issues: [],
          resolvedIssues: [],
          operationalAttention: false,
          operationalAttentionCount: 0,
          paused: false,
        })
      }
      if (input === '/api/admin/configuration/') {
        return json({ status: 'Configured', libraryDirectories: [] })
      }
      return undefined
    })

    renderApp('/admin/work')

    const lanes = await screen.findByRole('heading', { name: 'Lanes' })
    const panel = lanes.closest('section')!

    expect(await within(panel).findByText('1 file found so far')).toBeInTheDocument()
    expect(within(panel).getByText('4 of 12 files')).toBeInTheDocument()
    expect(within(panel).getByText('4 of 12 files done')).toBeInTheDocument()
    expect(within(panel).getByText('12 files done')).toBeInTheDocument()

    const queued = within(panel).getByText('Hashing').closest('article')!
    expect(within(queued).getByText(/Waiting to start/)).toBeInTheDocument()
    expect(within(queued).queryByText(/file/)).not.toBeInTheDocument()
  })

  it('says when a Library Directory is read again without anyone asking', async () => {
    signedInAs('Administrator', (input) => {
      if (input === '/api/admin/background-work/') {
        return json({
          work: [],
          issues: [],
          resolvedIssues: [],
          operationalAttention: false,
          operationalAttentionCount: 0,
          paused: false,
        })
      }
      if (input === '/api/admin/configuration/') {
        return json({
          status: 'Configured',
          libraryDirectories: [
            { id: 'first', name: 'Fab', nextScanDueAt: inHours(4) },
            { id: 'second', name: 'Ordeno', nextScanDueAt: inHours(-1) },
          ],
        })
      }
      return undefined
    })

    const { container } = renderApp('/admin/work')

    // A row of Scan buttons on its own reads as the only way a new file is ever found, which is
    // the question this line exists to answer.
    await waitFor(() => expect(container.querySelector('.scan-schedule')).not.toBeNull())
    const schedule = container.querySelector('.scan-schedule')!
    expect(schedule.textContent).toContain('Fab in 4 hours')

    // A due time that has passed is not "an hour ago": that Scan has not run yet.
    expect(schedule.textContent).toContain('Ordeno now')
  })
})

/// A moment relative to the test's own clock, so the words the screen chooses for it are stable
/// without freezing time under React Query.
function inHours(hours: number) {
  return new Date(Date.now() + hours * 3_600_000).toISOString()
}

function run(overrides: Record<string, unknown> = {}) {
  return {
    id: '01a0492b-7f27-7215-b3f1-01fae02144f9',
    category: 'LibraryScan',
    state: 'Completed',
    trigger: 'Administrator',
    phase: 'Settled',
    libraryDirectoryId: '01a0492b-782a-7ba4-a1d1-2d08cc98f453',
    libraryDirectoryName: 'Fab',
    discoveredCandidateCount: 0,
    completedItemCount: 0,
    issueCount: 0,
    completedPercent: null,
    waitingReason: null,
    nextAttemptAt: null,
    cancellationRequested: false,
    cancellable: false,
    requestedAt: '2026-08-29T12:00:00Z',
    startedAt: '2026-08-29T12:00:01Z',
    lastActivityAt: '2026-08-29T12:00:02Z',
    finishedAt: '2026-08-29T12:00:03Z',
    ...overrides,
  }
}
