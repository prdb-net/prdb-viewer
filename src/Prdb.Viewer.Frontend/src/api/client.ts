import type { components } from './schema'

export type AccessState = components['schemas']['AccessStateResponse']
export type Account = components['schemas']['SignedInAccountResponse']
export type AccountSummary = components['schemas']['AccountSummary']
export type BootstrapRequest = components['schemas']['BootstrapRequest']
export type BootstrapResponse = components['schemas']['BootstrapResponse']
export type SignInRequest = components['schemas']['SignInRequest']
export type SignInResponse = components['schemas']['SignInResponse']
export type RegistrationRequest = components['schemas']['RegistrationRequest']
export type RegistrationResponse = components['schemas']['RegistrationRequestResponse']
export type RecoverRequest = components['schemas']['RecoverRequest']
export type RecoverResponse = components['schemas']['RecoverResponse']
export type RecoveryCodeResponse = components['schemas']['RecoveryCodeResponse']
export type AccountActionResponse = components['schemas']['AccountActionResponse']
export type InstallationConfiguration = components['schemas']['InstallationConfigurationSummary']
export type PrdbConnectionUpdate = components['schemas']['PrdbConnectionUpdateResult']
export type LibraryDirectoryCandidates = components['schemas']['LibraryDirectoryCandidatesResponse']
export type LibraryDirectoryStage = components['schemas']['LibraryDirectoryStageResult']
export type LibraryDirectoryActivation = components['schemas']['LibraryDirectoryActivationResult']
export type LibraryDirectoryRemoval = components['schemas']['LibraryDirectoryRemovalResult']
export type LibraryDirectorySummary = components['schemas']['LibraryDirectorySummary']
export type BackgroundWorkStatus = components['schemas']['BackgroundWorkStatus']
export type BackgroundWorkSummary = components['schemas']['BackgroundWorkSummary']
export type WorkIssueSummary = components['schemas']['WorkIssueSummary']
export type WorkIssueAction = components['schemas']['WorkIssueAction']
export type WorkIssueAffectedItem = components['schemas']['WorkIssueAffectedItem']
export type BackgroundWorkActionResult = components['schemas']['BackgroundWorkActionResult']
export type BackgroundWorkPauseResult = components['schemas']['BackgroundWorkPauseResult']
export type QueueLibraryScanResult = components['schemas']['QueueLibraryScanResult']
export type VideoSummary = components['schemas']['VideoSummary']
export type VideoDetail = components['schemas']['VideoDetail']
export type LibraryPage = components['schemas']['LibraryPage']
export type LibraryFacets = components['schemas']['LibraryFacets']
export type VideoQualityBand = components['schemas']['VideoQualityBand']
export type LibrarySortOrder = components['schemas']['LibrarySortOrder']
export type LibraryPreferences = components['schemas']['LibraryPreferencesSummary']
export type PlaybackVariant = components['schemas']['PlaybackVariantView']
export type ClientVideoPlayability = components['schemas']['ClientVideoPlayability']
export type UnassessedPlaybackProfile = components['schemas']['UnassessedPlaybackProfile']
export type ClientPlaybackAssessmentReport = components['schemas']['ClientPlaybackAssessmentReport']
export type PlaybackFailureCategory = components['schemas']['PlaybackFailureCategory']

export type LibraryFilters = {
  query: string
  sort: LibrarySortOrder
  sites: string[]
  actors: string[]
  unknownSite: boolean
  work: string[]
  review: string[]
  playability: string[]
  availability: string[]
  quality: string[]
  playState: string[]
  /// The Personal Shelves to narrow to, by the API's names. A shelf page pins one here; the
  /// browsing screen offers them as a facet like any other.
  shelf: string[]
}

export const emptyFilters: LibraryFilters = {
  query: '',
  sort: 'Newest',
  sites: [],
  actors: [],
  unknownSite: false,
  work: [],
  review: [],
  playability: [],
  availability: [],
  quality: [],
  playState: [],
  shelf: [],
}

/// The narrowing every Library question carries: what the search and the facets admit. The sort
/// order and the page are not part of it, because the facets are counted over the whole match.
function narrowingQuery(filters: LibraryFilters) {
  const parameters = new URLSearchParams()
  if (filters.query.trim()) parameters.set('query', filters.query.trim())
  if (filters.sites.length) parameters.set('sites', filters.sites.join(','))
  if (filters.actors.length) parameters.set('actors', filters.actors.join(','))
  if (filters.unknownSite) parameters.set('unknownSite', 'true')
  if (filters.work.length) parameters.set('work', filters.work.join(','))
  if (filters.review.length) parameters.set('review', filters.review.join(','))
  if (filters.playability.length) parameters.set('playability', filters.playability.join(','))
  if (filters.availability.length) parameters.set('availability', filters.availability.join(','))
  if (filters.quality.length) parameters.set('quality', filters.quality.join(','))
  if (filters.playState.length) parameters.set('playState', filters.playState.join(','))
  if (filters.shelf.length) parameters.set('shelf', filters.shelf.join(','))
  return parameters
}

function libraryQuery(filters: LibraryFilters, skip: number, take: number) {
  const parameters = narrowingQuery(filters)
  parameters.set('sort', filters.sort)
  parameters.set('skip', String(skip))
  parameters.set('take', String(take))
  return parameters.toString()
}
export type PersonalVideoState = components['schemas']['PersonalVideoStateSummary']
export type PlaybackAttempt = components['schemas']['PlaybackAttemptResult']
export type PlaybackReportRequest = components['schemas']['PlaybackReportRequest']
export type PlaybackReport = components['schemas']['PlaybackReportResult']
export type PersonalStateMutation = components['schemas']['PersonalStateMutationResult']
export type IdentificationSummary = components['schemas']['IdentificationSummary']
export type IdentificationQueueItem = components['schemas']['IdentificationQueueItem']
export type IdentificationCase = components['schemas']['IdentificationCase']
export type IdentificationConsequence = components['schemas']['IdentificationConsequence']
export type IdentificationDecisionRequest = components['schemas']['IdentificationDecisionRequest']
export type IdentificationDecisionResult = components['schemas']['IdentificationDecisionResult']
export type IdentificationDecisionAction = components['schemas']['IdentificationDecisionAction']

/// Which browser and device this is. Client Playback Assessments and Observed Playback Outcomes
/// belong to one such context and expire when it materially changes, which is exactly what happens
/// here: a browser update or a different device produces a different key, and the evidence gathered
/// under the old one stops applying. It is derived rather than stored, so it survives no longer
/// than the facts it describes and needs nothing kept on the device.
export const clientContextKey = (() => {
  if (typeof navigator === 'undefined') return 'unqualified'
  const facts = [
    navigator.userAgent,
    // eslint-disable-next-line @typescript-eslint/no-deprecated
    navigator.platform ?? '',
    String(navigator.hardwareConcurrency ?? ''),
    String((navigator as { deviceMemory?: number }).deviceMemory ?? ''),
  ].join('|')
  let hash = 0x811c9dc5
  for (let index = 0; index < facts.length; index++) {
    hash ^= facts.charCodeAt(index)
    hash = Math.imul(hash, 0x01000193) >>> 0
  }
  return `c${hash.toString(16).padStart(8, '0')}`
})()

/// What a refused request said, kept rather than flattened into "it failed".
///
/// The API answers a refusal with a problem document whose detail says what to do about it, and
/// "Refresh the page and try the action again" is not the same instruction as "try again". A
/// screen that replaces the API's words with its own can end up telling the reader to do the one
/// thing that cannot work.
export class RequestFailure extends Error {
  constructor(readonly status: number, readonly detail?: string) {
    super(detail ?? `Request failed with status ${status}.`)
    this.name = 'RequestFailure'
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    credentials: 'same-origin',
    ...init,
    headers: {
      ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
      'X-Client-Context': clientContextKey,
      ...init?.headers,
    },
  })

  if (!response.ok) {
    throw new RequestFailure(response.status, await problemDetail(response))
  }

  return response.json() as Promise<T>
}

/// The instruction a problem document carries, where the answer is one and it has anything to say.
async function problemDetail(response: Response): Promise<string | undefined> {
  try {
    const problem: unknown = await response.json()
    const detail = (problem as { detail?: unknown } | null)?.detail
    return typeof detail === 'string' && detail.trim() ? detail.trim() : undefined
  } catch {
    return undefined
  }
}

function post<T>(path: string, body?: unknown, csrfToken?: string) {
  return request<T>(path, {
    method: 'POST',
    body: body === undefined ? undefined : JSON.stringify(body),
    headers: csrfToken ? { 'X-CSRF-Token': csrfToken } : undefined,
  })
}

function mutate<T>(path: string, method: 'PUT' | 'DELETE', csrfToken: string, body?: unknown) {
  return request<T>(path, {
    method,
    body: body === undefined ? undefined : JSON.stringify(body),
    headers: { 'X-CSRF-Token': csrfToken },
  })
}

export const api = {
  state: () => request<AccessState>('/api/access/state'),
  videos: (filters: LibraryFilters, skip = 0, take = 60) =>
    request<LibraryPage>(`/api/library/videos?${libraryQuery(filters, skip, take)}`),
  video: (videoId: string) => request<VideoDetail>(`/api/library/videos/${videoId}`),
  libraryFacets: (filters: LibraryFilters) =>
    request<LibraryFacets>(`/api/library/facets?${narrowingQuery(filters).toString()}`),
  setIncludeNotReady: (included: boolean, csrfToken: string) =>
    mutate<LibraryPreferences>(
      '/api/library/preferences/include-not-ready',
      'PUT',
      csrfToken,
      { included },
    ),
  unassessedPlaybackProfiles: () =>
    request<UnassessedPlaybackProfile[]>('/api/personal/playback-profiles'),
  recordPlaybackAssessments: (
    assessments: ClientPlaybackAssessmentReport[],
    csrfToken: string,
  ) => mutate<{ recorded: number }>(
    '/api/personal/playback-assessments',
    'PUT',
    csrfToken,
    { assessments },
  ),
  recordPlaybackOutcome: (
    videoFileId: string,
    outcome: 'Succeeded' | 'Failed',
    failureCategory: PlaybackFailureCategory | null,
    csrfToken: string,
  ) => post<{ recorded: boolean }>(
    '/api/personal/playback-outcomes',
    { videoFileId, outcome, failureCategory },
    csrfToken,
  ),
  forgetPlaybackOutcomes: (videoId: string, csrfToken: string) =>
    mutate<{ recorded: boolean }>(
      `/api/personal/videos/${videoId}/playback-outcomes`,
      'DELETE',
      csrfToken,
    ),
  startPlaybackAttempt: (videoId: string, videoFileId: string, csrfToken: string) =>
    post<PlaybackAttempt>(
      `/api/personal/videos/${videoId}/playback-attempts`,
      { videoFileId },
      csrfToken,
    ),
  reportPlayback: (
    playbackAttemptId: string,
    report: PlaybackReportRequest,
    csrfToken: string,
  ) => post<PlaybackReport>(
    `/api/personal/playback-attempts/${playbackAttemptId}/reports`,
    report,
    csrfToken,
  ),
  endPlaybackAttempt: (playbackAttemptId: string, csrfToken: string, keepalive = false) =>
    request<{ ended: boolean }>(
      `/api/personal/playback-attempts/${playbackAttemptId}/end`,
      {
        method: 'POST',
        headers: { 'X-CSRF-Token': csrfToken },
        keepalive,
      },
    ),
  setFavourite: (videoId: string, selected: boolean, csrfToken: string) =>
    mutate<PersonalStateMutation>(
      `/api/personal/videos/${videoId}/favourite`,
      selected ? 'PUT' : 'DELETE',
      csrfToken,
    ),
  setWatchLater: (videoId: string, selected: boolean, csrfToken: string) =>
    mutate<PersonalStateMutation>(
      `/api/personal/videos/${videoId}/watch-later`,
      selected ? 'PUT' : 'DELETE',
      csrfToken,
    ),
  setRating: (videoId: string, rating: number | null, csrfToken: string) =>
    rating === null
      ? mutate<PersonalStateMutation>(
          `/api/personal/videos/${videoId}/rating`,
          'DELETE',
          csrfToken,
        )
      : mutate<PersonalStateMutation>(
          `/api/personal/videos/${videoId}/rating`,
          'PUT',
          csrfToken,
          { rating },
        ),
  dismissContinueWatching: (videoId: string, csrfToken: string) =>
    post<PersonalStateMutation>(
      `/api/personal/videos/${videoId}/continue-watching/dismiss`,
      undefined,
      csrfToken,
    ),
  me: () => request<Account>('/api/access/me'),
  bootstrap: (input: BootstrapRequest) => post<BootstrapResponse>('/api/access/bootstrap', input),
  signIn: (input: SignInRequest) => post<SignInResponse>('/api/access/sign-in', input),
  register: (input: RegistrationRequest) =>
    post<RegistrationResponse>('/api/access/registration-requests', input),
  recover: (input: RecoverRequest) => post<RecoverResponse>('/api/access/recover', input),
  accounts: () => request<AccountSummary[]>('/api/admin/accounts/'),
  approve: (accountId: string, csrfToken: string) =>
    post<AccountActionResponse>(`/api/admin/accounts/${accountId}/approve`, undefined, csrfToken),
  disable: (accountId: string, csrfToken: string) =>
    post<AccountActionResponse>(`/api/admin/accounts/${accountId}/disable`, undefined, csrfToken),
  reinstate: (accountId: string, csrfToken: string) =>
    post<AccountActionResponse>(`/api/admin/accounts/${accountId}/reinstate`, undefined, csrfToken),
  recoveryCode: (accountId: string, csrfToken: string) =>
    post<RecoveryCodeResponse>(`/api/admin/accounts/${accountId}/recovery-code`, undefined, csrfToken),
  configuration: () => request<InstallationConfiguration>('/api/admin/configuration/'),
  verifyPrdb: (credential: string, csrfToken: string) =>
    post<PrdbConnectionUpdate>('/api/admin/configuration/prdb-connection', { credential }, csrfToken),
  retryPrdb: (csrfToken: string) =>
    post<PrdbConnectionUpdate>('/api/admin/configuration/prdb-connection/retry', undefined, csrfToken),
  libraryDirectoryCandidates: () =>
    request<LibraryDirectoryCandidates>('/api/admin/configuration/library-directory-candidates'),
  stageLibraryDirectory: (name: string, containerPath: string, csrfToken: string) =>
    post<LibraryDirectoryStage>(
      '/api/admin/configuration/library-directories/stages',
      { name, containerPath },
      csrfToken,
    ),
  activateLibraryDirectory: (stageId: string, csrfToken: string) =>
    post<LibraryDirectoryActivation>(
      `/api/admin/configuration/library-directories/stages/${stageId}/activate`,
      undefined,
      csrfToken,
    ),

  removeLibraryDirectory: (libraryDirectoryId: string, csrfToken: string) =>
    mutate<LibraryDirectoryRemoval>(
      `/api/admin/configuration/library-directories/${libraryDirectoryId}`,
      'DELETE',
      csrfToken,
    ),
  identificationQueue: () =>
    request<IdentificationQueueItem[]>('/api/admin/identification/queue'),
  identificationCase: (videoId: string) =>
    request<IdentificationCase>(`/api/admin/identification/videos/${videoId}`),
  decideIdentification: (
    videoId: string,
    decision: IdentificationDecisionRequest,
    csrfToken: string,
  ) => post<IdentificationDecisionResult>(
    `/api/admin/identification/videos/${videoId}/decisions`,
    decision,
    csrfToken,
  ),
  backgroundWork: () => request<BackgroundWorkStatus>('/api/admin/background-work/'),
  workIssueItems: (workIssueId: string) =>
    request<WorkIssueAffectedItem[]>(
      `/api/admin/background-work/issues/${workIssueId}/items`,
    ),
  advanceWorkIssue: (
    workIssueId: string,
    action: WorkIssueAction,
    version: number | string,
    csrfToken: string,
  ) => post<BackgroundWorkActionResult>(
    `/api/admin/background-work/issues/${workIssueId}/actions`,
    { action, version },
    csrfToken,
  ),
  pauseBackgroundWork: (paused: boolean, csrfToken: string) =>
    post<BackgroundWorkPauseResult>(
      '/api/admin/background-work/pause',
      { paused },
      csrfToken,
    ),
  cancelBackgroundWork: (workId: string, csrfToken: string) =>
    post<BackgroundWorkActionResult>(
      `/api/admin/background-work/${workId}/cancel`,
      undefined,
      csrfToken,
    ),
  queueLibraryScan: (libraryDirectoryId: string, csrfToken: string) =>
    post<QueueLibraryScanResult>(
      `/api/admin/background-work/library-directories/${libraryDirectoryId}/scans`,
      undefined,
      csrfToken,
    ),
  signOut: async (csrfToken: string) => {
    const response = await fetch('/api/access/sign-out', {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'X-CSRF-Token': csrfToken },
    })

    if (!response.ok) {
      throw new Error(`Request failed with status ${response.status}.`)
    }
  },
}
