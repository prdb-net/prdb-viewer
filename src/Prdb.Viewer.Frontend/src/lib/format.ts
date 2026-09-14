import type { PlaybackVariant, VideoSummary } from '../api/client'

export function friendlyState(state: string | null | undefined) {
  return (state ?? '').replace(/([a-z])([A-Z])/g, '$1 $2')
}

/// Client Video Playability in the words the domain has for it.
///
/// Splitting the enum on its capitals gave "Ready For Direct Play", which is nobody's name for
/// that state: CONTEXT.md calls it Ready for Direct Play, and a screen that invents its own
/// capitalisation of a defined term teaches the reader a second vocabulary for one thing.
export function playabilityLabel(playability: string | null | undefined) {
  if (playability === 'ReadyForDirectPlay') return 'Ready for Direct Play'
  if (playability === 'CompatibilityUncertain') return 'Compatibility Uncertain'
  if (playability === 'NotDirectlyPlayable') return 'Not Directly Playable'
  return friendlyState(playability)
}

/// What state an Account is in, said rather than spelled.
///
/// The enum reached the screen unbroken as "PendingApproval" beside an "Approved" that needed no
/// translation, so the one row an Administrator is there to act on was the one row written in the
/// database's own words.
export function accountStateLabel(state: string | null | undefined) {
  if (state === 'PendingApproval') return 'Waiting for approval'
  return friendlyState(state)
}

export function formatDuration(milliseconds: number) {
  const totalSeconds = Math.floor(milliseconds / 1_000)
  return `${Math.floor(totalSeconds / 60)}:${(totalSeconds % 60).toString().padStart(2, '0')}`
}

/// What a Video File is, in the words its container has rather than the ones its inspector uses.
///
/// `containerFormat` is `ffprobe`'s list of every format its demuxer accepts — `matroska,webm`,
/// `mov,mp4,m4a,3gp,3g2,mj2` — and printing it taught the reader a second vocabulary for one
/// thing. The Matroska list was the worse half of it: it ends in the name of the container
/// browsers do read, so a sentence saying that file cannot be played appeared to say the opposite.
export function fileFormat(file: PlaybackVariant) {
  const codecs = [file.videoCodec, file.audioCodec].filter(Boolean).join(' + ')
  return `${file.containerName} (${codecs})`
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
  // An Unsupported file used to read "not assessed yet", which promised an assessment no client
  // was ever going to be asked for: the installation settled this from the file's own bytes.
  if (variant.selectionReason === 'NoBrowserPath') return 'no browser path'
  return 'not assessed yet'
}

/// Why a Video has no Play action. It distinguishes the installation-wide case — every occurrence
/// is statically Unsupported — from this client having ruled them out, because those are different
/// facts and only one of them is about the files.
export function playbackUnavailableReason(video: VideoSummary) {
  if (video.videoFiles.length === 0) {
    return 'No Video File of this Video is currently available.'
  }

  if (!video.isUnsupportedVideo) {
    const formats = Array.from(new Set(video.videoFiles.map(fileFormat))).join(' or ')
    return `This browser did not play ${formats}. Another browser or device may still play it.`
  }

  // Said once per distinct obstacle, because two occurrences of one Video usually have the same
  // one and repeating it would be the sentence written twice.
  const obstacles = Array.from(new Set(video.videoFiles.map(directPlayObstacle)))

  // The product's boundary is one short sentence at the end rather than the whole explanation:
  // "needs conversion, which this product deliberately does not do" answered a question about the
  // product where the reader had asked one about their file.
  return `${obstacles.join(' ')} This product does not convert video, so the file is offered as ` +
    'it is or not at all.'
}

/// Which part of one Video File's configuration has no path to a browser, said so that a reader
/// knows what would have to change. The codecs and the container are different problems with
/// different answers, and collapsing both into "needs conversion" hid the one that is actionable.
function directPlayObstacle(file: PlaybackVariant) {
  const codecs = [file.videoCodec, file.audioCodec].filter(Boolean).join(' and ')

  if (file.directPlayObstacle === 'Container') {
    return `No browser reads the ${file.containerName} container. Its ${codecs} streams are ones ` +
      'browsers do play, so the container alone is what stands in the way.'
  }

  if (file.directPlayObstacle === 'Codecs') {
    return `No supported browser decodes ${codecs}, whichever container carries them.`
  }

  if (file.directPlayObstacle === 'ContainerAndCodecs') {
    return `Neither the ${file.containerName} container nor its ${codecs} streams has a path to ` +
      'a browser.'
  }

  return `Inspection did not establish enough about this ${file.containerName} file to say what ` +
    'a browser would make of it.'
}

/// Why a Video is shown with a placeholder instead of a picture.
///
/// A preview still to be generated and one that could not be produced were the same grey `▶` and
/// the same silence, and they are opposite facts. The Administrator had the distinction in a Work
/// Issue; the person looking at the missing picture had nothing at all.
export function missingPreviewReason(state: string | null | undefined) {
  if (state === 'Pending') return 'A preview for this Video has not been generated yet.'
  if (state === 'NoFrame') {
    return 'No frame could be read from this Video’s files, so it has no preview. Playback is ' +
      'unaffected.'
  }
  if (state === 'Unreachable') {
    return 'No preview could be made while this Video’s files cannot be read.'
  }
  return undefined
}

/// The same fact in the words a placeholder can carry, for a card that has no room for a sentence
/// and for a screen reader, which was told nothing whatever by an `aria-hidden` triangle.
export function missingPreviewLabel(state: string | null | undefined) {
  if (state === 'Pending') return 'Preview not generated yet'
  if (state === 'NoFrame') return 'No preview could be made from this Video'
  if (state === 'Unreachable') return 'No preview while this Video cannot be read'
  return 'No preview'
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

/// When a Library Directory is next read without anyone asking for it.
///
/// A due time that has passed is not "an hour ago": the Scan it names has not run yet. It is due,
/// and it stays due for as long as background work is paused, so saying it in the past tense would
/// describe a Scan that never happened.
export function nextScanDue(value: string | null | undefined, now: number = Date.now()) {
  if (!value) return undefined

  const at = Date.parse(value)
  if (Number.isNaN(at)) return undefined

  return at <= now ? 'now' : timeAgo(value, now)
}

/// A day, named rather than numbered.
///
/// `toLocaleDateString()` with nothing said gives the browser's own order — `9/4/2026`, which is
/// the fourth of September to one reader and the ninth of April to the next, and the application
/// is in English wherever it runs. A month written as a word cannot be read the wrong way round.
export function formatDay(value: string | null | undefined) {
  if (!value) return undefined

  const at = new Date(value)
  if (Number.isNaN(at.getTime())) return undefined

  return at.toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' })
}

/// The instant itself, for the title a relative time carries.
export function exactTime(value: string | null | undefined) {
  if (!value) return undefined
  const at = new Date(value)
  return Number.isNaN(at.getTime()) ? undefined : at.toLocaleString()
}
