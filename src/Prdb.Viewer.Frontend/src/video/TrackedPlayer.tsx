import { useEffect, useRef } from 'react'

import {
  api,
  type PlaybackDeparture,
  type PlaybackFailureCategory,
  type PlaybackReportRequest,
  type PlaybackVariant,
  type VideoSummary,
} from '../api/client'
import { fileFormat } from '../lib/format'

export function TrackedPlayer({ video, source, videoFileId, playbackAttemptId, resumePositionMilliseconds, previousAttempt, csrfToken, close, failed, succeeded, refresh }: {
  video: VideoSummary
  source: string
  videoFileId: string
  playbackAttemptId: string
  resumePositionMilliseconds: number
  previousAttempt?: PlaybackVariant
  csrfToken: string
  close: () => void
  /// What the attempt had reached when it failed, so a fallback to an equivalent encode can
  /// carry it rather than starting again. A failure at load has reached nothing.
  failed: (category: PlaybackFailureCategory, positionMilliseconds: number) => void
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

  /// What the timer and the page-leaving handler reach for when they run.
  ///
  /// They are installed once, because a five-second timer restarted on every render would never be
  /// allowed to finish. But a Playback Attempt outlives its Video File: automatic fallback moves to
  /// another one without remounting the player, so what was captured when the timer was installed
  /// is no longer what is playing. Held here, a report names the Video File that is actually
  /// carrying the attempt rather than the one that started it.
  const current = useRef({ flush, playbackAttemptId, csrfToken })
  // Writing a ref during render is what keeps this one current; the rule cannot tell that
  // apart from reading one to decide what to draw, which is the mistake it is for.
  // oxlint-disable-next-line react/refs
  current.current = { flush, playbackAttemptId, csrfToken }

  useEffect(() => {
    const interval = window.setInterval(() => void current.current.flush(false, false), 5_000)
    const end = (departure: PlaybackDeparture) => {
      ended.current = true
      void api
        .endPlaybackAttempt(
          current.current.playbackAttemptId,
          current.current.csrfToken,
          true,
          departure,
        )
        .catch(() => undefined)
    }
    // Closing the tab is an ordinary end to an ordinary session, and it is the one thing the
    // browser tells us for certain: nothing was navigated to.
    const closed = () => end('Closed')
    window.addEventListener('pagehide', closed)
    return () => {
      window.clearInterval(interval)
      window.removeEventListener('pagehide', closed)
      // Leaving the page ends the attempt as surely as closing the player does. Without this a
      // navigation would leave the attempt open until the session expired.
      //
      // Where it went is read from the address rather than guessed at. The router has already put
      // the destination there by the time this unmount is committed, so a reader who left this
      // Video for a different one is an observation rather than an inference — which is the only
      // basis on which a short visit is ever allowed to count against a Video.
      if (!ended.current) {
        end(departedToAnotherVideo(video.id) ? 'AnotherVideo' : 'Closed')
      }
    }
  }, [video.id])

  const stop = async () => {
    recordEvidence()
    try {
      await flush(false, true)
    } finally {
      ended.current = true
      await api
        .endPlaybackAttempt(playbackAttemptId, csrfToken, false, 'Closed')
        .catch(() => undefined)
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
      <div className="section-heading">
        <strong>{video.displayTitle}</strong>
        <button className="quiet-button" onClick={() => void stop()}>Close</button>
      </div>
      {previousAttempt && (
        <p className="fallback-notice" role="status">
          {fileFormat(previousAttempt)} did not play in this browser. Trying another Video File of
          the same Video.
        </p>
      )}
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
          // A failure is evidence about a Video File and about nothing else. Saying so here is
          // what stops it from ever being read as an opinion about the Video.
          void api
            .endPlaybackAttempt(playbackAttemptId, csrfToken, false, 'TechnicalFailure')
            .catch(() => undefined)
          // Where the viewer actually was, which the page cannot see from outside the element. A
          // fallback to a Video File whose timeline is equivalent resumes there instead of at the
          // beginning, which is what a Playback Attempt outliving one of its files should cost.
          const reached = Math.round(event.currentTarget.currentTime * 1_000)
          void classifyFailure(event.currentTarget.error, source)
            .then((category) => failed(category, reached))
        }}
      >Your browser cannot play this Video File.</video>
    </div>
  )
}

/// Whether the browser is now on a different Video's page than the one that was playing.
///
/// It is asked of the address the browser has actually arrived at, after the router wrote it, so
/// it states where the reader went rather than where they might have been going.
function departedToAnotherVideo(videoId: string) {
  const match = /^\/videos\/([^/?#]+)/.exec(window.location.pathname)
  return match !== null && match[1] !== videoId
}

/// Which kind of failure just happened. The browser says only that playback failed, so the same
/// delivery URL is asked once more: a file the installation cannot serve, or a network that is not
/// there, is not evidence that this browser cannot play the media. Only the media case rules a
/// variant out, so this distinction decides what is remembered and whether anything falls back.
async function classifyFailure(
  error: MediaError | null,
  source: string,
): Promise<PlaybackFailureCategory> {
  // The two codes that need no second opinion: 3 is a decode failure, and 2 is the browser's own
  // network error, which is still checked below because a 5xx reaches the element the same way.
  const decodeFailure = 3
  const networkFailure = 2

  if (error?.code === decodeFailure) return 'Media'

  try {
    const probe = await fetch(source, { method: 'HEAD', credentials: 'same-origin' })
    if (probe.status === 404 || probe.status === 410) return 'Availability'
    if (probe.status >= 500) return 'Delivery'
    if (!probe.ok) return 'Delivery'
  } catch {
    return 'Network'
  }

  return error?.code === networkFailure ? 'Network' : 'Media'
}
