import type { PlaybackVariant, VideoSummary } from '../api/client'

export function friendlyState(state: string | null | undefined) {
  return (state ?? '').replace(/([a-z])([A-Z])/g, '$1 $2')
}

export function formatDuration(milliseconds: number) {
  const totalSeconds = Math.floor(milliseconds / 1_000)
  return `${Math.floor(totalSeconds / 60)}:${(totalSeconds % 60).toString().padStart(2, '0')}`
}

export function fileFormat(file: PlaybackVariant) {
  const codecs = [file.videoCodec, file.audioCodec].filter(Boolean).join(' + ')
  return `${file.containerFormat} (${codecs})`
}

/// How one variant came to its place in the order, in the User's words.
export function variantReason(variant: PlaybackVariant) {
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

/// What a card says about playback beneath the title: the file this client would play and how it
/// knows. It states evidence rather than promising an outcome.
export function playbackSupport(video: VideoSummary, source: PlaybackVariant | undefined) {
  if (!source) return friendlyState(video.availability)
  return `${fileFormat(source)} · ${variantReason(source)}`
}

/// Why a Video has no Play action. It distinguishes the installation-wide case — every occurrence
/// is statically Unsupported — from this client having ruled them out, because those are different
/// facts and only one of them is about the files.
export function playbackUnavailableReason(video: VideoSummary) {
  if (video.videoFiles.length === 0) {
    return 'No Video File of this Video is currently available.'
  }
  const formats = Array.from(new Set(video.videoFiles.map(fileFormat))).join(' or ')
  return video.isUnsupportedVideo
    ? `Not directly playable: ${formats} needs conversion, which this product deliberately does not do.`
    : `This browser did not play ${formats}. Another browser or device may still play it.`
}

/// The playable occurrence a play action would reach for, which is also what decides whether the
/// card offers to play at all.
export function playableSource(video: VideoSummary) {
  return video.videoFiles.find((variant) => variant.selectionReason !== 'RuledOutHere')
}

export function bootstrapMessage(verdict: string) {
  if (verdict === 'InvalidAuthorization') return 'The one-time authorization is invalid or expired.'
  if (verdict === 'AlreadyClaimed') return 'This installation has already been claimed.'
  return 'Check the authorization and account details.'
}

export function signInMessage(verdict: string) {
  if (verdict === 'ApprovalPending') return 'Your request is waiting for Administrator approval.'
  if (verdict === 'Disabled') return 'This account has been disabled.'
  return 'The username or password is incorrect.'
}

export function directoryStageMessage(verdict: string) {
  if (verdict === 'InvalidName') return 'Give the directory a display name of up to 80 characters.'
  if (verdict === 'InvalidPath') return 'Enter the full container path, starting with a slash.'
  if (verdict === 'OutsideMountArea') return 'Choose a directory beneath the documented library mount area.'
  if (verdict === 'Missing') return 'The directory is not mounted or no longer exists.'
  if (verdict === 'Unreadable') return 'The application identity cannot read this directory.'
  if (verdict === 'AlreadyConfigured') return 'This Library Directory is already active.'
  return 'The directory could not be validated.'
}

/// A recognised Site says where it came from, because a name read out of a file's own path is not
/// the same knowledge as one prdb established.
export function siteProvenanceLabel(source: string | null | undefined) {
  if (source === 'PrdbIdentification') return 'from prdb'
  if (source === 'AdministratorDecision') return 'set by an Administrator'
  if (source === 'LocalInference') return 'recognised locally'
  return 'established'
}

export function provenanceLabel(source: string | null | undefined) {
  if (source === 'PrdbIdentification') return 'prdb match'
  if (source === 'AdministratorDecision') return 'Administrator assignment'
  if (source === 'LocalInference') return 'Local inference'
  return 'Established'
}

/// Where a proposal came from, in the queue's own line, so an Administrator can tell a remote
/// proposal from one read out of a file's path before opening the case.
export function candidateOrigin(source: string | null | undefined) {
  return source === 'LocalInference' ? 'from the file’s own path' : 'from prdb'
}

/// When something happened, in words rather than a timestamp to do arithmetic on.
///
/// An operator looking at a lane wants one thing from a time: whether this is the run that just
/// happened or one from yesterday. The exact instant stays available as the element's title.
export function timeAgo(value: string | null | undefined, now: number = Date.now()) {
  if (!value) return undefined

  const at = Date.parse(value)
  if (Number.isNaN(at)) return undefined

  const seconds = Math.round((at - now) / 1_000)
  const relative = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' })
  const units: [Intl.RelativeTimeFormatUnit, number][] = [
    ['second', 60],
    ['minute', 60],
    ['hour', 24],
    ['day', 7],
    ['week', 4.35],
    ['month', 12],
  ]

  let amount = seconds
  for (const [unit, next] of units) {
    if (Math.abs(amount) < next) return relative.format(Math.round(amount), unit)
    amount /= next
  }

  return relative.format(Math.round(amount), 'year')
}

/// The instant itself, for the title a relative time carries.
export function exactTime(value: string | null | undefined) {
  if (!value) return undefined
  const at = new Date(value)
  return Number.isNaN(at.getTime()) ? undefined : at.toLocaleString()
}
