import type { VideoSummary } from '../api/client'
import { highFrameRateLabel, qualitySource, resolutionLabel } from '../lib/quality'

/// A Video's art with what it is worth watching at written over it.
///
/// The two belong together and are therefore built in one place: the Library, the personal shelves
/// and the Video's own page all show the same picture, so they all say the same thing about its
/// quality rather than each deciding for itself.
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
      <QualityOverlay video={video} />
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
