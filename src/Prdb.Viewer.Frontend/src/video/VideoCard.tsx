import { Link } from 'react-router'

import type { VideoSummary } from '../api/client'
import {
  formatDuration,
  friendlyState,
  playbackSupport,
  playbackUnavailableReason,
  playableSource,
} from '../lib/format'
import type { PersonalAction } from '../personal/usePersonalActions'
import { Provenance } from './Provenance'

/// One Video as the Library shows it.
///
/// The card decides nothing about playback: it links to the Video, and the Video's own page owns
/// the play action, the order the variants are tried in, and what a failure means. That is what
/// makes a Video addressable — the same screen whether it was reached from a shelf, a search, or
/// a link somebody sent.
export function VideoCard({ video, act, pending, dismissible = false }: {
  video: VideoSummary
  act: PersonalAction
  pending: boolean
  dismissible?: boolean
}) {
  const source = playableSource(video)
  const progress = Number(video.personalState.playbackProgressMilliseconds ?? 0)
  const resume = progress > 0 && video.personalState.playState === 'InProgress'
  const playable = source !== undefined && video.playability !== 'NotDirectlyPlayable'

  return (
    <article className="video-card">
      <Link to={`/videos/${video.id}`} className="video-link">
        {video.previewUrl
          ? <img className="video-preview" src={video.previewUrl} alt="" loading="lazy" />
          : <div className="video-placeholder" aria-hidden="true">▶</div>}
        <strong className="video-title">{video.displayTitle}</strong>
      </Link>
      <div className="card-facts">
        <small>{playbackSupport(video, source)}</small>
        <Provenance identification={video.identification} />
        {video.personalState.playState !== 'Unplayed' && (
          <div className="play-state">
            <span>{friendlyState(video.personalState.playState)}</span>
            {progress > 0 && <span>{formatDuration(progress)}</span>}
            <span>{Number(video.personalState.playCount)} plays</span>
          </div>
        )}
      </div>
      <div className="card-actions">
        {playable && (
          <Link
            className={video.playability === 'CompatibilityUncertain'
              ? 'primary-button uncertain'
              : 'primary-button'}
            to={`/videos/${video.id}?play=1`}
          >
            {video.playability === 'CompatibilityUncertain'
              ? 'Try Direct Play'
              : resume ? 'Resume' : 'Play'}
          </Link>
        )}
        {!playable && <span className="unsupported">{playbackUnavailableReason(video)}</span>}
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
        {dismissible && (
          <button className="dismiss-button" onClick={() => act('dismiss', video)} disabled={pending}>
            Dismiss
          </button>
        )}
      </div>
    </article>
  )
}

export function VideoGrid({ videos, act, pending, dismissible = false }: {
  videos: VideoSummary[]
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
          act={act}
          pending={pending}
          dismissible={dismissible}
        />
      ))}
    </div>
  )
}
