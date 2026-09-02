import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { MemoryRouter } from 'react-router'

import type { PlaybackVariant } from '../api/client'
import { App } from '../App'

/// What the tests agree a Video, an Account and a library answer look like.
///
/// Every screen is a route, so a test says which one it opens and the fixtures say what the API
/// answers. Both belong here rather than in one suite, because the shell, the Library and one
/// Video are now separate screens asking the same questions.

export function renderApp(route = '/') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[route]}>
        <App />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

export function libraryPage(videos: unknown[], overrides: Record<string, unknown> = {}) {
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

export function isFacetRequest(input: unknown) {
  return typeof input === 'string' && input.startsWith('/api/library/facets')
}

/// What the facets endpoint answers when a test is not about facets.
export function noFacets(overrides: Record<string, unknown> = {}) {
  return { sites: [], actors: [], quality: [], ...overrides }
}

export function isLibraryRequest(input: unknown) {
  return typeof input === 'string' && input.startsWith('/api/library/videos?')
}

/// One Video answered on its own, which is what the Video's own page asks for.
export function isVideoRequest(input: unknown) {
  return typeof input === 'string' && /^\/api\/library\/videos\/[^?]+$/.test(input)
}

export function videoDetail(video: unknown, supersededVideoId: string | null = null) {
  return { video, supersededVideoId }
}

export function json(body: unknown) {
  return Promise.resolve(new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  }))
}

export function variant(overrides: Record<string, unknown> = {}): PlaybackVariant {
  return {
    videoFileId: '01994dd4-2a0a-7000-8000-000000000011',
    deliveryUrl: '/media/videos/01994dd4-2a0a-7000-8000-000000000012',
    containerFormat: 'matroska,webm',
    videoCodec: 'vp8',
    audioCodec: 'vorbis',
    width: 1920,
    height: 1080,
    frameRate: 25,
    bitrate: 2000000,
    audioChannels: 2,
    audioSampleRate: 48000,
    audioBitrate: 128000,
    size: 10,
    durationMilliseconds: 10000,
    qualityBand: 'FullHd1080',
    directPlayClassification: 'BaselineCandidate',
    profileKey: 'video/webm|vp8|profile-unknown|level-unknown|8bit|fullhd|standard|vorbis|2ch',
    preciseVideoContentType: null,
    preciseAudioContentType: null,
    basicContentType: 'video/webm; codecs="vp8, vorbis"',
    assessment: null,
    smooth: null,
    powerEfficient: null,
    outcome: null,
    readyForDirectPlay: true,
    selectionReason: 'BaselineCandidate',
    ...overrides,
  } as PlaybackVariant
}

export function personalState(overrides: Record<string, unknown> = {}) {
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

export function claim(overrides: Record<string, unknown> = {}) {
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

export function identification(overrides: Record<string, unknown> = {}) {
  return {
    work: claim(),
    site: claim({ dimension: 'SiteRecognition' }),
    actors: [],
    metadataFetchedAt: null,
    ...overrides,
  }
}

export function libraryVideo(overrides: Record<string, unknown> = {}) {
  return {
    id: '01994dd4-2a0a-7000-8000-000000000010',
    displayTitle: 'Sample Video',
    discoveryDate: '2026-08-27T12:00:00Z',
    availability: 'Available',
    previewUrl: null,
    identification: identification(),
    playability: 'ReadyForDirectPlay',
    isUnsupportedVideo: false,
    personalState: personalState(),
    videoFiles: [variant()],
    ...overrides,
  }
}

export async function waitForVideo(container: HTMLElement) {
  await vi.waitFor(() => expect(container.querySelector('video')).not.toBeNull())
  return container.querySelector('video')!
}

/// The answers every screen needs before it can render, with room for the one a test is about.
export function signedInAs(
  authority: 'Administrator' | 'User',
  answer: (input: unknown) => Promise<Response> | undefined = () => undefined,
) {
  vi.spyOn(globalThis, 'fetch').mockImplementation((input) => {
    const specific = answer(input)
    if (specific) return specific

    if (input === '/api/access/state') return json({ claimed: true, signedIn: true })
    if (input === '/api/access/me') {
      return json({
        id: '01994dd4-2a0a-7000-8000-000000000001',
        username: 'viewer',
        email: null,
        authority,
        csrfToken: 'csrf-token',
      })
    }
    if (input === '/api/personal/playback-profiles') return json([])
    if (input === '/api/admin/background-work/') return json({ work: [], issues: [] })
    if (input === '/api/admin/identification/queue') return json([])
    if (isFacetRequest(input)) return json(noFacets())
    if (isVideoRequest(input)) return json(videoDetail(libraryVideo()))
    if (isLibraryRequest(input)) return json(libraryPage([libraryVideo()]))
    return json([])
  })
}
