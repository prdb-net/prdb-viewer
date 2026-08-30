import type { PlaybackVariant, VideoQualityBand, VideoSummary } from '../api/client'
import { friendlyState, playableSource, variantReason } from './format'

/// What a Video File is worth watching at, in the words a person uses for it.
///
/// The installation bands resolution twice, for two different questions. The Playback Profile Key
/// bands it so that files putting the same question to a browser share one Client Playback
/// Assessment; Video File Quality bands it so that a person can be told what they would be
/// watching. `fullhd` is the first. `1080p` is the second.

/// The name of a Video File Quality band.
///
/// The band itself is decided in the Core and travels on the wire, so what a card claims and what
/// the Library filtered by cannot disagree. All that is left here is what to call it.
export function qualityBandLabel(band: VideoQualityBand | null | undefined) {
  if (band === 'Uhd4320') return '8K'
  if (band === 'Uhd2160') return '4K'
  if (band === 'Qhd1440') return '1440p'
  if (band === 'FullHd1080') return '1080p'
  if (band === 'Hd720') return '720p'
  if (band === 'StandardDefinition') return 'SD'

  return undefined
}

/// The resolution a Video File would be advertised at, or nothing where inspection established no
/// dimensions to name one from.
export function resolutionLabel(file: PlaybackVariant) {
  return qualityBandLabel(file.qualityBand)
}

/// The frame rate, where it is one worth naming. Everything up to thirty frames is what video
/// ordinarily is, so stating it tells nobody anything; above that is the fact being looked for.
export function highFrameRateLabel(file: PlaybackVariant) {
  const rate = Number(file.frameRate ?? 0)

  return rate > 30.5 ? `${Math.round(rate)} fps` : undefined
}

/// The one line that answers "what quality is this": the resolution, and the frame rate when it is
/// remarkable. Nothing when inspection established neither.
export function qualityLabel(file: PlaybackVariant) {
  const parts = [resolutionLabel(file), highFrameRateLabel(file)].filter(Boolean)

  return parts.length > 0 ? parts.join(' · ') : undefined
}

/// A bitrate as a person reads it, or nothing where inspection did not establish one.
export function formatBitrate(bitsPerSecond: number) {
  if (!(bitsPerSecond > 0)) {
    return undefined
  }

  return bitsPerSecond >= 1_000_000
    ? `${decimal(bitsPerSecond / 1_000_000)} Mbit/s`
    : `${Math.round(bitsPerSecond / 1_000)} kbit/s`
}

/// A file size in the decimal units the storage it sits on is sold in, so that what the library
/// says about a file matches what the host says about it.
export function formatSize(bytes: number) {
  if (!(bytes > 0)) {
    return undefined
  }

  const units = ['bytes', 'kB', 'MB', 'GB', 'TB']
  let value = bytes
  let unit = 0

  while (value >= 1_000 && unit < units.length - 1) {
    value /= 1_000
    unit += 1
  }

  return unit === 0 ? `${value} bytes` : `${decimal(value)} ${units[unit]}`
}

/// A channel count as the layout everybody names it by: `5.1` is what people call six channels,
/// and `6` is what nobody calls it.
export function channelLayout(channels: number) {
  if (channels === 1) return 'Mono'
  if (channels === 2) return 'Stereo'
  if (channels === 6) return '5.1'
  if (channels === 8) return '7.1'

  return `${channels} channels`
}

/// The audio in one line: what it is encoded as, how it is laid out, and how finely it was
/// sampled — each part only where inspection established it.
export function audioSummary(file: PlaybackVariant) {
  if (!file.audioCodec) {
    return undefined
  }

  const channels = Number(file.audioChannels ?? 0)
  const sampleRate = Number(file.audioSampleRate ?? 0)
  const parts = [
    file.audioCodec,
    channels > 0 ? channelLayout(channels) : undefined,
    sampleRate > 0 ? `${decimal(sampleRate / 1_000)} kHz` : undefined,
  ].filter(Boolean)

  return parts.join(' · ')
}

/// How long the Video runs, counted in hours where it runs that long. This is the file's own
/// duration rather than anybody's progress through it, so it is the same fact for every Account.
export function formatRuntime(milliseconds: number) {
  if (!(milliseconds > 0)) {
    return undefined
  }

  // Below a minute the seconds are the honest unit. Rounding a clip to minutes printed "0 min",
  // which is not a runtime any file has, next to a Progress line counting the seconds it does.
  if (milliseconds < 60_000) {
    return `${Math.max(1, Math.round(milliseconds / 1_000))} s`
  }

  const minutes = Math.round(milliseconds / 60_000)
  const hours = Math.floor(minutes / 60)

  return hours > 0 ? `${hours} h ${minutes % 60} min` : `${minutes} min`
}

/// What is worth stating about one Video File beyond the name of its format, in the order somebody
/// weighing it up asks for it. A fact inspection could not establish is left out rather than
/// printed as a blank, because an absent line is honest and an empty one is not.
export function qualityFacts(file: PlaybackVariant) {
  const facts: { label: string, value: string }[] = []
  const add = (label: string, value: string | undefined) => {
    if (value) facts.push({ label, value })
  }

  add('Quality', qualityLabel(file))
  add('Runtime', formatRuntime(Number(file.durationMilliseconds ?? 0)))
  add('Bitrate', formatBitrate(Number(file.bitrate ?? 0)))
  add('Audio', audioSummary(file))
  add('Size', formatSize(Number(file.size ?? 0)))

  return facts
}

/// What a card says beneath the title: how long the Video runs, and how this browser knows it
/// would play.
///
/// It used to open with the container and codecs — `mov,mp4,m4a,3gp,3g2,mj2 (h264 + aac)` — which
/// is what the file is made of rather than anything somebody browsing is asking, and it was long
/// enough to push the rest of the line out of the card. What a Video would be watched at is in the
/// corner of the picture, what it is made of is on its own page against every occurrence, and this
/// is the one thing neither of those says: how long it takes and whether it will play here.
export function playbackSummary(video: VideoSummary, source: PlaybackVariant | undefined) {
  if (!source) return friendlyState(video.availability)

  const runtime = formatRuntime(Number(source.durationMilliseconds ?? 0))
  return [runtime, variantReason(source)].filter(Boolean).join(' · ')
}

/// The occurrence whose quality is this Video's quality here: the one a play action would reach
/// for. Where this client has ruled every occurrence out, the best-ranked one still says what the
/// Video is — which is exactly what somebody deciding to open it on another device needs to know.
export function qualitySource(video: VideoSummary): PlaybackVariant | undefined {
  return playableSource(video) ?? video.videoFiles[0]
}

/// A number with at most one decimal, so that a size or a rate reads as a fact rather than as the
/// full precision of a division nobody asked for.
function decimal(value: number) {
  return value.toLocaleString(undefined, { maximumFractionDigits: 1 })
}
