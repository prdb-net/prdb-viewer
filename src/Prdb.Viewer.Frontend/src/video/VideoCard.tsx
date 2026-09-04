import { Link } from 'react-router'

import type { VideoSummary } from '../api/client'
import {
  formatDay,
  formatDuration,
  friendlyState,
  playbackUnavailableReason,
  playableSource,
  siteProvenanceLabel,
} from '../lib/format'
import { StarRating } from '../personal/StarRating'
import type { PersonalAction, PersonalPending } from '../personal/usePersonalActions'
import { VideoArt } from './VideoArt'

/// One Video as the Library shows it.
///
/// The card decides nothing about playback: it links to the Video, and the Video's own page owns
/// the play action, the order the variants are tried in, and what a failure means. That is what
/// makes a Video addressable — the same screen whether it was reached from a shelf, a search, or
/// a link somebody sent.
///
/// It says what is worth saying about this Video and nothing that is true of every Video. Forty
/// cards each carrying "prdb match" and "expected to play smoothly here" said those things forty
/// times and distinguished nothing; the ordinary case is now silent, and what a card states is an
/// exception — an Unknown Video, a review, a Site recognised only locally, a file that will not
/// play here.
export function VideoCard({ video, act, pending, dismissible = false }: {
  video: VideoSummary
  act: PersonalAction
  pending: PersonalPending
  dismissible?: boolean
}) {
  // This card is busy only while one of its own actions is in flight, not while any card's is.
  const busy = pending(video.id)
  const source = playableSource(video)
  const progress = Number(video.personalState.playbackProgressMilliseconds ?? 0)
  const resume = progress > 0 && video.personalState.playState === 'InProgress'
  const playable = source !== undefined && video.playability !== 'NotDirectlyPlayable'
  const rating = video.personalState.personalRating
  const rated = rating !== null && rating !== undefined
  const kept = video.personalState.favourite || video.personalState.watchLater

  return (
    <article className="video-card">
      <Link to={`/videos/${video.id}`} className="video-link">
        <VideoArt video={video} />
        <strong className="video-title">{video.displayTitle}</strong>
      </Link>
      {/* Favourite and Watch Later sit on the picture, where a thumb or a pointer finds them, and
          show themselves on hover, on focus, wherever there is no pointer to hover with, and
          whenever one of them is set — a card that is a Favourite says so without being asked. */}
      <div className={kept ? 'art-actions pinned' : 'art-actions'}>
        <button
          className={video.personalState.favourite ? 'art-action selected' : 'art-action'}
          aria-pressed={video.personalState.favourite}
          aria-label="Favourite"
          title="Favourite"
          onClick={() => act('favourite', video, !video.personalState.favourite)}
          disabled={busy}
        >
          <HeartIcon />
        </button>
        <button
          className={video.personalState.watchLater ? 'art-action selected' : 'art-action'}
          aria-pressed={video.personalState.watchLater}
          aria-label="Watch Later"
          title="Watch Later"
          onClick={() => act('watch-later', video, !video.personalState.watchLater)}
          disabled={busy}
        >
          <BookmarkIcon />
        </button>
      </div>
      <div className="card-facts">
        <CardFacts video={video} source={source} />
      </div>
      <div className="card-actions">
        {/* A Personal Rating is shown where there is one, and can be changed where it is shown.
            Five empty stars on every unrated card were a control nobody had asked for, forty
            times over; the Video's own page is where a first rating is given. It sits above the
            play action so that the play actions of a row stay level whether or not a card carries
            a rating. */}
        {rated && (
          <StarRating
            title={video.displayTitle}
            value={rating}
            onChange={(score) => act('rating', video, score)}
            disabled={busy}
          />
        )}
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
        {dismissible && (
          <button className="dismiss-button" onClick={() => act('dismiss', video)} disabled={busy}>
            Dismiss
          </button>
        )}
      </div>
    </article>
  )
}

/// What the card says beneath the title: where the Video is from and who is in it, and then only
/// what is exceptional about it.
///
/// A title that is nothing but the Actors' names — which is what an Established Work is often
/// called — is not repeated as the Actors' names beneath it; the Site and the Discovery Date take
/// that line instead, because they are the two facts such a title leaves out.
function CardFacts({ video, source }: {
  video: VideoSummary
  source: ReturnType<typeof playableSource>
}) {
  const identification = video.identification
  const site = identification?.site
  const work = identification?.work
  const actors = identification?.actors ?? []
  const siteName = site?.resolution === 'Established' && site.targetTitle
    ? site.source === 'PrdbIdentification'
      ? site.targetTitle
      : `${site.targetTitle} · ${siteProvenanceLabel(site.source)}`
    : undefined
  const review = work?.reviewStatus === 'ReviewNeeded' || site?.reviewStatus === 'ReviewNeeded'
  const flags = [
    work && work.resolution !== 'Established' ? { kind: 'unknown', label: 'Unknown Video' } : undefined,
    review ? { kind: 'review', label: 'Review needed' } : undefined,
  ].filter((flag) => flag !== undefined)
  const line = titleNamesActors(video.displayTitle, actors)
    ? [siteName, formatDay(video.discoveryDate)]
    : [siteName, actors.length > 0 ? actors.join(', ') : undefined]
  const state = video.personalState.playState
  const progress = Number(video.personalState.playbackProgressMilliseconds ?? 0)

  return (
    <>
      {line.some(Boolean) && (
        <small className="card-line">
          {line.filter((part) => part !== undefined).map((part) => <span key={part}>{part}</span>)}
        </small>
      )}
      {!source && <small className="card-line"><span>{friendlyState(video.availability)}</span></small>}
      {flags.length > 0 && (
        <div className="card-flags">
          {flags.map((flag) => <span key={flag.label} className={`badge ${flag.kind}`}>{flag.label}</span>)}
        </div>
      )}
      {state !== 'Unplayed' && (
        <small className="card-line play-state">
          <span>{friendlyState(state)}</span>
          {state === 'InProgress' && progress > 0 && <span>{formatDuration(progress)}</span>}
        </small>
      )}
    </>
  )
}

/// Whether a title says nothing the Actors' names do not: "Alex Doe", or "Alex Doe And Sam Roe".
function titleNamesActors(title: string, actors: string[]) {
  if (actors.length === 0) return false

  let rest = plain(title)
  for (const actor of actors) rest = rest.replace(plain(actor), ' ')

  return rest.replace(/\band\b/g, ' ').trim() === ''
}

function plain(text: string) {
  return text.toLowerCase().normalize('NFKD').replace(/[^a-z0-9]+/g, ' ').trim()
}

function HeartIcon() {
  return (
    <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true" focusable="false">
      <path
        d="M12 21s-7.5-4.6-9.5-9.2C1 8 3.4 4.5 7 4.5c2 0 3.5 1.1 5 2.9 1.5-1.8 3-2.9 5-2.9 3.6 0 6 3.5 4.5 7.3C19.5 16.4 12 21 12 21z"
        fill="currentColor"
      />
    </svg>
  )
}

function BookmarkIcon() {
  return (
    <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true" focusable="false">
      <path d="M6 3h12a1 1 0 0 1 1 1v17l-7-4.5L5 21V4a1 1 0 0 1 1-1z" fill="currentColor" />
    </svg>
  )
}

export function VideoGrid({ videos, act, pending, dismissible = false }: {
  videos: VideoSummary[]
  act: PersonalAction
  pending: PersonalPending
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
