import { useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import {
  api,
  emptyFilters,
  type Account,
  type AccountSummary,
  type BootstrapRequest,
  type LibraryDirectoryStage,
  type RecoverRequest,
  type RegistrationRequest,
  type IdentificationCase,
  type IdentificationConsequence,
  type IdentificationDecisionAction,
  type IdentificationQueueItem,
  type ClientPlaybackAssessmentReport,
  type PlaybackFailureCategory,
  type PlaybackReportRequest,
  type PlaybackVariant,
  type UnassessedPlaybackProfile,
  type SignInRequest,
  type LibraryFacets,
  type LibraryFilters,
  type LibraryPage,
  type VideoSummary,
  type WorkIssueAction,
  type WorkIssueSummary,
} from './api/client'

const queryKeys = {
  state: ['access-state'] as const,
  account: ['account'] as const,
  accounts: ['accounts'] as const,
  configuration: ['configuration'] as const,
  libraryDirectoryCandidates: ['library-directory-candidates'] as const,
  backgroundWork: ['background-work'] as const,
  workIssueItems: (workIssueId: string) => ['work-issue-items', workIssueId] as const,
  identificationQueue: ['identification-queue'] as const,
  identificationCase: (videoId: string) => ['identification-case', videoId] as const,
  videos: (filters: string, pages: number) => ['videos', filters, pages] as const,
  libraryFacets: ['library-facets'] as const,
  personalLibrary: ['personal-library'] as const,
  playbackProfiles: ['playback-profiles'] as const,
}

export function App() {
  const state = useQuery({ queryKey: queryKeys.state, queryFn: api.state, retry: false })
  const account = useQuery({
    queryKey: queryKeys.account,
    queryFn: api.me,
    enabled: state.data?.signedIn === true,
    staleTime: Number.POSITIVE_INFINITY,
    retry: false,
  })

  if (state.isPending || (state.data?.signedIn && account.isPending)) {
    return <CenteredCard><p role="status">Opening your library…</p></CenteredCard>
  }

  if (state.isError || account.isError) {
    return <CenteredCard><Notice kind="error">The viewer could not reach its API. Try again shortly.</Notice></CenteredCard>
  }

  if (!state.data.claimed) {
    return <BootstrapPanel />
  }

  if (!state.data.signedIn || !account.data) {
    return <AccessPanel />
  }

  return <Library account={account.data} />
}

function BootstrapPanel() {
  const queryClient = useQueryClient()
  const mutation = useMutation({
    mutationFn: api.bootstrap,
    onSuccess: (result) => {
      if (result.account) {
        queryClient.setQueryData(queryKeys.account, result.account)
        queryClient.setQueryData(queryKeys.state, { claimed: true, signedIn: true })
      }
    },
  })

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    mutation.mutate(values<BootstrapRequest>(event.currentTarget, ['authorization', 'username', 'password', 'email']))
  }

  return (
    <CenteredCard>
      <Brand />
      <h2>Claim this installation</h2>
      <p>Use the one-time authorization written by the operator command, then create the first Administrator.</p>
      <form onSubmit={submit}>
        <Field name="authorization" label="One-time authorization" autoComplete="off" required />
        <Field name="username" label="Administrator username" autoComplete="username" required />
        <Field name="email" label="Email (optional)" type="email" autoComplete="email" />
        <Field name="password" label="Password" type="password" autoComplete="new-password" minLength={12} required />
        <SubmitButton pending={mutation.isPending}>Create Administrator</SubmitButton>
      </form>
      {mutation.data && !mutation.data.account && (
        <Notice kind="error">{bootstrapMessage(mutation.data.verdict)}</Notice>
      )}
      {mutation.isError && <RequestError />}
    </CenteredCard>
  )
}

type AccessMode = 'sign-in' | 'register' | 'recover'

function AccessPanel() {
  const [mode, setMode] = useState<AccessMode>('sign-in')
  const queryClient = useQueryClient()
  const signIn = useMutation({
    mutationFn: api.signIn,
    onSuccess: (result) => {
      if (result.account) {
        queryClient.setQueryData(queryKeys.account, result.account)
        queryClient.setQueryData(queryKeys.state, { claimed: true, signedIn: true })
      }
    },
  })
  const register = useMutation({ mutationFn: api.register })
  const recover = useMutation({ mutationFn: api.recover })

  function submitSignIn(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    signIn.mutate(values<SignInRequest>(event.currentTarget, ['username', 'password']))
  }

  function submitRegistration(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    register.mutate(values<RegistrationRequest>(event.currentTarget, ['username', 'password', 'email']))
  }

  function submitRecovery(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    recover.mutate(values<RecoverRequest>(event.currentTarget, ['username', 'recoveryCode', 'newPassword']))
  }

  return (
    <CenteredCard>
      <Brand />
      <div className="tabs" aria-label="Account access">
        <Tab active={mode === 'sign-in'} onClick={() => setMode('sign-in')}>Sign in</Tab>
        <Tab active={mode === 'register'} onClick={() => setMode('register')}>Request access</Tab>
        <Tab active={mode === 'recover'} onClick={() => setMode('recover')}>Recover</Tab>
      </div>

      {mode === 'sign-in' && (
        <form onSubmit={submitSignIn}>
          <Field name="username" label="Username" autoComplete="username" required />
          <Field name="password" label="Password" type="password" autoComplete="current-password" required />
          <SubmitButton pending={signIn.isPending}>Sign in</SubmitButton>
          {signIn.data && !signIn.data.account && <Notice kind="error">{signInMessage(signIn.data.verdict)}</Notice>}
          {signIn.isError && <RequestError />}
        </form>
      )}

      {mode === 'register' && (
        <form onSubmit={submitRegistration}>
          <p>Ask an Administrator to approve your request after submitting it.</p>
          <Field name="username" label="Username" autoComplete="username" required />
          <Field name="email" label="Email (optional)" type="email" autoComplete="email" />
          <Field name="password" label="Password" type="password" autoComplete="new-password" minLength={12} required />
          <SubmitButton pending={register.isPending}>Submit request</SubmitButton>
          {register.data?.verdict === 'Submitted' && <Notice kind="success">Request submitted. Access begins only after approval.</Notice>}
          {register.data?.verdict === 'InvalidInput' && <Notice kind="error">Check the username, email, and password.</Notice>}
          {register.isError && <RequestError />}
        </form>
      )}

      {mode === 'recover' && (
        <form onSubmit={submitRecovery}>
          <Field name="username" label="Username" autoComplete="username" required />
          <Field name="recoveryCode" label="Recovery code" autoComplete="off" required />
          <Field name="newPassword" label="New password" type="password" autoComplete="new-password" minLength={12} required />
          <SubmitButton pending={recover.isPending}>Replace password</SubmitButton>
          {recover.data?.verdict === 'PasswordReplaced' && <Notice kind="success">Password replaced. You can now sign in.</Notice>}
          {recover.data && recover.data.verdict !== 'PasswordReplaced' && <Notice kind="error">The recovery code or account details are invalid.</Notice>}
          {recover.isError && <RequestError />}
        </form>
      )}
    </CenteredCard>
  )
}

function Library({ account }: { account: Account }) {
  const queryClient = useQueryClient()
  const signOut = useMutation({
    mutationFn: () => api.signOut(account.csrfToken),
    onSuccess: () => {
      queryClient.setQueryData(queryKeys.state, { claimed: true, signedIn: false })
      queryClient.removeQueries({ queryKey: queryKeys.account })
    },
  })

  return (
    <main className="app-shell">
      <header className="app-header">
        <Brand compact />
        <div className="account-menu">
          <span>{account.username}</span>
          <button className="quiet-button" onClick={() => signOut.mutate()} disabled={signOut.isPending}>Sign out</button>
        </div>
      </header>
      <section className="workspace">
        <div>
          <span className="eyebrow">Library</span>
          <h2>Your collection starts here</h2>
          <p>Account access is ready. Active Library Directories will appear here as scanning discovers playable Videos.</p>
        </div>
        <VideoLibrary account={account} />
        {account.authority === 'Administrator' && (
          <>
            <InstallationSetup account={account} />
            <IdentificationReview account={account} />
            <BackgroundWorkPanel account={account} />
            <AccountAdministration account={account} />
          </>
        )}
      </section>
    </main>
  )
}

/// Qualifies this browser against the media configurations the library actually holds.
///
/// Client Video Playability is per Account and per client, and the only one who can answer for a
/// client is the client. This asks about configurations it has not answered for — including those
/// of Videos it currently cannot see, which is exactly the set an unqualified client is missing —
/// measures each with Media Capabilities where the inspected facts determine a full codec string,
/// falls back to the coarser support test where they do not, and reports what it found.
function useClientQualification(account: Account) {
  const queryClient = useQueryClient()
  const profiles = useQuery({
    queryKey: queryKeys.playbackProfiles,
    queryFn: api.unassessedPlaybackProfiles,
    staleTime: 60_000,
  })
  const report = useMutation({
    mutationFn: (assessments: ClientPlaybackAssessmentReport[]) =>
      api.recordPlaybackAssessments(assessments, account.csrfToken),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['videos'] })
      void queryClient.invalidateQueries({ queryKey: queryKeys.personalLibrary })
      void queryClient.invalidateQueries({ queryKey: queryKeys.playbackProfiles })
    },
  })
  const pending = report.isPending
  const outstanding = profiles.data

  useEffect(() => {
    if (!outstanding || outstanding.length === 0 || pending) return
    let cancelled = false
    void Promise.all(outstanding.map(assessProfile)).then((assessments) => {
      if (!cancelled && assessments.length > 0) {
        report.mutate(assessments)
      }
    })
    return () => { cancelled = true }
    // The mutation is intentionally not a dependency: it changes identity on every render, and
    // one round of qualification per set of outstanding profiles is what this owes the library.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [outstanding, pending])

  return report.isError
}

/// What this browser makes of one media configuration.
async function assessProfile(
  profile: UnassessedPlaybackProfile,
): Promise<ClientPlaybackAssessmentReport> {
  const capabilities = navigator.mediaCapabilities

  if (capabilities && profile.videoContentType) {
    try {
      const support = await capabilities.decodingInfo({
        type: 'file',
        video: {
          contentType: profile.videoContentType,
          width: Number(profile.width ?? 1280),
          height: Number(profile.height ?? 720),
          bitrate: Number(profile.bitrate ?? 2_000_000),
          framerate: Number(profile.frameRate ?? 25),
        },
        ...(profile.audioContentType
          ? {
            audio: {
              contentType: profile.audioContentType,
              channels: String(profile.audioChannels ?? 2),
              bitrate: Number(profile.audioBitrate ?? 128_000),
              samplerate: Number(profile.audioSampleRate ?? 48_000),
            },
          }
          : {}),
      })

      return {
        profileKey: profile.profileKey,
        verdict: support.supported ? 'Positive' : 'Negative',
        smooth: support.smooth ?? null,
        powerEfficient: support.powerEfficient ?? null,
        method: 'MediaCapabilities',
      }
    } catch {
      // A configuration this browser cannot even be asked about is not an answer either way.
    }
  }

  const probe = document.createElement('video')
  const answer = profile.basicContentType ? probe.canPlayType(profile.basicContentType) : ''

  return {
    profileKey: profile.profileKey,
    // `maybe` is the browser declining to commit, which is indeterminate rather than a refusal.
    verdict: answer === 'probably' ? 'Positive' : answer === 'maybe' ? 'Indeterminate' : 'Negative',
    smooth: null,
    powerEfficient: null,
    method: 'CanPlayType',
  }
}

function VideoLibrary({ account }: { account: Account }) {
  const [filters, setFilters] = useState<LibraryFilters>(emptyFilters)
  const [pages, setPages] = useState(1)
  const facets = useQuery({ queryKey: queryKeys.libraryFacets, queryFn: api.libraryFacets })
  const videos = useQuery({
    queryKey: queryKeys.videos(JSON.stringify(filters), pages),
    queryFn: () => api.videos(filters, 0, pageSize * pages),
    refetchInterval: 5_000,
    placeholderData: (previous) => previous,
  })
  const narrow = (change: Partial<LibraryFilters>) => {
    setPages(1)
    setFilters((current) => ({ ...current, ...change }))
  }
  const includeNotReady = useMutation({
    mutationFn: (included: boolean) => api.setIncludeNotReady(included, account.csrfToken),
    onSuccess: () => {
      setPages(1)
      void queryClient.invalidateQueries({ queryKey: ['videos'] })
    },
  })
  const personalLibrary = useQuery({
    queryKey: queryKeys.personalLibrary,
    queryFn: api.personalLibrary,
  })
  const queryClient = useQueryClient()
  const qualificationFailed = useClientQualification(account)
  const [playing, setPlaying] = useState<PlaybackSession>()
  const [failure, setFailure] = useState<TerminalPlaybackFailure>()
  const startPlayback = useMutation({
    mutationFn: ({ video, variant, remaining, attempted }: {
      video: VideoSummary
      variant: PlaybackVariant
      remaining: PlaybackVariant[]
      attempted: PlaybackVariant[]
    }) => api.startPlaybackAttempt(video.id, variant.videoFileId, account.csrfToken)
      .then((result) => ({ result, video, variant, remaining, attempted })),
    onSuccess: ({ result, video, variant, remaining, attempted }) => {
      if (result.verdict === 'Started' && result.playbackAttemptId) {
        setPlaying({
          video,
          variant,
          remaining,
          attempted,
          playbackAttemptId: result.playbackAttemptId,
          resumePositionMilliseconds: Number(result.resumePositionMilliseconds ?? 0),
        })
        return
      }

      setFailure({
        video,
        attempted: [...attempted, variant],
        category: 'Availability',
        detail: 'The Video File could not be opened for playback.',
      })
    },
  })
  const recordOutcome = useMutation({
    mutationFn: ({ videoFileId, outcome, category }: {
      videoFileId: string
      outcome: 'Succeeded' | 'Failed'
      category: PlaybackFailureCategory | null
    }) => api.recordPlaybackOutcome(videoFileId, outcome, category, account.csrfToken),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['videos'] })
      void queryClient.invalidateQueries({ queryKey: queryKeys.personalLibrary })
    },
  })
  const forgetOutcomes = useMutation({
    mutationFn: (videoId: string) => api.forgetPlaybackOutcomes(videoId, account.csrfToken),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['videos'] })
      void queryClient.invalidateQueries({ queryKey: queryKeys.personalLibrary })
    },
  })
  const personalAction = useMutation({
    mutationFn: ({ kind, video, selected, rating }: {
      kind: 'favourite' | 'watch-later' | 'rating' | 'dismiss'
      video: VideoSummary
      selected?: boolean
      rating?: number | null
    }) => {
      if (kind === 'favourite') {
        return api.setFavourite(video.id, selected === true, account.csrfToken)
      }
      if (kind === 'watch-later') {
        return api.setWatchLater(video.id, selected === true, account.csrfToken)
      }
      if (kind === 'rating') {
        return api.setRating(video.id, rating ?? null, account.csrfToken)
      }
      return api.dismissContinueWatching(video.id, account.csrfToken)
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['videos'] })
      void queryClient.invalidateQueries({ queryKey: queryKeys.personalLibrary })
    },
  })

  /// One deliberate play action. The server has already ordered the variants by the evidence this
  /// client produced, so this follows that order and tries each Available occurrence at most once.
  /// A variant the client has ruled out is attempted only when it was chosen explicitly.
  const play = (video: VideoSummary, chosen?: PlaybackVariant) => {
    const ordered = chosen
      ? [chosen, ...video.videoFiles.filter((variant) => variant.videoFileId !== chosen.videoFileId)]
      : video.videoFiles.filter((variant) => variant.selectionReason !== 'RuledOutHere')
    const [first, ...remaining] = ordered
    if (!first) return
    setFailure(undefined)
    startPlayback.mutate({ video, variant: first, remaining, attempted: [] })
  }

  /// What a failed attempt does next. Only a media failure says anything about the file, so only
  /// that is remembered and only that falls back: a delivery or network failure would fail the same
  /// way for every other variant, and trying them all would say nothing and cost everything.
  const failed = (category: PlaybackFailureCategory) => {
    const session = playing
    if (!session) return
    setPlaying(undefined)

    if (category === 'Media') {
      recordOutcome.mutate({
        videoFileId: session.variant.videoFileId,
        outcome: 'Failed',
        category,
      })
    }

    const attempted = [...session.attempted, session.variant]
    const next = category === 'Media' ? session.remaining[0] : undefined

    if (next) {
      startPlayback.mutate({
        video: session.video,
        variant: next,
        remaining: session.remaining.slice(1),
        attempted,
      })
      return
    }

    setFailure({ video: session.video, attempted, category, detail: undefined })
  }

  /// A confirmed success is the strongest evidence there is for this Account on this client, and
  /// the only one that is not a prediction.
  const succeeded = () => {
    if (playing) {
      recordOutcome.mutate({
        videoFileId: playing.variant.videoFileId,
        outcome: 'Succeeded',
        category: null,
      })
    }
  }
  const act = (
    kind: 'favourite' | 'watch-later' | 'rating' | 'dismiss',
    video: VideoSummary,
    value?: boolean | number | null,
  ) => personalAction.mutate({
    kind,
    video,
    selected: typeof value === 'boolean' ? value : undefined,
    rating: typeof value === 'number' || value === null ? value : undefined,
  })

  if (videos.isPending) {
    return <p role="status">Opening the shared library…</p>
  }

  if (videos.isError || personalLibrary.isError) {
    return <RequestError />
  }

  const page = videos.data
  const narrowed = filters.query.trim().length > 0 ||
    filters.sites.length > 0 ||
    filters.actors.length > 0 ||
    filters.unknownSite ||
    filters.work.length > 0 ||
    filters.playability.length > 0 ||
    filters.availability.length > 0 ||
    filters.playState.length > 0

  return (
    <section className="video-library" aria-labelledby="videos-title">
      {playing && (
        <TrackedPlayer
          video={playing.video}
          source={playing.variant.deliveryUrl}
          videoFileId={playing.variant.videoFileId}
          playbackAttemptId={playing.playbackAttemptId}
          resumePositionMilliseconds={playing.resumePositionMilliseconds}
          csrfToken={account.csrfToken}
          close={() => setPlaying(undefined)}
          failed={failed}
          succeeded={succeeded}
          refresh={() => {
            void queryClient.invalidateQueries({ queryKey: ['videos'] })
            void queryClient.invalidateQueries({ queryKey: queryKeys.personalLibrary })
          }}
        />
      )}
      {startPlayback.isPending && (startPlayback.variables?.attempted.length ?? 0) > 0 && (
        <p className="fallback-notice" role="status">
          That Video File did not play here. Trying{' '}
          {fileFormat(startPlayback.variables!.variant)} instead…
        </p>
      )}
      {failure && (
        <TerminalFailure
          failure={failure}
          dismiss={() => setFailure(undefined)}
          retry={() => {
            forgetOutcomes.mutate(failure.video.id)
            setFailure(undefined)
          }}
        />
      )}
      {personalLibrary.data && (
        <div className="personal-library">
          <VideoShelf title="Continue Watching" videos={personalLibrary.data.continueWatching} kind="continue" play={play} act={act} pending={personalAction.isPending || startPlayback.isPending || playing !== undefined} />
          <VideoShelf title="Favourites" videos={personalLibrary.data.favourites} kind="favourite" play={play} act={act} pending={personalAction.isPending || startPlayback.isPending || playing !== undefined} />
          <VideoShelf title="Watch Later" videos={personalLibrary.data.watchLater} kind="watch-later" play={play} act={act} pending={personalAction.isPending || startPlayback.isPending || playing !== undefined} />
        </div>
      )}
      <div className="section-heading">
        <h3 id="videos-title">Videos</h3>
        <span className="muted">{page.totalMatches} {narrowed ? 'matching' : 'available'}</span>
      </div>
      <LibraryControls
        filters={filters}
        facets={facets.data}
        narrow={narrow}
        clear={() => { setPages(1); setFilters(emptyFilters) }}
        narrowed={narrowed}
        includesNotReady={page.includesNotReadyForDirectPlay === true}
        setIncludesNotReady={(included) => includeNotReady.mutate(included)}
        preferencePending={includeNotReady.isPending}
      />
      {page.videos.length === 0 && (
        <div className="empty-library">
          <strong>{narrowed ? 'Nothing matches' : 'No Videos yet'}</strong>
          <p>{narrowed
            ? 'Adjust the search or the filters.'
            : 'Videos appear here as technical inspection completes.'}</p>
        </div>
      )}
      <VideoGrid videos={page.videos} play={play} act={act} pending={personalAction.isPending || startPlayback.isPending || playing !== undefined} />
      <HiddenMatches
        page={page}
        includeNotReady={() => includeNotReady.mutate(true)}
        showUnavailable={() => narrow({ availability: ['Unavailable'], playability: playabilityValues })}
        pending={includeNotReady.isPending}
      />
      {page.hasMore && (
        <button
          className="quiet-button load-more"
          onClick={() => setPages((current) => current + 1)}
          disabled={videos.isFetching}
        >
          {videos.isFetching ? 'Loading…' : 'Show more'}
        </button>
      )}
      {(startPlayback.isError || personalAction.isError || includeNotReady.isError ||
        qualificationFailed) && <RequestError />}
    </section>
  )
}

const pageSize = 60

const playabilityValues = ['ReadyForDirectPlay', 'CompatibilityUncertain', 'NotDirectlyPlayable']

/// One deliberate play action in progress: the variant being tried, the ones left to try, and the
/// ones already attempted, so no occurrence is tried twice and the failure can name them all.
type PlaybackSession = {
  video: VideoSummary
  variant: PlaybackVariant
  remaining: PlaybackVariant[]
  attempted: PlaybackVariant[]
  playbackAttemptId: string
  resumePositionMilliseconds: number
}

type TerminalPlaybackFailure = {
  video: VideoSummary
  attempted: PlaybackVariant[]
  category: PlaybackFailureCategory
  detail?: string
}

/// The search box, the two sort orders and the facets, which is the whole of the MVP's browsing.
function LibraryControls({
  filters,
  facets,
  narrow,
  clear,
  narrowed,
  includesNotReady,
  setIncludesNotReady,
  preferencePending,
}: {
  filters: LibraryFilters
  facets?: LibraryFacets
  narrow: (change: Partial<LibraryFilters>) => void
  clear: () => void
  narrowed: boolean
  includesNotReady: boolean
  setIncludesNotReady: (included: boolean) => void
  preferencePending: boolean
}) {
  return (
    <div className="library-controls">
      <div className="library-search">
        <label className="field">
          <span>Search</span>
          <input
            type="search"
            value={filters.query}
            placeholder="Title, site, actor or file name"
            onChange={(event) => narrow({ query: event.target.value })}
          />
        </label>
        <label className="field">
          <span>Sort</span>
          <select
            value={filters.sort}
            onChange={(event) => narrow({ sort: event.target.value as LibraryFilters['sort'] })}
          >
            <option value="Newest">Newest</option>
            <option value="TitleAscending">Title A–Z</option>
          </select>
        </label>
        {narrowed && <button className="quiet-button" onClick={clear}>Clear</button>}
      </div>
      <div className="facet-row">
        <FacetToggle
          label="Unknown work"
          selected={filters.work.includes('Unknown')}
          onToggle={(selected) => narrow({ work: selected ? ['Unknown'] : [] })}
        />
        <FacetToggle
          label="Unknown site"
          selected={filters.unknownSite}
          onToggle={(selected) => narrow({ unknownSite: selected })}
        />
        <FacetToggle
          label="Needs review"
          selected={filters.review.includes('ReviewNeeded')}
          onToggle={(selected) => narrow({ review: selected ? ['ReviewNeeded'] : [] })}
        />
        <FacetToggle
          label="Unplayed"
          selected={filters.playState.includes('Unplayed')}
          onToggle={(selected) => narrow({ playState: selected ? ['Unplayed'] : [] })}
        />
        <FacetToggle
          label="Unsupported only"
          selected={filters.playability.includes('NotDirectlyPlayable')}
          onToggle={(selected) => narrow({ playability: selected ? ['NotDirectlyPlayable'] : [] })}
        />
      </div>
      <div className="facet-row">
        <label className="preference">
          <input
            type="checkbox"
            checked={includesNotReady}
            disabled={preferencePending}
            onChange={(event) => setIncludesNotReady(event.target.checked)}
          />
          <span>Show unsupported Videos in ordinary results</span>
        </label>
      </div>
      {facets?.sites?.length ? (
        <div className="facet-row" aria-label="Sites">
          {facets.sites.map((site) => (
            <FacetToggle
              key={site.value}
              label={`${site.value} (${site.count})`}
              selected={filters.sites.includes(site.value)}
              onToggle={(selected) => narrow({ sites: selected ? [site.value] : [] })}
            />
          ))}
        </div>
      ) : null}
      {facets?.actors?.length ? (
        <div className="facet-row" aria-label="Actors">
          {facets.actors.map((actor) => (
            <FacetToggle
              key={actor.value}
              label={`${actor.value} (${actor.count})`}
              selected={filters.actors.includes(actor.value)}
              onToggle={(selected) => narrow({ actors: selected ? [actor.value] : [] })}
            />
          ))}
        </div>
      ) : null}
    </div>
  )
}

function FacetToggle({ label, selected, onToggle }: {
  label: string
  selected: boolean
  onToggle: (selected: boolean) => void
}) {
  return (
    <button
      className={selected ? 'facet selected' : 'facet'}
      aria-pressed={selected}
      onClick={() => onToggle(!selected)}
    >
      {label}
    </button>
  )
}

/// Matches the current rules keep out are reported rather than silently dropped, together with
/// the control that reveals them.
function HiddenMatches({ page, includeNotReady, showUnavailable, pending }: {
  page: LibraryPage
  includeNotReady: () => void
  showUnavailable: () => void
  pending: boolean
}) {
  if (Number(page.hiddenNotReadyForDirectPlay) === 0 && Number(page.hiddenUnavailable) === 0) {
    return null
  }

  return (
    <div className="hidden-matches" role="status">
      {Number(page.hiddenNotReadyForDirectPlay) > 0 && (
        <p>
          {page.hiddenNotReadyForDirectPlay} match
          {Number(page.hiddenNotReadyForDirectPlay) === 1 ? '' : 'es'} not ready for direct play.
          <button className="quiet-button" onClick={includeNotReady} disabled={pending}>
            Include them
          </button>
        </p>
      )}
      {Number(page.hiddenUnavailable) > 0 && (
        <p>
          {page.hiddenUnavailable} match
          {Number(page.hiddenUnavailable) === 1 ? '' : 'es'} currently unavailable.
          <button className="quiet-button" onClick={showUnavailable}>Show them</button>
        </p>
      )}
    </div>
  )
}

type PersonalAction = (
  kind: 'favourite' | 'watch-later' | 'rating' | 'dismiss',
  video: VideoSummary,
  value?: boolean | number | null,
) => void

function VideoShelf({ title, videos, kind, play, act, pending }: {
  title: string
  videos: VideoSummary[]
  kind: 'continue' | 'favourite' | 'watch-later'
  play: (video: VideoSummary) => void
  act: PersonalAction
  pending: boolean
}) {
  if (videos.length === 0) return null
  return (
    <section className="personal-shelf" aria-label={title}>
      <div className="section-heading"><h3>{title}</h3><span className="muted">{videos.length}</span></div>
      <VideoGrid videos={videos} play={play} act={act} pending={pending} dismissible={kind === 'continue'} />
    </section>
  )
}

function VideoGrid({ videos, play, act, pending, dismissible = false }: {
  videos: VideoSummary[]
  play: (video: VideoSummary) => void
  act: PersonalAction
  pending: boolean
  dismissible?: boolean
}) {
  return (
    <div className="video-grid">
      {videos.map((video) => (
        <VideoCard
          key={video.id}
          video={video}
          play={() => play(video)}
          act={act}
          pending={pending}
          dismissible={dismissible}
        />
      ))}
    </div>
  )
}

function VideoCard({ video, play, act, pending, dismissible }: {
  video: VideoSummary
  play: (chosen?: PlaybackVariant) => void
  act: PersonalAction
  pending: boolean
  dismissible: boolean
}) {
  const [showVariants, setShowVariants] = useState(false)
  const source = video.videoFiles.find((variant) => variant.selectionReason !== 'RuledOutHere')
  const progress = Number(video.personalState.playbackProgressMilliseconds ?? 0)
  const resume = progress > 0 && video.personalState.playState === 'InProgress'
  return (
    <article className="video-card">
      {video.previewUrl
        ? <img className="video-preview" src={video.previewUrl} alt="" loading="lazy" />
        : <div className="video-placeholder" aria-hidden="true">▶</div>}
      <div>
        <strong>{video.displayTitle}</strong>
        <small>{playbackSupport(video, source)}</small>
        <Provenance identification={video.identification} />
      </div>
      {video.personalState.playState !== 'Unplayed' && (
        <div className="play-state">
          <span>{friendlyState(video.personalState.playState)}</span>
          {progress > 0 && <span>{formatDuration(progress)}</span>}
          <span>{Number(video.personalState.playCount)} plays</span>
        </div>
      )}
      {video.playability === 'ReadyForDirectPlay' && source && (
        <button className="primary-button" onClick={() => play()} disabled={pending}>
          {resume ? 'Resume' : 'Play'}
        </button>
      )}
      {video.playability === 'CompatibilityUncertain' && source && (
        <>
          <button className="primary-button uncertain" onClick={() => play()} disabled={pending}>
            Try Direct Play
          </button>
          <small className="uncertain-note">
            This browser has not confirmed {fileFormat(source)}. Playback may fail; nothing is
            converted.
          </small>
        </>
      )}
      {video.playability === 'NotDirectlyPlayable' && (
        <span className="unsupported">{playbackUnavailableReason(video)}</span>
      )}
      {video.videoFiles.length > 0 && (
        <button
          className="quiet-button variant-toggle"
          aria-expanded={showVariants}
          onClick={() => setShowVariants((current) => !current)}
        >{showVariants ? 'Hide variants' : `Variants (${video.videoFiles.length})`}</button>
      )}
      {showVariants && (
        <ul className="variant-list">
          {video.videoFiles.map((variant) => (
            <li key={variant.videoFileId}>
              <span>{fileFormat(variant)}</span>
              <small>{variantReason(variant)}</small>
              <button
                className="quiet-button"
                onClick={() => play(variant)}
                disabled={pending}
              >{variant.selectionReason === 'RuledOutHere' ? 'Try anyway' : 'Play this one'}</button>
            </li>
          ))}
        </ul>
      )}
      <div className="personal-actions">
        <button
          className={video.personalState.favourite ? 'selected' : ''}
          aria-pressed={video.personalState.favourite}
          onClick={() => act('favourite', video, !video.personalState.favourite)}
          disabled={pending}
        >Favourite</button>
        <button
          className={video.personalState.watchLater ? 'selected' : ''}
          aria-pressed={video.personalState.watchLater}
          onClick={() => act('watch-later', video, !video.personalState.watchLater)}
          disabled={pending}
        >Watch Later</button>
      </div>
      <label className="rating-field">
        <span>Personal Rating</span>
        <select
          aria-label={`Rate ${video.displayTitle}`}
          value={video.personalState.personalRating?.toString() ?? ''}
          onChange={(event) => act('rating', video, event.target.value ? Number(event.target.value) : null)}
          disabled={pending}
        >
          <option value="">Not rated</option>
          {[1, 2, 3, 4, 5].map((rating) => <option key={rating} value={rating}>{rating} / 5</option>)}
        </select>
      </label>
      {dismissible && <button className="dismiss-button" onClick={() => act('dismiss', video)} disabled={pending}>Dismiss</button>}
    </article>
  )
}

function TrackedPlayer({ video, source, videoFileId, playbackAttemptId, resumePositionMilliseconds, csrfToken, close, failed, succeeded, refresh }: {
  video: VideoSummary
  source: string
  videoFileId: string
  playbackAttemptId: string
  resumePositionMilliseconds: number
  csrfToken: string
  close: () => void
  failed: (category: PlaybackFailureCategory) => void
  succeeded: () => void
  refresh: () => void
}) {
  const element = useRef<HTMLVideoElement>(null)
  const lastMediaTime = useRef<number | undefined>(undefined)
  const lastWallTime = useRef<number | undefined>(undefined)
  const activeWatching = useRef(0)
  const sequence = useRef(0)
  const pendingReport = useRef<PlaybackReportRequest | undefined>(undefined)
  const sending = useRef(false)
  const ended = useRef(false)

  const confirmed = useRef(false)

  const resetEvidence = () => {
    const player = element.current
    lastMediaTime.current = player ? player.currentTime * 1_000 : undefined
    lastWallTime.current = performance.now()
  }

  /// Playback that actually advanced is the observation worth keeping, and it is recorded once.
  const confirm = () => {
    if (!confirmed.current) {
      confirmed.current = true
      succeeded()
    }
  }

  const recordEvidence = () => {
    const player = element.current
    if (!player) return
    const mediaTime = player.currentTime * 1_000
    const wallTime = performance.now()
    if (!player.paused && !player.seeking && lastMediaTime.current !== undefined && lastWallTime.current !== undefined) {
      const mediaAdvance = mediaTime - lastMediaTime.current
      const wallAdvance = wallTime - lastWallTime.current
      if (mediaAdvance > 0 && wallAdvance > 0 && wallAdvance <= 16_000 && mediaAdvance <= wallAdvance + 1_000) {
        activeWatching.current += Math.min(mediaAdvance, wallAdvance)
      }
    }
    lastMediaTime.current = mediaTime
    lastWallTime.current = wallTime
  }

  const flush = async (naturalEndConfirmed: boolean, endSession: boolean) => {
    const player = element.current
    if (!player || sending.current) return
    if (!pendingReport.current) {
      const active = Math.min(15_000, Math.round(activeWatching.current))
      if (active === 0 && !endSession) return
      activeWatching.current = Math.max(0, activeWatching.current - active)
      pendingReport.current = {
        reportId: crypto.randomUUID(),
        sequence: sequence.current++,
        videoFileId,
        positionMilliseconds: Math.round(player.currentTime * 1_000),
        activeWatchingMilliseconds: active,
        naturalEndConfirmed,
        endSession,
      }
    }

    sending.current = true
    try {
      const result = await api.reportPlayback(playbackAttemptId, pendingReport.current, csrfToken)
      if (result.verdict === 'Accepted' || result.verdict === 'Duplicate') {
        pendingReport.current = undefined
        refresh()
      }
    } catch {
      // Retain the exact report identifier so the next flush retries idempotently.
    } finally {
      sending.current = false
    }
  }

  useEffect(() => {
    const interval = window.setInterval(() => void flush(false, false), 5_000)
    const leave = () => {
      ended.current = true
      void api.endPlaybackAttempt(playbackAttemptId, csrfToken, true).catch(() => undefined)
    }
    window.addEventListener('pagehide', leave)
    return () => {
      window.clearInterval(interval)
      window.removeEventListener('pagehide', leave)
    }
  }, [])

  const stop = async () => {
    recordEvidence()
    try {
      await flush(false, true)
    } finally {
      ended.current = true
      await api.endPlaybackAttempt(playbackAttemptId, csrfToken).catch(() => undefined)
      close()
    }
  }

  const finish = () => {
    recordEvidence()
    ended.current = true
    void flush(true, true).catch(() => undefined).finally(refresh)
  }

  return (
    <div className="player-shell">
      <div className="section-heading"><strong>{video.displayTitle}</strong><button className="quiet-button" onClick={() => void stop()}>Close</button></div>
      <video
        ref={element}
        controls
        autoPlay
        src={source}
        onLoadedMetadata={(event) => {
          if (resumePositionMilliseconds > 0 && resumePositionMilliseconds < event.currentTarget.duration * 1_000) {
            event.currentTarget.currentTime = resumePositionMilliseconds / 1_000
          }
          resetEvidence()
        }}
        onSeeking={resetEvidence}
        onTimeUpdate={() => {
          recordEvidence()
          if (activeWatching.current >= 5_000) void flush(false, false)
        }}
        onPause={() => {
          recordEvidence()
          void flush(false, false)
        }}
        onPlaying={() => { resetEvidence(); confirm() }}
        onEnded={finish}
        onError={(event) => {
          ended.current = true
          void api.endPlaybackAttempt(playbackAttemptId, csrfToken).catch(() => undefined)
          void classifyFailure(event.currentTarget.error, source).then(failed)
        }}
      >Your browser cannot play this Video File.</video>
    </div>
  )
}

function Provenance({ identification }: { identification?: VideoSummary['identification'] }) {
  if (!identification) return null
  const { work, site } = identification
  const review = work.reviewStatus === 'ReviewNeeded' || site.reviewStatus === 'ReviewNeeded'
  return (
    <div className="provenance">
      <span className={work.resolution === 'Established' ? 'badge established' : 'badge unknown'}>
        {work.resolution === 'Established' ? provenanceLabel(work.source) : 'Unknown Video'}
      </span>
      {site.resolution === 'Established' && site.targetTitle && (
        <span className="badge site">{site.targetTitle} · {siteProvenanceLabel(site.source)}</span>
      )}
      {review && <span className="badge review">Review needed</span>}
      {identification.actors.length > 0 && <small>{identification.actors.join(', ')}</small>}
    </div>
  )
}

/// A recognised Site says where it came from, because a name read out of a file's own path is not
/// the same knowledge as one prdb established.
function siteProvenanceLabel(source: string | null | undefined) {
  if (source === 'PrdbIdentification') return 'from prdb'
  if (source === 'AdministratorDecision') return 'set by an Administrator'
  if (source === 'LocalInference') return 'recognised locally'
  return 'established'
}

/// The claim the open case is actually about. Reviewing a proposed Site next to the current Work
/// Identification would compare two different questions.
function reviewedClaim(open: IdentificationCase, dimension: string) {
  return dimension === 'SiteRecognition' ? open.identification.site : open.identification.work
}

/// Where a proposal came from, in the queue's own line, so an Administrator can tell a remote
/// proposal from one read out of a file's path before opening the case.
function candidateOrigin(source: string | null | undefined) {
  return source === 'LocalInference' ? 'from the file’s own path' : 'from prdb'
}

function provenanceLabel(source: string | null | undefined) {
  if (source === 'PrdbIdentification') return 'prdb match'
  if (source === 'AdministratorDecision') return 'Administrator assignment'
  if (source === 'LocalInference') return 'Local inference'
  return 'Established'
}

function IdentificationReview({ account }: { account: Account }) {
  const queryClient = useQueryClient()
  const queue = useQuery({
    queryKey: queryKeys.identificationQueue,
    queryFn: api.identificationQueue,
    refetchInterval: 15_000,
  })
  const [selected, setSelected] = useState<IdentificationQueueItem>()
  const [pending, setPending] = useState<IdentificationDecisionAction>()
  const [consequence, setConsequence] = useState<IdentificationConsequence>()
  const [note, setNote] = useState('')
  const [target, setTarget] = useState({ key: '', title: '' })
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
    setSeparated([])
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
          targetKey: target.key || null,
          targetTitle: target.title || null,
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
        setSelected(undefined)
        reset()
        void queryClient.invalidateQueries({ queryKey: queryKeys.identificationQueue })
        void queryClient.invalidateQueries({ queryKey: ['videos'] })
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

  return (
    <section className="admin-panel" aria-labelledby="identification-title">
      <div className="section-heading">
        <div>
          <span className="eyebrow">Administrator</span>
          <h3 id="identification-title">Identification review</h3>
        </div>
        <span className="muted">{queue.data?.length ?? 0} open</span>
      </div>
      <p>Candidates and conflicts wait here. Nothing under review reaches ordinary browsing.</p>
      {outcome && <Notice kind="success">{outcome}</Notice>}
      {queue.data?.length === 0 && <p className="muted">No identification decision is waiting.</p>}
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
            </div>
            <button
              className="quiet-button"
              onClick={() => { setSelected(item); reset(); setOutcome(undefined) }}
            >Review</button>
          </article>
        ))}
      </div>

      {selected && openCase.data && (
        <div className="review-case">
          <div className="section-heading">
            <strong>{openCase.data.displayLabel}</strong>
            <button className="quiet-button" onClick={() => { setSelected(undefined); reset() }}>Back to queue</button>
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
              <small>{openCase.data.videoFiles.length} Video File(s)</small>
            </div>
            <div>
              <span className="eyebrow">Proposed</span>
              <p>{selected.candidate.targetTitle}</p>
              <small>{selected.candidate.evidenceSummary}</small>
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
          <div className="assign-target">
            <Field name="targetKey" label="Target identifier" value={target.key} onChange={(event) => setTarget((current) => ({ ...current, key: event.target.value }))} />
            <Field name="targetTitle" label="Target title" value={target.title} onChange={(event) => setTarget((current) => ({ ...current, title: event.target.value }))} />
          </div>
          <div className="row-actions">
            <button onClick={() => act('AcceptCandidate', false)} disabled={decide.isPending}>Accept candidate</button>
            <button onClick={() => act('RejectCandidate', false)} disabled={decide.isPending}>Reject candidate</button>
            <button onClick={() => act('AssignDirectly', false)} disabled={decide.isPending}>Assign directly</button>
            <button onClick={() => act('ReplaceClaim', false)} disabled={decide.isPending}>Replace claim</button>
            <button onClick={() => act('RevokeClaim', false)} disabled={decide.isPending}>Revoke claim</button>
            {openCase.data.videoFiles.length > 1 && (
              <button onClick={() => act('SplitVideo', false)} disabled={decide.isPending}>Split Video</button>
            )}
          </div>

          {pending && consequence && (
            <div className="confirmation" role="group" aria-label="Consequence preview">
              <p>{consequence.claimTransition}</p>
              <p>{consequence.candidateTransition}</p>
              {consequence.mergeSummary && <p>{consequence.mergeSummary}</p>}
              <small>
                Affects {consequence.affectedVideoFileCount} Video File(s) · review becomes{' '}
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
      {(queue.isError || openCase.isError || decide.isError) && <RequestError />}
    </section>
  )
}

/// What the card says about playback beneath the title: the file this client would play and how
/// it knows. It states evidence rather than promising an outcome.
function playbackSupport(video: VideoSummary, source: PlaybackVariant | undefined) {
  if (!source) return friendlyState(video.availability)
  return `${fileFormat(source)} · ${variantReason(source)}`
}

/// Why a Video has no Play action here. It distinguishes the installation-wide case — every
/// occurrence is statically Unsupported — from this client having ruled them out, because those
/// are different facts and only one of them is about the files.
function playbackUnavailableReason(video: VideoSummary) {
  if (video.videoFiles.length === 0) {
    return 'No Video File of this Video is currently available.'
  }
  const formats = Array.from(new Set(video.videoFiles.map(fileFormat))).join(' or ')
  return video.isUnsupportedVideo
    ? `Not directly playable: ${formats} needs conversion, which this product deliberately does not do.`
    : `This browser did not play ${formats}. Another browser or device may still play it.`
}

/// Which kind of failure just happened. The browser says only that playback failed, so the same
/// delivery URL is asked once more: a file the installation cannot serve, or a network that is not
/// there, is not evidence that this browser cannot play the media. Only the media case rules a
/// variant out, so this distinction decides what is remembered and whether anything falls back.
async function classifyFailure(
  error: MediaError | null,
  source: string,
): Promise<PlaybackFailureCategory> {
  if (error?.code === MediaError.MEDIA_ERR_DECODE) return 'Media'

  try {
    const probe = await fetch(source, { method: 'HEAD', credentials: 'same-origin' })
    if (probe.status === 404 || probe.status === 410) return 'Availability'
    if (probe.status >= 500) return 'Delivery'
    if (!probe.ok) return 'Delivery'
  } catch {
    return 'Network'
  }

  return error?.code === MediaError.MEDIA_ERR_NETWORK ? 'Network' : 'Media'
}

/// The end of one deliberate play action that never succeeded. It names what was attempted, says
/// which kind of failure ended it, and offers only the actions that could change the outcome.
function TerminalFailure({ failure, dismiss, retry }: {
  failure: TerminalPlaybackFailure
  dismiss: () => void
  retry: () => void
}) {
  const attempted = failure.attempted.map(fileFormat).join(', ')

  return (
    <div className="terminal-failure" role="alert">
      <strong>{failure.video.displayTitle} could not be played</strong>
      <p>{failureExplanation(failure.category)}</p>
      {failure.detail && <p>{failure.detail}</p>}
      <small>
        {failure.attempted.length === 1 ? 'Attempted variant' : 'Attempted variants'}: {attempted}
      </small>
      <div className="row-actions">
        {failure.category === 'Media' && (
          <button className="quiet-button" onClick={retry}>Forget this and try again</button>
        )}
        {failure.category !== 'Media' && (
          <button className="quiet-button" onClick={dismiss}>Try again later</button>
        )}
        <button className="quiet-button" onClick={dismiss}>Close</button>
      </div>
    </div>
  )
}

function failureExplanation(category: PlaybackFailureCategory) {
  if (category === 'Media') {
    return 'This browser could not decode the media in any Available Video File. Nothing is ' +
      'converted, so another browser or device may still play it.'
  }
  if (category === 'Availability') {
    return 'The Video File could not be read where the library expects it. This is a library ' +
      'problem rather than a browser one, and the next scan will reconcile it.'
  }
  if (category === 'Delivery') {
    return 'The installation answered the playback request incorrectly. Other Video Files would ' +
      'fail the same way, so nothing else was attempted. An Administrator should check the ' +
      'installation and any reverse proxy in front of it.'
  }
  return 'This browser could not reach the installation. Nothing about the Video File is known ' +
    'from this attempt.'
}

/// How one variant came to its place in the order, in the User's words.
function variantReason(variant: PlaybackVariant) {
  if (variant.selectionReason === 'PreviouslyPlayedHere') return 'played here before'
  if (variant.selectionReason === 'PositivelyAssessedAndSmooth') {
    return variant.powerEfficient ? 'smooth and energy-efficient here' : 'expected to play smoothly here'
  }
  if (variant.selectionReason === 'PositivelyAssessed') return 'this browser accepts it'
  if (variant.selectionReason === 'BaselineCandidate') return 'the cross-browser baseline'
  if (variant.selectionReason === 'RuledOutHere') {
    return variant.outcome === 'Failed' ? 'failed here before' : 'this browser rejects it'
  }
  return 'not assessed yet'
}

function fileFormat(file: PlaybackVariant) {
  const codecs = [file.videoCodec, file.audioCodec].filter(Boolean).join(' + ')
  return `${file.containerFormat} (${codecs})`
}

function formatDuration(milliseconds: number) {
  const totalSeconds = Math.floor(milliseconds / 1_000)
  return `${Math.floor(totalSeconds / 60)}:${(totalSeconds % 60).toString().padStart(2, '0')}`
}

function BackgroundWorkPanel({ account }: { account: Account }) {
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
    <section className="admin-panel" aria-labelledby="work-title">
      <div className="section-heading">
        <div><span className="eyebrow">Administrator</span><h3 id="work-title">Background work</h3></div>
        {status.isFetching && <span className="muted">Refreshing…</span>}
      </div>
      {status.data?.operationalAttention && (
        <p className="attention-banner" role="status">
          <strong>Operational attention</strong>
          <span>
            {status.data.operationalAttentionCount} issue
            {Number(status.data.operationalAttentionCount) === 1 ? '' : 's'} block work until someone acts.
          </span>
        </p>
      )}
      <p>
        Library Scans and every derived lane resume from durable checkpoints after a restart.
        {status.data?.paused && ' Background work is paused installation-wide.'}
      </p>
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
      {status.data?.work.length === 0 && <p className="muted">No Background Work has run yet.</p>}
      {status.data?.work.map((work) => (
        <article className="work-row" key={work.id}>
          <div>
            <strong>{friendlyState(work.category)}</strong>
            <small>
              {work.libraryDirectoryName} · {friendlyState(work.state)} · {work.phase}
              {work.waitingReason ? ` · ${work.waitingReason}` : ''}
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
      {(configuration.isError || status.isError || scan.isError || pause.isError || cancel.isError) && (
        <RequestError />
      )}
    </section>
  )
}

function WorkIssueCard({ issue, account, refresh }: {
  issue: WorkIssueSummary
  account: Account
  refresh: () => void
}) {
  const [showItems, setShowItems] = useState(false)
  const [copied, setCopied] = useState(false)
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

              if (action === 'OpenPrdbSettings' || action === 'OpenLibraryDirectory') {
                document.getElementById('setup-title')?.scrollIntoView({ behavior: 'smooth' })
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
      {advance.isError && <RequestError />}
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

function InstallationSetup({ account }: { account: Account }) {
  const configuration = useQuery({ queryKey: queryKeys.configuration, queryFn: api.configuration })
  const candidates = useQuery({
    queryKey: queryKeys.libraryDirectoryCandidates,
    queryFn: api.libraryDirectoryCandidates,
  })
  const queryClient = useQueryClient()
  const [stage, setStage] = useState<LibraryDirectoryStage>()
  const verify = useMutation({
    mutationFn: (credential: string) => api.verifyPrdb(credential, account.csrfToken),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: queryKeys.configuration }),
  })
  const retry = useMutation({
    mutationFn: () => api.retryPrdb(account.csrfToken),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: queryKeys.configuration }),
  })
  const stageDirectory = useMutation({
    mutationFn: ({ name, containerPath }: { name: string; containerPath: string }) =>
      api.stageLibraryDirectory(name, containerPath, account.csrfToken),
    onSuccess: (result) => {
      setStage(result)
      void queryClient.invalidateQueries({ queryKey: queryKeys.configuration })
    },
  })
  const activate = useMutation({
    mutationFn: (stageId: string) => api.activateLibraryDirectory(stageId, account.csrfToken),
    onSuccess: () => {
      setStage(undefined)
      void queryClient.invalidateQueries({ queryKey: queryKeys.configuration })
    },
  })

  function submitCredential(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = event.currentTarget
    verify.mutate(new FormData(form).get('credential')?.toString() ?? '', {
      onSuccess: (result) => {
        if (result.verdict === 'Verified') form.reset()
      },
    })
  }

  function submitDirectory(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    stageDirectory.mutate(values<{ name: string; containerPath: string }>(
      event.currentTarget,
      ['name', 'containerPath'],
    ))
  }

  const current = configuration.data
  const connectionReady = current?.prdbConnectionStatus === 'Verified'
  const connectionRetryable = current?.prdbConnectionStatus === 'VerificationPending' ||
    current?.prdbConnectionStatus === 'Degraded'

  return (
    <section className="admin-panel setup-panel" aria-labelledby="setup-title">
      <div className="section-heading">
        <div><span className="eyebrow">Installation</span><h3 id="setup-title">Configuration</h3></div>
        {current && <span className={`state-badge ${current.status === 'Configured' ? 'ready' : ''}`}>{friendlyState(current.status)}</span>}
      </div>

      {configuration.isPending && <p role="status">Loading configuration…</p>}
      {current && (
        <div className="setup-grid">
          <div className="setup-step">
            <div className="step-title"><span>1</span><div><strong>prdb connection</strong><small>{friendlyState(current.prdbConnectionStatus)}</small></div></div>
            <p>The API key is verified once and is never returned by the application.</p>
            <form onSubmit={submitCredential}>
              <Field name="credential" label={current.hasPrdbCredential ? 'Replacement API key' : 'API key'} type="password" autoComplete="off" required />
              <SubmitButton pending={verify.isPending}>{connectionReady ? 'Verify replacement' : 'Verify connection'}</SubmitButton>
            </form>
            {connectionRetryable && <button className="quiet-button inline-button" onClick={() => retry.mutate()} disabled={retry.isPending}>Retry verification</button>}
            {verify.data?.verdict === 'Verified' && <Notice kind="success">The prdb connection is verified.</Notice>}
            {verify.data?.verdict === 'Rejected' && <Notice kind="error">prdb rejected this API key. The previously verified key, if any, remains active.</Notice>}
            {verify.data?.verdict === 'VerificationPending' && <Notice kind="error">prdb is temporarily unavailable. The key is staged for a visible retry.</Notice>}
          </div>

          <div className="setup-step">
            <div className="step-title"><span>2</span><div><strong>Library Directory</strong><small>{current.libraryDirectories.length > 0 ? 'Active' : 'Required'}</small></div></div>
            <p>Select a readable directory mounted beneath <code>{current.libraryMountRoot}</code>. The container mount remains the Installation Operator's responsibility.</p>
            <form onSubmit={submitDirectory}>
              <Field name="name" label="Display name" placeholder="Main Library" required />
              <Field name="containerPath" label="Container path" list="library-directory-candidates" placeholder={`${current.libraryMountRoot}/main`} required />
              <datalist id="library-directory-candidates">
                {candidates.data?.containerPaths.map((path) => <option key={path} value={path} />)}
              </datalist>
              <SubmitButton pending={stageDirectory.isPending}>Validate directory</SubmitButton>
            </form>
            {stage?.verdict === 'Staged' && stage.stageId && (
              <div className="confirmation">
                <p><strong>{stage.name}</strong><br /><code>{stage.containerPath}</code></p>
                <button className="primary-button" onClick={() => activate.mutate(stage.stageId!)} disabled={activate.isPending}>Activate validated directory</button>
              </div>
            )}
            {stage && stage.verdict !== 'Staged' && <Notice kind="error">{directoryStageMessage(stage.verdict)}</Notice>}
            {current.libraryDirectories.map((directory) => (
              <div className="configured-directory" key={directory.id}>
                <strong>{directory.name}</strong><code>{directory.containerPath}</code>
              </div>
            ))}
          </div>
        </div>
      )}
      {(configuration.isError || candidates.isError || verify.isError || retry.isError || stageDirectory.isError || activate.isError) && <RequestError />}
    </section>
  )
}

function AccountAdministration({ account }: { account: Account }) {
  const accounts = useQuery({ queryKey: queryKeys.accounts, queryFn: api.accounts })
  const queryClient = useQueryClient()
  const [issuedCode, setIssuedCode] = useState<string>()
  const action = useMutation({
    mutationFn: ({ kind, target }: { kind: 'approve' | 'disable' | 'recover'; target: string }) => {
      if (kind === 'approve') return api.approve(target, account.csrfToken)
      if (kind === 'disable') return api.disable(target, account.csrfToken)
      return api.recoveryCode(target, account.csrfToken)
    },
    onSuccess: (result) => {
      if ('recoveryCode' in result && typeof result.recoveryCode === 'string') {
        setIssuedCode(result.recoveryCode)
      }
      void queryClient.invalidateQueries({ queryKey: queryKeys.accounts })
    },
  })

  return (
    <section className="admin-panel" aria-labelledby="accounts-title">
      <div className="section-heading">
        <div><span className="eyebrow">Administrator</span><h3 id="accounts-title">Account requests</h3></div>
        {accounts.isFetching && <span className="muted">Refreshing…</span>}
      </div>
      {accounts.data?.map((candidate) => (
        <AccountRow
          key={candidate.id}
          account={candidate}
          currentAccountId={account.id}
          pending={action.isPending}
          act={(kind) => action.mutate({ kind, target: candidate.id })}
        />
      ))}
      {issuedCode && <Notice kind="success">One-time recovery code: <code>{issuedCode}</code></Notice>}
      {(accounts.isError || action.isError) && <RequestError />}
    </section>
  )
}

function AccountRow({ account, currentAccountId, pending, act }: {
  account: AccountSummary
  currentAccountId: string
  pending: boolean
  act: (kind: 'approve' | 'disable' | 'recover') => void
}) {
  return (
    <article className="account-row">
      <div><strong>{account.username}</strong><small>{account.authority} · {account.state}</small></div>
      <div className="row-actions">
        {account.state === 'PendingApproval' && <button onClick={() => act('approve')} disabled={pending}>Approve</button>}
        {account.state === 'Approved' && <button onClick={() => act('recover')} disabled={pending}>Recovery code</button>}
        {account.state !== 'Disabled' && account.id !== currentAccountId && <button className="danger-button" onClick={() => act('disable')} disabled={pending}>Disable</button>}
      </div>
    </article>
  )
}

function CenteredCard({ children }: { children: ReactNode }) {
  return <main className="shell"><section className="card">{children}</section></main>
}

function Brand({ compact = false }: { compact?: boolean }) {
  return <div className={compact ? 'brand compact' : 'brand'}><span aria-hidden="true">▶</span><h1>prdb-viewer</h1></div>
}

function Field({ label, ...props }: React.InputHTMLAttributes<HTMLInputElement> & { label: string }) {
  return <label className="field"><span>{label}</span><input {...props} /></label>
}

function Tab({ active, children, onClick }: { active: boolean; children: ReactNode; onClick: () => void }) {
  return <button type="button" className={active ? 'tab active' : 'tab'} aria-pressed={active} onClick={onClick}>{children}</button>
}

function SubmitButton({ pending, children }: { pending: boolean; children: ReactNode }) {
  return <button className="primary-button" type="submit" disabled={pending}>{pending ? 'Working…' : children}</button>
}

function Notice({ kind, children }: { kind: 'error' | 'success'; children: ReactNode }) {
  return <div className={`notice ${kind}`} role={kind === 'error' ? 'alert' : 'status'}>{children}</div>
}

function RequestError() {
  return <Notice kind="error">The request could not be completed. Try again.</Notice>
}

function values<T>(form: HTMLFormElement, keys: string[]): T {
  const data = new FormData(form)
  return Object.fromEntries(keys.map((key) => [key, data.get(key)?.toString() || null])) as T
}

function bootstrapMessage(verdict: string) {
  if (verdict === 'InvalidAuthorization') return 'The one-time authorization is invalid or expired.'
  if (verdict === 'AlreadyClaimed') return 'This installation has already been claimed.'
  return 'Check the authorization and account details.'
}

function signInMessage(verdict: string) {
  if (verdict === 'ApprovalPending') return 'Your request is waiting for Administrator approval.'
  if (verdict === 'Disabled') return 'This account has been disabled.'
  return 'The username or password is incorrect.'
}

function friendlyState(state: string | null | undefined) {
  return (state ?? '').replace(/([a-z])([A-Z])/g, '$1 $2')
}

function directoryStageMessage(verdict: string) {
  if (verdict === 'InvalidName') return 'Give the directory a display name of up to 80 characters.'
  if (verdict === 'InvalidPath') return 'Enter the full container path, starting with a slash.'
  if (verdict === 'OutsideMountArea') return 'Choose a directory beneath the documented library mount area.'
  if (verdict === 'Missing') return 'The directory is not mounted or no longer exists.'
  if (verdict === 'Unreadable') return 'The application identity cannot read this directory.'
  if (verdict === 'AlreadyConfigured') return 'This Library Directory is already active.'
  return 'The directory could not be validated.'
}
