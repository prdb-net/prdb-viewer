import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen } from '@testing-library/react'

import { App } from './App'

function renderApp() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={queryClient}><App /></QueryClientProvider>)
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
      if (input === '/api/library/videos') {
        return json([{
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
        }])
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
      if (input === '/api/library/videos') return json([video])
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

async function waitForVideo(container: HTMLElement) {
  await vi.waitFor(() => expect(container.querySelector('video')).not.toBeNull())
  return container.querySelector('video')!
}
