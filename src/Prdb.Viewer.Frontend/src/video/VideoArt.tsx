import type { VideoSummary } from '../api/client'
import {
  formatRuntime,
  highFrameRateLabel,
  qualitySource,
  resolutionLabel,
} from '../lib/quality'

/// A Video's art with what it is worth watching at, and how long it runs, written over it.
///
/// The three belong together and are therefore built in one place: the Library, the personal
/// shelves and the Video's own page all show the same picture, so they all say the same thing
/// about its quality and its runtime rather than each deciding for itself.
export function VideoArt({ video, large = false }: { video: VideoSummary, large?: boolean }) {
  return (
    <div className={large ? 'video-art large' : 'video-art'}>
      {video.previewUrl
        ? (
          <img
            className={large ? 'video-preview large' : 'video-preview'}
            src={video.previewUrl}
            alt=""
            loading={large ? undefined : 'lazy'}
          />
          )
        : (
          <div
            className={large ? 'video-placeholder large' : 'video-placeholder'}
            aria-hidden="true"
          >▶</div>
          )}
      {!large && <RuntimeOverlay video={video} />}
      <QualityOverlay video={video} />
      <ProgressOverlay video={video} />
    </div>
  )
}

/// How long the Video runs, in the corner of the picture where a runtime is looked for.
///
/// It read as prose beneath the title, in the same line as how this browser knew the Video would
/// play, which is where nobody looks for it. The corner opposite the quality is where every video
/// library puts it, so the line beneath the title is free for what only prose can say. The
/// Video's own page states the runtime among its facts, so its larger picture does not repeat it.
function RuntimeOverlay({ video }: { video: VideoSummary }) {
  const source = qualitySource(video)
  const runtime = formatRuntime(Number(source?.durationMilliseconds ?? 0))
  if (!runtime) return null

  return <span className="runtime-badge">{runtime}</span>
}

/// How far this Account got through the Video, drawn over its art.
///
/// A shelf of part-watched Videos is read by how far each bar has moved, which is a glance; the
/// same fact as a timestamp beside a title is arithmetic against a runtime the card does not
/// state. The number stays where it was for anyone who wants it, so this adds a picture rather
/// than replacing a fact.
function ProgressOverlay({ video }: { video: VideoSummary }) {
  const source = qualitySource(video)
  const duration = Number(source?.durationMilliseconds ?? 0)
  const progress = Number(video.personalState.playbackProgressMilliseconds ?? 0)
  if (!(duration > 0) || !(progress > 0)) return null

  return (
    <div className="video-progress" aria-hidden="true">
      <span style={{ width: `${Math.min(100, (progress / duration) * 100)}%` }} />
    </div>
  )
}

/// What quality this Video has here, over the art it describes.
///
/// It reads the occurrence a play action would reach for, so what the corner of the picture
/// promises is what pressing Play delivers rather than the best file the library happens to hold.
/// Where inspection established no dimensions, nothing is claimed at all.
export function QualityOverlay({ video }: { video: VideoSummary }) {
  const source = qualitySource(video)
  if (!source) return null

  const resolution = resolutionLabel(source)
  const frameRate = highFrameRateLabel(source)
  if (!resolution && !frameRate) return null

  return (
    <span className="quality-badges">
      {resolution && <span className="quality-badge">{resolution}</span>}
      {frameRate && <span className="quality-badge">{frameRate}</span>}
    </span>
  )
}
