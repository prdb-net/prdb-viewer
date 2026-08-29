import { useEffect, useRef } from 'react'

import {
  api,
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
      // Leaving the page ends the attempt as surely as closing the player does. Without this a
      // navigation would leave the attempt open until the session expired.
      if (!ended.current) {
        ended.current = true
        void api.endPlaybackAttempt(playbackAttemptId, csrfToken, true).catch(() => undefined)
      }
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
          void api.endPlaybackAttempt(playbackAttemptId, csrfToken).catch(() => undefined)
          void classifyFailure(event.currentTarget.error, source).then(failed)
        }}
      >Your browser cannot play this Video File.</video>
    </div>
  )
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
