import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen } from '@testing-library/react'

import { App } from './App'

function renderApp() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={queryClient}><App /></QueryClientProvider>)
}

function libraryPage(videos: unknown[], overrides: Record<string, unknown> = {}) {
  return {
    videos,
    totalMatches: videos.length,
    hiddenNotReadyForDirectPlay: 0,
    hiddenUnavailable: 0,
    hasMore: false,
    includesNotReadyForDirectPlay: false,
    ...overrides,
  }
}

function isFacetRequest(input: unknown) {
  return input === '/api/library/facets'
}

function isLibraryRequest(input: unknown) {
  return typeof input === 'string' && input.startsWith('/api/library/videos')
}

function json(body: unknown) {
  return Promise.resolve(new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  }))
}

describe('App', () => {
  afterEach(() => vi.restoreAllMocks())

  it('guides an unclaimed installation to create its first Administrator', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation((input) => {
      if (input === '/api/access/state') return json({ claimed: false, signedIn: false })
      if (input === '/api/access/bootstrap') {
        return json({
          verdict: 'Created',
          account: {
            id: '01994dd4-2a0a-7000-8000-000000000001',
            username: 'administrator',
            email: null,
            authority: 'Administrator',
            csrfToken: 'csrf-token',
          },
        })
      }
      if (input === '/api/admin/configuration/') {
        return json({
          status: 'ConfigurationRequired',
          prdbConnectionStatus: 'Missing',
          hasPrdbCredential: false,
          credentialReplacementPending: false,
          lastConnectionAttemptAt: null,
          lastConnectionVerifiedAt: null,
          lastConnectionIssue: null,
          libraryMountRoot: '/libraries',
          libraryDirectories: [],
        })
      }
      if (input === '/api/admin/configuration/library-directory-candidates') {
        return json({ containerPaths: [] })
      }
      if (input === '/api/admin/background-work/') {
        return json({ work: [], issues: [] })
      }
      if (input === '/api/personal/library') {
        return json({ continueWatching: [], favourites: [], watchLater: [] })
      }
      if (input === '/api/personal/videos/01994dd4-2a0a-7000-8000-000000000010/playback-attempts') {
        return json({
          verdict: 'Started',
          playbackAttemptId: '01994dd4-2a0a-7000-8000-000000000013',
          resumePositionMilliseconds: null,
        })
      }
      if (input === '/api/personal/playback-attempts/01994dd4-2a0a-7000-8000-000000000013/reports') {
        return json({ verdict: 'Accepted', personalState: personalState({ playState: 'InProgress' }) })
      }
      if (input === '/api/personal/playback-attempts/01994dd4-2a0a-7000-8000-000000000013/end') {
        return json({ ended: true })
      }
      if (isFacetRequest(input)) return json({ sites: [], actors: [] })
      if (isFacetRequest(input)) return json({ sites: [], actors: [] })
      if (isLibraryRequest(input)) {
        return json(libraryPage([{
          id: '01994dd4-2a0a-7000-8000-000000000010',
          displayTitle: 'Sample Video',
          discoveryDate: '2026-08-27T12:00:00Z',
          availability: 'Available',
          personalState: personalState(),
          videoFiles: [{
            id: '01994dd4-2a0a-7000-8000-000000000011',
            relativePath: 'sample.mp4',
            size: 10,
            durationMilliseconds: 10000,
            containerFormat: 'mp4',
            videoCodec: 'h264',
            audioCodec: 'aac',
            width: 640,
            height: 360,
            availability: 'Available',
            directPlayClassification: 'BaselineCandidate',
            deliveryUrl: '/media/videos/01994dd4-2a0a-7000-8000-000000000012',
          }],
        }]))
      }
      return json([])
    })

    const rendered = renderApp()
    expect(await screen.findByRole('heading', { name: 'Claim this installation' })).toBeInTheDocument()

    fireEvent.change(screen.getByLabelText('One-time authorization'), { target: { value: 'authorization' } })
    fireEvent.change(screen.getByLabelText('Administrator username'), { target: { value: 'administrator' } })
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'administrator password' } })
    fireEvent.click(screen.getByRole('button', { name: 'Create Administrator' }))

    expect(await screen.findByRole('heading', { name: 'Your collection starts here' })).toBeInTheDocument()
    expect(await screen.findByRole('heading', { name: 'Configuration' })).toBeInTheDocument()
    fireEvent.click(await screen.findByRole('button', { name: 'Play' }))
    expect(await screen.findByText('Your browser cannot play this Video File.')).toBeInTheDocument()
    expect(rendered.container.querySelector('video')).toHaveAttribute(
      'src',
      '/media/videos/01994dd4-2a0a-7000-8000-000000000012',
    )
    fireEvent.click(screen.getByRole('button', { name: 'Close' }))
    await vi.waitFor(() => expect(rendered.container.querySelector('video')).toBeNull())
    expect(globalThis.fetch).toHaveBeenCalledWith(
      '/api/access/bootstrap',
      expect.objectContaining({ method: 'POST' }),
    )
  })

  it('reports confirmed playback and exposes private organisation actions', async () => {
    const video = {
      id: '01994dd4-2a0a-7000-8000-000000000010',
      displayTitle: 'Resume Me',
      discoveryDate: '2026-08-27T12:00:00Z',
      availability: 'Available',
      personalState: personalState({
        playbackProgressMilliseconds: 20_000,
        playState: 'InProgress',
        continueWatching: true,
      }),
      videoFiles: [{
        id: '01994dd4-2a0a-7000-8000-000000000011',
        relativePath: 'resume.mp4',
        size: 10,
        durationMilliseconds: 100_000,
        containerFormat: 'mp4',
        videoCodec: 'h264',
        audioCodec: 'aac',
        width: 640,
        height: 360,
        availability: 'Available',
        directPlayClassification: 'BaselineCandidate',
        deliveryUrl: '/media/videos/resume',
      }],
    }
    vi.spyOn(globalThis, 'fetch').mockImplementation((input) => {
      if (input === '/api/access/state') return json({ claimed: true, signedIn: true })
      if (input === '/api/access/me') return json({
        id: '01994dd4-2a0a-7000-8000-000000000001',
        username: 'viewer',
        email: null,
        authority: 'User',
        csrfToken: 'csrf-token',
      })
      if (isFacetRequest(input)) return json({ sites: [], actors: [] })
      if (isLibraryRequest(input)) return json(libraryPage([video]))
      if (input === '/api/personal/library') {
        return json({ continueWatching: [video], favourites: [], watchLater: [] })
      }
      if (input === '/api/personal/videos/01994dd4-2a0a-7000-8000-000000000010/playback-attempts') {
        return json({
          verdict: 'Started',
          playbackAttemptId: '01994dd4-2a0a-7000-8000-000000000013',
          resumePositionMilliseconds: 20_000,
        })
      }
      if (input === '/api/personal/playback-attempts/01994dd4-2a0a-7000-8000-000000000013/reports') {
        return json({ verdict: 'Accepted', personalState: video.personalState })
      }
      if (input === '/api/personal/playback-attempts/01994dd4-2a0a-7000-8000-000000000013/end') {
        return json({ ended: true })
      }
      if (input === '/api/personal/videos/01994dd4-2a0a-7000-8000-000000000010/favourite') {
        return json({ verdict: 'Updated', personalState: personalState({ favourite: true }) })
      }
      return json([])
    })
    const clock = vi.spyOn(performance, 'now').mockReturnValue(0)

    const rendered = renderApp()
    expect(await screen.findByRole('region', { name: 'Continue Watching' })).toBeInTheDocument()
    fireEvent.click(screen.getAllByRole('button', { name: 'Favourite' })[0])
    await vi.waitFor(() => expect(globalThis.fetch).toHaveBeenCalledWith(
      '/api/personal/videos/01994dd4-2a0a-7000-8000-000000000010/favourite',
      expect.objectContaining({ method: 'PUT', headers: { 'X-CSRF-Token': 'csrf-token' } }),
    ))

    fireEvent.click(screen.getAllByRole('button', { name: 'Resume' })[0])
    const player = await waitForVideo(rendered.container)
    Object.defineProperty(player, 'paused', { configurable: true, value: false })
    player.currentTime = 20
    fireEvent.playing(player)
    clock.mockReturnValue(6_000)
    player.currentTime = 26
    fireEvent.timeUpdate(player)

    await vi.waitFor(() => expect(globalThis.fetch).toHaveBeenCalledWith(
      '/api/personal/playback-attempts/01994dd4-2a0a-7000-8000-000000000013/reports',
      expect.objectContaining({
        method: 'POST',
        headers: expect.objectContaining({ 'X-CSRF-Token': 'csrf-token' }),
      }),
    ))
    const reportCall = vi.mocked(globalThis.fetch).mock.calls.find(([path]) =>
      path === '/api/personal/playback-attempts/01994dd4-2a0a-7000-8000-000000000013/reports')
    expect(JSON.parse(reportCall![1]!.body!.toString())).toEqual(expect.objectContaining({
      positionMilliseconds: 26_000,
      activeWatchingMilliseconds: 6_000,
    }))
    fireEvent.click(screen.getByRole('button', { name: 'Close' }))
    await vi.waitFor(() => expect(rendered.container.querySelector('video')).toBeNull())
  })

  it('shows preview art and provenance without letting a candidate look established', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation((input) => {
      if (input === '/api/access/state') return json({ claimed: true, signedIn: true })
      if (input === '/api/access/me') return json({
        id: '01994dd4-2a0a-7000-8000-000000000001',
        username: 'viewer',
        email: null,
        authority: 'User',
        csrfToken: 'csrf-token',
      })
      if (isFacetRequest(input)) return json({ sites: [], actors: [] })
      if (isFacetRequest(input)) return json({ sites: [], actors: [] })
      if (isLibraryRequest(input)) {
        return json(libraryPage([
          libraryVideo({
            id: '01994dd4-2a0a-7000-8000-000000000020',
            displayTitle: 'A Known Work',
            previewUrl: '/media/previews/01994dd4-2a0a-7000-8000-000000000021',
            identification: identification({
              work: claim({ resolution: 'Established', targetTitle: 'A Known Work' }),
              site: claim({ resolution: 'Established', targetTitle: 'Known Site' }),
              actors: ['Alex Doe'],
            }),
          }),
          libraryVideo({
            id: '01994dd4-2a0a-7000-8000-000000000030',
            displayTitle: 'unknown-file',
            identification: identification({
              work: claim({ reviewStatus: 'ReviewNeeded' }),
            }),
          }),
        ]))
      }
      if (input === '/api/personal/library') {
        return json({ continueWatching: [], favourites: [], watchLater: [] })
      }
      return json([])
    })

    const rendered = renderApp()

    expect(await screen.findByText('A Known Work')).toBeInTheDocument()
    expect(rendered.container.querySelector('img.video-preview')).toHaveAttribute(
      'src',
      '/media/previews/01994dd4-2a0a-7000-8000-000000000021',
    )
    expect(screen.getByText('prdb match')).toBeInTheDocument()
    expect(screen.getByText('Known Site · from prdb')).toBeInTheDocument()
    expect(screen.getByText('Alex Doe')).toBeInTheDocument()
    expect(screen.getByText('Unknown Video')).toBeInTheDocument()
    expect(screen.getByText('Review needed')).toBeInTheDocument()
    expect(screen.queryByText('A Guessed Work')).not.toBeInTheDocument()
  })

  it('lets an Administrator preview and confirm an identification decision', async () => {
    const queueItem = {
      videoId: '01994dd4-2a0a-7000-8000-000000000030',
      caseVersion: 3,
      displayLabel: 'unknown-file',
      previewUrl: null,
      dimension: 'WorkIdentification',
      currentResolution: 'Unknown',
      currentTargetTitle: null,
      candidate: {
        id: '01994dd4-2a0a-7000-8000-000000000031',
        dimension: 'WorkIdentification',
        status: 'Pending',
        targetTitle: 'A Guessed Work',
        targetUrl: null,
        evidenceClass: 'Suggestive',
        reason: 'SuggestiveEvidence',
        evidenceSummary: 'Suggestive evidence, matched by Filename',
        supportingVideoFileId: '01994dd4-2a0a-7000-8000-000000000032',
        createdAt: '2026-08-28T10:00:00Z',
        resolvedAt: null,
      },
      affectedVideoFileCount: 1,
      reason: 'The evidence is only suggestive.',
    }
    const openCase = {
      videoId: queueItem.videoId,
      caseVersion: 3,
      displayLabel: 'unknown-file',
      previewUrl: null,
      identification: identification({ work: claim({ reviewStatus: 'ReviewNeeded' }) }),
      openCandidates: [queueItem.candidate],
      candidateHistory: [],
      videoFiles: [{
        id: '01994dd4-2a0a-7000-8000-000000000032',
        relativePath: 'unknown-file.mp4',
        availability: 'Available',
        directPlayClassification: 'BaselineCandidate',
        containerFormat: 'mp4',
        videoCodec: 'h264',
        audioCodec: 'aac',
        durationMilliseconds: 10_000,
        osHashSummary: 'abc123…',
        perceptualHashSummary: 'def456…',
        hashState: 'Computed',
      }],
      decisions: [],
      unavailableSiteActions: [],
      explanation: 'The evidence is only suggestive, so it can propose a candidate.',
    }
    const decisions: string[] = []
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      if (input === '/api/access/state') return json({ claimed: true, signedIn: true })
      if (input === '/api/access/me') return json({
        id: '01994dd4-2a0a-7000-8000-000000000001',
        username: 'administrator',
        email: null,
        authority: 'Administrator',
        csrfToken: 'csrf-token',
      })
      if (isFacetRequest(input)) return json({ sites: [], actors: [] })
      if (isLibraryRequest(input)) return json(libraryPage([]))
      if (input === '/api/personal/library') {
        return json({ continueWatching: [], favourites: [], watchLater: [] })
      }
      if (input === '/api/admin/identification/queue') {
        return json(decisions.length > 0 ? [] : [queueItem])
      }
      if (input === `/api/admin/identification/videos/${queueItem.videoId}`) return json(openCase)
      if (input === `/api/admin/identification/videos/${queueItem.videoId}/decisions`) {
        const body = JSON.parse(init!.body!.toString())
        if (!body.confirm) {
          return json({
            verdict: 'Preview',
            consequence: {
              claimTransition: 'Work Identification: Unknown becomes Established "A Guessed Work" as an Administrative Override.',
              candidateTransition: 'The open candidate becomes Superseded.',
              affectedVideoFileCount: 1,
              resultingReviewStatus: 'Clear',
              mergesAnotherVideo: false,
              mergeSummary: null,
              requiresNote: false,
            },
            case: openCase,
          })
        }
        decisions.push(body.action)
        return json({ verdict: 'Applied', consequence: null, case: openCase })
      }
      if (input === '/api/admin/configuration/') {
        return json({
          status: 'Configured',
          prdbConnectionStatus: 'Verified',
          hasPrdbCredential: true,
          credentialReplacementPending: false,
          lastConnectionAttemptAt: null,
          lastConnectionVerifiedAt: null,
          lastConnectionIssue: null,
          libraryMountRoot: '/libraries',
          libraryDirectories: [],
        })
      }
      if (input === '/api/admin/configuration/library-directory-candidates') {
        return json({ containerPaths: [] })
      }
      if (input === '/api/admin/background-work/') return json({ work: [], issues: [] })
      return json([])
    })

    renderApp()

    fireEvent.click(await screen.findByRole('button', { name: 'Review' }))
    expect(await screen.findByText('The evidence is only suggestive, so it can propose a candidate.'))
      .toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Accept candidate' }))

    const preview = await screen.findByRole('group', { name: 'Consequence preview' })
    expect(preview).toHaveTextContent('Administrative Override')
    expect(decisions).toHaveLength(0)

    fireEvent.click(screen.getByRole('button', { name: /^Confirm/ }))
    await vi.waitFor(() => expect(decisions).toEqual(['AcceptCandidate']))
    expect(await screen.findByText('Accept Candidate applied.')).toBeInTheDocument()
  })

  it('offers sign-in and an approval-gated registration request', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation((input) => {
      if (input === '/api/access/state') return json({ claimed: true, signedIn: false })
      return json({ verdict: 'Submitted' })
    })

    renderApp()
    fireEvent.click(await screen.findByRole('button', { name: 'Request access' }))
    fireEvent.change(screen.getByLabelText('Username'), { target: { value: 'viewer' } })
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'a secure password' } })
    fireEvent.click(screen.getByRole('button', { name: 'Submit request' }))

    expect(await screen.findByRole('status')).toHaveTextContent('Access begins only after approval.')
  })

  it('searches, narrows by facet, and offers the matches the rules keep out', async () => {
    const shown = libraryVideo({ displayTitle: 'A Known Work' })
    vi.spyOn(globalThis, 'fetch').mockImplementation((input) => {
      if (input === '/api/access/state') return json({ claimed: true, signedIn: true })
      if (input === '/api/access/me') return json({
        id: '01994dd4-2a0a-7000-8000-000000000001',
        username: 'viewer',
        email: null,
        authority: 'User',
        csrfToken: 'csrf-token',
      })
      if (isFacetRequest(input)) {
        return json({ sites: [{ value: 'Known Site', count: 3 }], actors: [] })
      }
      if (isLibraryRequest(input)) {
        return json(libraryPage([shown], {
          totalMatches: 1,
          hiddenNotReadyForDirectPlay: 2,
          hiddenUnavailable: 1,
        }))
      }
      if (input === '/api/library/preferences/include-not-ready') {
        return json({ includesNotReadyForDirectPlay: true })
      }
      if (input === '/api/personal/library') {
        return json({ continueWatching: [], favourites: [], watchLater: [] })
      }
      return json([])
    })

    renderApp()
    expect(await screen.findByText('A Known Work')).toBeInTheDocument()

    // What the current rules keep out is reported rather than silently dropped.
    expect(screen.getByText(/2 matches not ready for direct play/)).toBeInTheDocument()
    expect(screen.getByText(/1 match currently unavailable/)).toBeInTheDocument()

    fireEvent.change(screen.getByLabelText('Search'), { target: { value: 'known' } })
    await vi.waitFor(() => expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('query=known'),
      expect.anything(),
    ))

    fireEvent.change(screen.getByLabelText('Sort'), { target: { value: 'TitleAscending' } })
    await vi.waitFor(() => expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('sort=TitleAscending'),
      expect.anything(),
    ))

    fireEvent.click(screen.getByRole('button', { name: 'Known Site (3)' }))
    await vi.waitFor(() => expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('sites=Known+Site'),
      expect.anything(),
    ))

    // The control that reveals the hidden matches sets the Account's own preference.
    fireEvent.click(screen.getByRole('button', { name: 'Include them' }))
    await vi.waitFor(() => expect(globalThis.fetch).toHaveBeenCalledWith(
      '/api/library/preferences/include-not-ready',
      expect.objectContaining({ method: 'PUT' }),
    ))
  })

  it('shows unsupported Videos with their title, preview, and the reason playback is unavailable', async () => {
    const unsupported = libraryVideo({
      displayTitle: 'An Unsupported Video',
      previewUrl: '/media/previews/01994dd4-2a0a-7000-8000-000000000014',
      identification: identification({
        site: claim({
          dimension: 'SiteRecognition',
          resolution: 'Established',
          targetTitle: 'Known Site',
          source: 'LocalInference',
        }),
      }),
      videoFiles: [{
        id: '01994dd4-2a0a-7000-8000-000000000011',
        relativePath: 'known site - scene.mkv',
        size: 10,
        durationMilliseconds: 10000,
        containerFormat: 'matroska',
        videoCodec: 'wmv3',
        audioCodec: 'wmav2',
        width: 640,
        height: 360,
        availability: 'Available',
        directPlayClassification: 'Unsupported',
        deliveryUrl: '/media/videos/01994dd4-2a0a-7000-8000-000000000012',
      }],
    })
    vi.spyOn(globalThis, 'fetch').mockImplementation((input) => {
      if (input === '/api/access/state') return json({ claimed: true, signedIn: true })
      if (input === '/api/access/me') return json({
        id: '01994dd4-2a0a-7000-8000-000000000001',
        username: 'viewer',
        email: null,
        authority: 'User',
        csrfToken: 'csrf-token',
      })
      if (isFacetRequest(input)) return json({ sites: [], actors: [] })
      if (isLibraryRequest(input)) {
        return json(libraryPage([unsupported], { includesNotReadyForDirectPlay: true }))
      }
      if (input === '/api/library/preferences/include-not-ready') {
        return json({ includesNotReadyForDirectPlay: false })
      }
      if (input === '/api/personal/library') {
        return json({ continueWatching: [], favourites: [], watchLater: [] })
      }
      return json([])
    })

    renderApp()

    // Title and preview are there; the entry explains itself instead of offering playback.
    expect(await screen.findByText('An Unsupported Video')).toBeInTheDocument()
    expect(document.querySelector('img.video-preview')).toHaveAttribute(
      'src',
      '/media/previews/01994dd4-2a0a-7000-8000-000000000014',
    )
    expect(screen.getByText(/matroska \(wmv3 \+ wmav2\)/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Play' })).not.toBeInTheDocument()

    // A locally recognised Site says so rather than looking like a prdb match.
    expect(screen.getByText('Known Site · recognised locally')).toBeInTheDocument()

    // The standing preference is visible and can be turned off again.
    const preference = screen.getByLabelText('Show unsupported Videos in ordinary results')
    expect(preference).toBeChecked()
    fireEvent.click(preference)
    await vi.waitFor(() => expect(globalThis.fetch).toHaveBeenCalledWith(
      '/api/library/preferences/include-not-ready',
      expect.objectContaining({ method: 'PUT', body: JSON.stringify({ included: false }) }),
    ))

    // The explicit filter narrows one view without changing the preference.
    fireEvent.click(screen.getByRole('button', { name: 'Unsupported only' }))
    await vi.waitFor(() => expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('readiness=NotDirectlyPlayable'),
      expect.anything(),
    ))
  })

  it('shows operational attention, pauses work, and rechecks a blocked issue', async () => {
    const issue = {
      id: '01994dd4-2a0a-7000-8000-000000000030',
      reference: 'WI-A1B2C3D4E5F6',
      backgroundWorkId: '01994dd4-2a0a-7000-8000-000000000031',
      category: 'LibraryScan',
      libraryDirectoryId: '01994dd4-2a0a-7000-8000-000000000032',
      severity: 'OperationalBlocker',
      cause: 'SourceAccess',
      remediationOwner: 'InstallationOperator',
      retryDisposition: 'RetriesExhausted',
      phase: 'Traversing directories',
      summary: 'Library directory “Films” cannot be scanned',
      detail: 'The Library Scan could not observe this part of the directory.',
      affectedScope: 'Films',
      containerPath: '/library/films',
      impact: 'Nothing in this Library Directory can be discovered.',
      requiredAction: 'Ask the Installation Operator to restore the mount.',
      expectedResolutionEvidence: 'A scan that completes its traversal.',
      occurrenceCount: 3,
      affectedItemCount: 0,
      version: 4,
      actions: ['CheckAgain', 'OpenLibraryDirectory', 'CopyOperatorHandoff'],
      operatorHandoff: 'prdb-viewer operator handoff\nReference: WI-A1B2C3D4E5F6',
      videoId: null,
      videoFileId: null,
      nextAttemptAt: null,
      firstOccurredAt: '2026-08-28T09:00:00Z',
      lastOccurredAt: '2026-08-28T11:00:00Z',
      resolvedAt: null,
      resolutionEvidence: null,
    }
    vi.spyOn(globalThis, 'fetch').mockImplementation((input) => {
      if (input === '/api/access/state') return json({ claimed: true, signedIn: true })
      if (input === '/api/access/me') return json({
        id: '01994dd4-2a0a-7000-8000-000000000001',
        username: 'administrator',
        email: null,
        authority: 'Administrator',
        csrfToken: 'csrf-token',
      })
      if (input === '/api/admin/background-work/') {
        return json({
          work: [{
            id: '01994dd4-2a0a-7000-8000-000000000031',
            category: 'LibraryScan',
            state: 'Running',
            trigger: 'Administrator',
            phase: 'Traversing directories',
            libraryDirectoryId: '01994dd4-2a0a-7000-8000-000000000032',
            libraryDirectoryName: 'Films',
            discoveredCandidateCount: 12,
            completedItemCount: 4,
            issueCount: 1,
            completedPercent: null,
            waitingReason: null,
            nextAttemptAt: null,
            cancellationRequested: false,
            cancellable: true,
            requestedAt: '2026-08-28T09:00:00Z',
            startedAt: '2026-08-28T09:00:01Z',
            lastActivityAt: '2026-08-28T11:00:00Z',
            finishedAt: null,
          }],
          issues: [issue],
          recentlyResolvedIssues: [],
          operationalAttention: true,
          operationalAttentionCount: 1,
          paused: false,
          pausedAt: null,
        })
      }
      if (input === '/api/admin/background-work/pause') {
        return json({ paused: true, pausedAt: '2026-08-28T11:05:00Z' })
      }
      if (input === `/api/admin/background-work/issues/${issue.id}/actions`) {
        return json({ verdict: 'Accepted', issue })
      }
      if (input === '/api/admin/configuration/') {
        return json({
          status: 'Configured',
          prdbConnectionStatus: 'Verified',
          hasPrdbCredential: true,
          credentialReplacementPending: false,
          lastConnectionAttemptAt: null,
          lastConnectionVerifiedAt: null,
          lastConnectionIssue: null,
          libraryMountRoot: '/libraries',
          libraryDirectories: [],
        })
      }
      if (input === '/api/admin/configuration/library-directory-candidates') {
        return json({ containerPaths: [] })
      }
      if (isFacetRequest(input)) return json({ sites: [], actors: [] })
      if (isLibraryRequest(input)) return json(libraryPage([]))
      if (input === '/api/personal/library') {
        return json({ continueWatching: [], favourites: [], watchLater: [] })
      }
      return json([])
    })

    renderApp()

    expect(await screen.findByText('Operational attention')).toBeInTheDocument()
    expect(screen.getByText('1 issue block work until someone acts.')).toBeInTheDocument()
    expect(await screen.findByText('Library directory “Films” cannot be scanned')).toBeInTheDocument()
    expect(screen.getByText(/WI-A1B2C3D4E5F6/)).toBeInTheDocument()
    expect(screen.getByText(/\/library\/films/)).toBeInTheDocument()

    // A Library Scan reports its counts rather than an invented percentage.
    expect(screen.getByText('4/12')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Pause background work' }))
    await vi.waitFor(() => expect(globalThis.fetch).toHaveBeenCalledWith(
      '/api/admin/background-work/pause',
      expect.objectContaining({
        method: 'POST',
        headers: expect.objectContaining({ 'X-CSRF-Token': 'csrf-token' }),
      }),
    ))

    fireEvent.click(screen.getByRole('button', { name: 'Check again' }))
    await vi.waitFor(() => expect(globalThis.fetch).toHaveBeenCalledWith(
      `/api/admin/background-work/issues/${issue.id}/actions`,
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ action: 'CheckAgain', version: 4 }),
      }),
    ))
  })
})

function personalState(overrides: Record<string, unknown> = {}) {
  return {
    playbackProgressMilliseconds: null,
    accumulatedWatchDurationMilliseconds: 0,
    playCount: 0,
    hasViewingCompletion: false,
    playState: 'Unplayed',
    continueWatching: false,
    favourite: false,
    watchLater: false,
    personalRating: null,
    ...overrides,
  }
}

function claim(overrides: Record<string, unknown> = {}) {
  return {
    dimension: 'WorkIdentification',
    resolution: 'Unknown',
    reviewStatus: 'Clear',
    targetTitle: null,
    targetUrl: null,
    source: 'PrdbIdentification',
    evidenceClass: 'Conclusive',
    administrativeOverride: false,
    establishedAt: null,
    lastConfirmedAt: null,
    ...overrides,
  }
}

function identification(overrides: Record<string, unknown> = {}) {
  return {
    work: claim(),
    site: claim({ dimension: 'SiteRecognition' }),
    actors: [],
    metadataFetchedAt: null,
    ...overrides,
  }
}

function libraryVideo(overrides: Record<string, unknown> = {}) {
  return {
    id: '01994dd4-2a0a-7000-8000-000000000010',
    displayTitle: 'Sample Video',
    discoveryDate: '2026-08-27T12:00:00Z',
    availability: 'Available',
    previewUrl: null,
    identification: identification(),
    personalState: personalState(),
    videoFiles: [{
      id: '01994dd4-2a0a-7000-8000-000000000011',
      relativePath: 'sample.mp4',
      size: 10,
      durationMilliseconds: 10000,
      containerFormat: 'mp4',
      videoCodec: 'h264',
      audioCodec: 'aac',
      width: 640,
      height: 360,
      availability: 'Available',
      directPlayClassification: 'BaselineCandidate',
      deliveryUrl: '/media/videos/01994dd4-2a0a-7000-8000-000000000012',
    }],
    ...overrides,
  }
}

async function waitForVideo(container: HTMLElement) {
  await vi.waitFor(() => expect(container.querySelector('video')).not.toBeNull())
  return container.querySelector('video')!
}
