import { fireEvent, screen, within } from '@testing-library/react'

import { formatDay } from './lib/format'
import {
  claim,
  identification,
  isFacetRequest,
  isLibraryRequest,
  isVideoRequest,
  json,
  libraryPage,
  libraryVideo,
  noFacets,
  personalState,
  renderApp,
  signedInAs,
  variant,
  videoDetail,
  waitForVideo,
} from './test/fixtures'

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
      if (input === '/api/personal/playback-profiles') return json([])
      if (isFacetRequest(input)) return json(noFacets())
      if (input === '/api/personal/playback-profiles') return json([])
      if (isFacetRequest(input)) return json(noFacets())
      if (isLibraryRequest(input)) {
        return json(libraryPage([{
          id: '01994dd4-2a0a-7000-8000-000000000010',
          displayTitle: 'Sample Video',
          discoveryDate: '2026-08-27T12:00:00Z',
          availability: 'Available',
          playability: 'ReadyForDirectPlay',
          isUnsupportedVideo: false,
          personalState: personalState(),
          videoFiles: [variant()],
        }]))
      }
      return json([])
    })

    renderApp()
    expect(await screen.findByRole('heading', { name: 'Claim this installation' })).toBeInTheDocument()

    fireEvent.change(screen.getByLabelText('One-time authorization'), { target: { value: 'authorization' } })
    fireEvent.change(screen.getByLabelText('Administrator username'), { target: { value: 'administrator' } })
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'administrator password' } })
    fireEvent.click(screen.getByRole('button', { name: 'Create Administrator' }))

    // Claiming the installation lands in the library rather than on a page that carries every
    // administrative section beneath it.
    expect(await screen.findByRole('heading', { name: 'Browse' })).toBeInTheDocument()
    expect(screen.getByText('Sample Video')).toBeInTheDocument()

    // The installation is a destination, reached from the navigation the shell always shows,
    // and it is called there what the navigation calls it.
    fireEvent.click(screen.getByRole('link', { name: 'Installation' }))
    expect(await screen.findByRole('heading', { name: 'Installation' })).toBeInTheDocument()
    expect(screen.queryByText('Sample Video')).not.toBeInTheDocument()

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
      playability: 'ReadyForDirectPlay',
      isUnsupportedVideo: false,
      personalState: personalState({
        playbackProgressMilliseconds: 20_000,
        playState: 'InProgress',
        continueWatching: true,
      }),
      videoFiles: [variant()],
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
      if (input === '/api/personal/playback-profiles') return json([])
      if (isFacetRequest(input)) return json(noFacets())
      if (isLibraryRequest(input)) return json(libraryPage([video]))
      if (isVideoRequest(input)) return json(videoDetail(video))
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

    // Continue Watching is its own destination rather than a shelf stacked above the library.
    const rendered = renderApp('/continue')
    expect(await screen.findByRole('heading', { name: 'Continue Watching' })).toBeInTheDocument()
    fireEvent.click(screen.getAllByRole('button', { name: 'Favourite' })[0])
    await vi.waitFor(() => expect(globalThis.fetch).toHaveBeenCalledWith(
      '/api/personal/videos/01994dd4-2a0a-7000-8000-000000000010/favourite',
      expect.objectContaining({ method: 'PUT' }),
    ))

    // Resuming leads to the Video's own page, which is where playback lives, and starts there.
    fireEvent.click(screen.getAllByRole('link', { name: 'Resume' })[0])
    expect(await screen.findByRole('heading', { name: 'Resume Me' })).toBeInTheDocument()
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
      if (input === '/api/personal/playback-profiles') return json([])
      if (isFacetRequest(input)) return json(noFacets())
      if (input === '/api/personal/playback-profiles') return json([])
      if (isFacetRequest(input)) return json(noFacets())
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
      return json([])
    })

    const rendered = renderApp()

    expect(await screen.findByText('A Known Work')).toBeInTheDocument()
    expect(rendered.container.querySelector('img.video-preview')).toHaveAttribute(
      'src',
      '/media/previews/01994dd4-2a0a-7000-8000-000000000021',
    )
    // A prdb match is the ordinary case, so the card does not say so; the Site and the Actor are
    // what distinguish this Video and are said plainly.
    expect(screen.queryByText('prdb match')).not.toBeInTheDocument()
    expect(screen.getByText('Known Site')).toBeInTheDocument()
    expect(screen.getByText('Alex Doe')).toBeInTheDocument()
    expect(screen.getByText('Unknown Video')).toBeInTheDocument()
    expect(screen.getByText('Review needed')).toBeInTheDocument()
    expect(screen.queryByText('A Guessed Work')).not.toBeInTheDocument()
  })

  it('says what a Video is worth watching at, on the shelf and on its own page', async () => {
    const file = variant({
      containerFormat: 'mov,mp4',
      videoCodec: 'h264',
      audioCodec: 'aac',
      qualityBand: 'Uhd2160',
      width: 3840,
      height: 2160,
      frameRate: 59.94,
      bitrate: 24_000_000,
      audioChannels: 6,
      audioSampleRate: 48_000,
      size: 8_400_000_000,
      durationMilliseconds: 5_400_000,
    })
    const video = libraryVideo({ videoFiles: [file] })
    signedInAs('User', (input) => {
      if (isLibraryRequest(input)) return json(libraryPage([video]))
      if (isVideoRequest(input)) return json(videoDetail(video))
      return undefined
    })

    renderApp()

    // The shelf says it in the corner of the picture, where a person looking for it looks.
    expect(await screen.findByText('4K')).toBeInTheDocument()
    expect(screen.getByText('60 fps')).toBeInTheDocument()

    fireEvent.click(screen.getByText('Sample Video'))

    // The Video's own page says the rest of it, about the occurrence Play would reach for.
    expect(await screen.findByText('4K · 60 fps')).toBeInTheDocument()
    expect(screen.getByText('1 h 30 min')).toBeInTheDocument()
    expect(screen.getByText('24 Mbit/s')).toBeInTheDocument()
    expect(screen.getByText('aac · 5.1 · 48 kHz')).toBeInTheDocument()
    expect(screen.getByText('8.4 GB')).toBeInTheDocument()
    expect(screen.getByText('4K · 60 fps · mov,mp4 (h264 + aac)')).toBeInTheDocument()
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
      videoFiles: [variant()],
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
      if (input === '/api/personal/playback-profiles') return json([])
      if (isFacetRequest(input)) return json(noFacets())
      if (isLibraryRequest(input)) return json(libraryPage([]))
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

    renderApp('/admin/identification')

    fireEvent.click(await screen.findByRole('button', { name: 'Review' }))
    expect(await screen.findByText('The evidence is only suggestive, so it can propose a candidate.'))
      .toBeInTheDocument()

    // A target belongs to the decisions that read one, so the fields wait to be asked for and
    // naming a target is not a step every other decision appears to need.
    expect(screen.queryByLabelText('Target identifier')).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Assign directly' }))
    expect(screen.getByLabelText('Target identifier')).toBeInTheDocument()
    expect(decisions).toHaveLength(0)
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(screen.queryByLabelText('Target identifier')).not.toBeInTheDocument()

    // Nothing is established here yet, so the two decisions that change an established claim are
    // refused by the screen rather than by the request.
    expect(screen.getByRole('button', { name: 'Replace claim' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Revoke claim' })).toBeDisabled()

    fireEvent.click(screen.getByRole('button', { name: 'Accept candidate' }))

    const preview = await screen.findByRole('group', { name: 'Consequence preview' })
    expect(preview).toHaveTextContent('Administrative Override')
    expect(decisions).toHaveLength(0)

    fireEvent.click(screen.getByRole('button', { name: /^Confirm/ }))
    await vi.waitFor(() => expect(decisions).toEqual(['AcceptCandidate']))
    expect(await screen.findByText('Accept Candidate applied.')).toBeInTheDocument()
  })

  it('refuses the site decisions a case does not offer, and says why', async () => {
    const candidate = {
      id: '01994dd4-2a0a-7000-8000-000000000041',
      dimension: 'SiteRecognition',
      status: 'Pending',
      targetTitle: 'A Known Site',
      targetUrl: null,
      evidenceClass: 'Suggestive',
      reason: 'SuggestiveEvidence',
      source: 'LocalInference',
      evidenceSummary: 'Local: Suggestive evidence, matched by the file’s own path',
      supportingVideoFileId: '01994dd4-2a0a-7000-8000-000000000042',
      createdAt: '2026-09-02T10:00:00Z',
      resolvedAt: null,
    }
    const queueItem = {
      videoId: '01994dd4-2a0a-7000-8000-000000000040',
      caseVersion: 1,
      displayLabel: 'A Known Work',
      previewUrl: null,
      dimension: 'SiteRecognition',
      currentResolution: 'Established',
      currentTargetTitle: 'A Known Site',
      candidate,
      affectedVideoFileCount: 1,
      reason: 'The evidence is only suggestive.',
    }
    const openCase = {
      videoId: queueItem.videoId,
      caseVersion: 1,
      displayLabel: 'A Known Work',
      previewUrl: null,
      identification: identification({
        work: claim({ resolution: 'Established', targetTitle: 'A Known Work' }),
        site: claim({
          dimension: 'SiteRecognition',
          resolution: 'Established',
          reviewStatus: 'ReviewNeeded',
          targetTitle: 'A Known Site',
        }),
      }),
      openCandidates: [candidate],
      candidateHistory: [],
      videoFiles: [variant()],
      decisions: [],
      // A Site that came with the Work Identification is not separately editable, which the
      // screen used to find out by sending a decision the server then refused.
      unavailableSiteActions: ['AcceptCandidate', 'AssignDirectly', 'ReplaceClaim', 'RevokeClaim'],
      explanation: 'The evidence is only suggestive, so it can propose a candidate.',
    }
    signedInAs('Administrator', (input) => {
      if (input === '/api/admin/identification/queue') return json([queueItem])
      if (input === `/api/admin/identification/videos/${queueItem.videoId}`) return json(openCase)
      return undefined
    })

    renderApp('/admin/identification')

    // Local recognition reads a path whether or not prdb answered the same question first, so the
    // queue says when a proposal is one the library already knows.
    expect(await screen.findByText('Proposes what is already established here.')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Review' }))

    expect(await screen.findByRole('button', { name: 'Reject candidate' })).toBeEnabled()
    for (const label of ['Accept candidate', 'Assign directly', 'Replace claim', 'Revoke claim']) {
      expect(screen.getByRole('button', { name: label })).toBeDisabled()
    }
    expect(screen.getByText(/second site truth/)).toBeInTheDocument()
    expect(screen.queryByLabelText('Target identifier')).not.toBeInTheDocument()

    // Four refused decisions and one that is not are an answer, not a choice, so the case says
    // which decision is left, why the proposal adds nothing, and what the library keeps afterwards.
    expect(screen.getByText('“Reject candidate” is the only decision this case offers.'))
      .toBeInTheDocument()
    expect(screen.getByText(/proposes the Site Recognition that is already established/))
      .toBeInTheDocument()
    expect(screen.getByText(/leaves the Site Recognition established as “A Known Site”/))
      .toBeInTheDocument()
    expect(screen.getByText(/nothing else on this Video then waits for a decision/))
      .toBeInTheDocument()
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
        return json(noFacets({ sites: [{ value: 'Known Site', count: 3 }] }))
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
      return json([])
    })

    renderApp()
    expect(await screen.findByText('A Known Work')).toBeInTheDocument()

    // What the current rules keep out is reported rather than silently dropped.
    expect(screen.getByText(/2 matches not ready for direct play/)).toBeInTheDocument()
    expect(screen.getByText(/1 match currently unavailable/)).toBeInTheDocument()

    fireEvent.change(screen.getByLabelText('Search the library'), { target: { value: 'known' } })
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

  it('narrows and orders the Library by the quality it holds', async () => {
    signedInAs('User', (input) => {
      if (isFacetRequest(input)) {
        return json(noFacets({
          quality: [
            { value: 'Uhd2160', count: 3 },
            { value: 'FullHd1080', count: 12 },
            { value: 'Unknown', count: 1 },
          ],
        }))
      }
      return undefined
    })

    renderApp()

    // The bands are offered by name, best first, with what each one holds. The band inspection
    // never established is not a choice, so it is not offered as one.
    expect(await screen.findByRole('button', { name: '4K (3)' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '1080p (12)' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Unknown \(1\)/ })).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: '4K (3)' }))
    await vi.waitFor(() => expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('quality=Uhd2160'),
      expect.anything(),
    ))

    // Values inside one facet combine with OR, so the second band widens what the first chose.
    fireEvent.click(screen.getByRole('button', { name: '1080p (12)' }))
    await vi.waitFor(() => expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('quality=Uhd2160%2CFullHd1080'),
      expect.anything(),
    ))

    fireEvent.change(screen.getByLabelText('Sort'), { target: { value: 'QualityDescending' } })
    await vi.waitFor(() => expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('sort=QualityDescending'),
      expect.anything(),
    ))
  })

  it('names what is chosen, takes it out again, and keeps the long tail of a facet behind one control', async () => {
    signedInAs('User', (input) => {
      if (isFacetRequest(input)) {
        return json(noFacets({
          sites: Array.from({ length: 10 }, (_, index) => ({ value: `Site ${index + 1}`, count: 10 - index })),
        }))
      }
      return undefined
    })

    renderApp('/?sites=Site+9&query=known')

    // The facets are counted against the same narrowing the Videos are.
    await vi.waitFor(() => expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringMatching(/^\/api\/library\/facets\?.*query=known/),
      expect.anything(),
    ))

    // What is chosen is said in one row, each with the control that takes it out again.
    const chosen = await screen.findByRole('list', { name: 'Active filters' })
    expect(chosen).toHaveTextContent('“known”')
    expect(chosen).toHaveTextContent('Site 9')

    // The most populated eight are offered; the ninth is shown anyway because it is chosen, and
    // the tenth waits behind one control.
    expect(screen.getByRole('button', { name: 'Site 8 (3)' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Site 9 (2)' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.queryByRole('button', { name: 'Site 10 (1)' })).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Show all 10' }))
    expect(screen.getByRole('button', { name: 'Site 10 (1)' })).toBeInTheDocument()

    fireEvent.click(within(chosen).getByRole('button', { name: 'Remove Site 9' }))
    await vi.waitFor(() => expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringMatching(/^\/api\/library\/videos\?(?!.*sites=).*query=known/),
      expect.anything(),
    ))
  })

  it('looks for a facet value at the Host, and never claims to list what it was not sent', async () => {
    signedInAs('User', (input) => {
      if (isFacetRequest(input)) {
        // The Host answers the narrowing it was given: everything, or what the term admits. It
        // also says that it left values out, which is the case this is about.
        const found = typeof input === 'string' && input.includes('siteSearch=late')
        return json(noFacets({
          sites: found
            ? [{ value: 'Late Night', count: 2 }]
            : Array.from({ length: 10 }, (_, index) => ({
              value: `Site ${index + 1}`,
              count: 10 - index,
            })),
          moreSites: true,
        }))
      }
      return undefined
    })

    renderApp()

    // A facet the answer could not carry whole does not offer to show all of it: the control says
    // how many it has, and the field beside it is how the rest is reached.
    expect(await screen.findByRole('button', { name: 'Show 10 most common' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Show all 10' })).not.toBeInTheDocument()

    // Typing asks the Host rather than filtering what arrived, because `Late Night` is one of the
    // values that never did.
    fireEvent.change(screen.getByLabelText('Find a Site'), { target: { value: 'late' } })
    await vi.waitFor(() => expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringMatching(/^\/api\/library\/facets\?.*siteSearch=late/),
      expect.anything(),
    ))

    // What matches is shown whole rather than cut back to the preview, and the Actors beside it
    // are untouched: the term narrowed one facet's values, not the Library.
    expect(await screen.findByRole('button', { name: 'Late Night (2)' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Site 1 (10)' })).not.toBeInTheDocument()
    expect(globalThis.fetch).not.toHaveBeenCalledWith(
      expect.stringMatching(/^\/api\/library\/videos\?.*siteSearch/),
      expect.anything(),
    )
  })

  it('says on a card only what is exceptional, and counts what is on screen against the match', async () => {
    const named = libraryVideo({
      id: '01994dd4-2a0a-7000-8000-000000000040',
      displayTitle: 'Alex Doe And Sam Roe',
      discoveryDate: '2026-08-27T12:00:00Z',
      identification: identification({
        work: claim({ resolution: 'Established', targetTitle: 'Alex Doe And Sam Roe' }),
        site: claim({ resolution: 'Established', targetTitle: 'Known Site' }),
        actors: ['Sam Roe', 'Alex Doe'],
      }),
      personalState: personalState({ personalRating: 4 }),
    })
    const plain = libraryVideo({
      id: '01994dd4-2a0a-7000-8000-000000000041',
      displayTitle: 'A Known Work',
      identification: identification({
        work: claim({ resolution: 'Established', targetTitle: 'A Known Work' }),
        site: claim({ resolution: 'Established', targetTitle: 'Known Site' }),
        actors: ['Alex Doe'],
      }),
    })
    signedInAs('User', (input) => {
      if (isLibraryRequest(input)) {
        return json(libraryPage([named, plain], { totalMatches: 5, hasMore: true }))
      }
      return undefined
    })

    const rendered = renderApp()
    expect(await screen.findByText('Alex Doe And Sam Roe')).toBeInTheDocument()

    // The ordinary case is silent: no card says it is a prdb match or that it is expected to play.
    expect(screen.queryByText('prdb match')).not.toBeInTheDocument()
    expect(screen.queryByText(/expected to play/)).not.toBeInTheDocument()

    // A title that is the Actors' names is not followed by the Actors' names; the Site and the
    // Discovery Date take that line. The other card names its Actor.
    const cards = rendered.container.querySelectorAll('.video-card')
    expect(cards[0]).toHaveTextContent(formatDay('2026-08-27T12:00:00Z')!)
    expect(within(cards[0] as HTMLElement).queryByText('Sam Roe, Alex Doe')).not.toBeInTheDocument()
    expect(within(cards[1] as HTMLElement).getByText('Alex Doe')).toBeInTheDocument()

    // The runtime is on the picture, and a Personal Rating is shown only where there is one.
    expect(within(cards[0] as HTMLElement).getByText('10 s')).toBeInTheDocument()
    expect(within(cards[0] as HTMLElement).getByRole('radio', { name: '4 of 5' })).toBeChecked()
    expect(within(cards[1] as HTMLElement).queryByRole('radio')).not.toBeInTheDocument()

    // Favourite and Watch Later are still on every card, as controls on the picture.
    expect(within(cards[1] as HTMLElement).getByRole('button', { name: 'Favourite' })).toBeInTheDocument()

    // How much of the match is on screen, beside the control that reveals more of it.
    expect(screen.getByText('2 of 5 shown')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Show more' })).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('5 Videos')
  })

  it('qualifies this browser, then falls back to the next variant when one fails to decode', async () => {
    const first = variant({
      videoFileId: '01994dd4-2a0a-7000-8000-0000000000a1',
      deliveryUrl: '/media/videos/aaa',
      containerFormat: 'mov,mp4,m4a,3gp,3g2,mj2',
      videoCodec: 'h264',
      audioCodec: 'aac',
      directPlayClassification: 'ClientDependent',
      profileKey: 'mp4-high',
      preciseVideoContentType: 'video/mp4; codecs="avc1.640028"',
      preciseAudioContentType: 'audio/mp4; codecs="mp4a.40.2"',
      assessment: 'Positive',
      smooth: true,
      selectionReason: 'PositivelyAssessedAndSmooth',
    })
    const second = variant({
      videoFileId: '01994dd4-2a0a-7000-8000-0000000000a2',
      deliveryUrl: '/media/videos/bbb',
      selectionReason: 'BaselineCandidate',
    })
    const video = libraryVideo({
      displayTitle: 'Two Variants',
      videoFiles: [first, second],
    })
    const decodingInfo = vi.fn().mockResolvedValue({
      supported: true,
      smooth: true,
      powerEfficient: false,
    })
    Object.defineProperty(navigator, 'mediaCapabilities', {
      configurable: true,
      value: { decodingInfo },
    })
    const attempts: string[] = []
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      if (input === '/api/access/state') return json({ claimed: true, signedIn: true })
      if (input === '/api/access/me') return json({
        id: '01994dd4-2a0a-7000-8000-000000000001',
        username: 'viewer',
        email: null,
        authority: 'User',
        csrfToken: 'csrf-token',
      })
      if (input === '/api/personal/playback-profiles') {
        return json([{
          profileKey: 'mp4-high',
          videoContentType: 'video/mp4; codecs="avc1.640028"',
          audioContentType: 'audio/mp4; codecs="mp4a.40.2"',
          basicContentType: 'video/mp4',
          width: 1920,
          height: 1080,
          frameRate: 25,
          bitrate: 4000000,
          audioChannels: 2,
          audioSampleRate: 48000,
          audioBitrate: 128000,
        }])
      }
      if (input === '/api/personal/playback-assessments') return json({ recorded: 1 })
      if (input === '/api/personal/playback-outcomes') return json({ recorded: true })
      if (typeof input === 'string' && input.endsWith('/playback-attempts')) {
        const body = JSON.parse(String(init?.body ?? '{}')) as { videoFileId: string }
        attempts.push(body.videoFileId)
        return json({
          verdict: 'Started',
          playbackAttemptId: `attempt-${attempts.length}`,
          resumePositionMilliseconds: null,
        })
      }
      if (typeof input === 'string' && input.endsWith('/end')) return json({ ended: true })
      if (typeof input === 'string' && input.startsWith('/media/videos/')) {
        return Promise.resolve(new Response(null, { status: 206 }))
      }
      if (isFacetRequest(input)) return json(noFacets())
      if (isVideoRequest(input)) return json(videoDetail(video))
      if (isLibraryRequest(input)) return json(libraryPage([video]))
      return json([])
    })

    // Playback is the Video's own page, so this is where the fallback chain plays out.
    const rendered = renderApp('/videos/01994dd4-2a0a-7000-8000-000000000010')
    expect(await screen.findByRole('heading', { name: 'Two Variants' })).toBeInTheDocument()

    // The browser answers for the configurations the library holds, and says how it answered.
    await vi.waitFor(() => expect(decodingInfo).toHaveBeenCalled())
    await vi.waitFor(() => expect(globalThis.fetch).toHaveBeenCalledWith(
      '/api/personal/playback-assessments',
      expect.objectContaining({ method: 'PUT' }),
    ))

    fireEvent.click(screen.getByRole('button', { name: 'Play' }))
    const player = await waitForVideo(rendered.container)
    expect(attempts).toEqual(['01994dd4-2a0a-7000-8000-0000000000a1'])

    // A decode failure is remembered about that file and the next variant is tried, visibly.
    Object.defineProperty(player, 'error', {
      configurable: true,
      // MEDIA_ERR_DECODE: the browser accepted the bytes and could not decode them.
      value: { code: 3 },
    })
    fireEvent.error(player)

    await vi.waitFor(() => expect(globalThis.fetch).toHaveBeenCalledWith(
      '/api/personal/playback-outcomes',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          videoFileId: '01994dd4-2a0a-7000-8000-0000000000a1',
          outcome: 'Failed',
          failureCategory: 'Media',
        }),
      }),
    ))
    // The fallback stays inside the same Playback Attempt, and it says so rather than presenting
    // the intermediate failure as terminal.
    expect(await screen.findByText(/did not play in this browser/)).toBeInTheDocument()
    expect(attempts).toEqual(['01994dd4-2a0a-7000-8000-0000000000a1'])
    await vi.waitFor(() => expect(
      rendered.container.querySelector('video')?.getAttribute('src'),
    ).toBe('/media/videos/bbb'))
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
      playability: 'NotDirectlyPlayable',
      isUnsupportedVideo: true,
      videoFiles: [variant({
        containerFormat: 'asf',
        videoCodec: 'wmv3',
        audioCodec: 'wmav2',
        directPlayClassification: 'Unsupported',
        readyForDirectPlay: false,
        selectionReason: 'RuledOutHere',
        basicContentType: null,
      })],
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
      if (input === '/api/personal/playback-profiles') return json([])
      if (isFacetRequest(input)) return json(noFacets())
      if (isLibraryRequest(input)) {
        return json(libraryPage([unsupported], { includesNotReadyForDirectPlay: true }))
      }
      if (input === '/api/library/preferences/include-not-ready') {
        return json({ includesNotReadyForDirectPlay: false })
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
    expect(screen.getByText(/asf \(wmv3 \+ wmav2\)/)).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Play' })).not.toBeInTheDocument()

    // A locally recognised Site says so rather than looking like a prdb match.
    expect(screen.getByText('Known Site · recognised locally')).toBeInTheDocument()

    // The explicit filter narrows one view, and the address is what carries it.
    fireEvent.click(screen.getByRole('button', { name: 'Unsupported only' }))
    await vi.waitFor(() => expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('playability=NotDirectlyPlayable'),
      expect.anything(),
    ))

    // The standing preference is not a filter, so it lives with the Account rather than beside
    // the facets, and can be turned off again from there.
    fireEvent.click(screen.getByRole('link', { name: 'Your Account' }))
    const preference = await screen.findByLabelText('Show unsupported Videos in ordinary results')
    await vi.waitFor(() => expect(preference).toBeChecked())
    fireEvent.click(preference)
    await vi.waitFor(() => expect(globalThis.fetch).toHaveBeenCalledWith(
      '/api/library/preferences/include-not-ready',
      expect.objectContaining({ method: 'PUT', body: JSON.stringify({ included: false }) }),
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
      if (input === '/api/personal/playback-profiles') return json([])
      if (isFacetRequest(input)) return json(noFacets())
      if (isLibraryRequest(input)) return json(libraryPage([]))
      return json([])
    })

    renderApp('/admin/work')

    expect(await screen.findByText('Operational attention')).toBeInTheDocument()
    expect(screen.getByText(/1 issue blocks work until someone acts\./)).toBeInTheDocument()
    expect(await screen.findByText('Library directory “Films” cannot be scanned')).toBeInTheDocument()
    expect(screen.getByText(/WI-A1B2C3D4E5F6/)).toBeInTheDocument()
    expect(screen.getByText(/\/library\/films/)).toBeInTheDocument()

    // A running Library Scan says what it has found, in words. A ratio against its own discovery
    // would always read as complete, and a percentage would be invented outright.
    expect(screen.getByText('12 files found so far')).toBeInTheDocument()

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
