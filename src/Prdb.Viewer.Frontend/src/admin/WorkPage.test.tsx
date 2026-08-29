import { screen, within } from '@testing-library/react'

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
    expect(within(scan).getByText('7/7')).toBeInTheDocument()

    // Which run this is has to be answerable, so each lane carries when it last did something.
    const times = panel.querySelectorAll('time')
    expect(times).toHaveLength(2)
    expect(times[0]).toHaveAttribute('datetime', '2026-08-29T12:00:04Z')
  })
})

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
