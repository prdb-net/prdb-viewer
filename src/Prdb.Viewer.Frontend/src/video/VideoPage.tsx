import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams, useSearchParams } from 'react-router'

import {
  api,
  type Account,
  type PlaybackFailureCategory,
  type PlaybackVariant,
  type VideoSummary,
} from '../api/client'
import {
  fileFormat,
  formatDuration,
  friendlyState,
  playbackUnavailableReason,
  variantReason,
} from '../lib/format'
import {
  formatBitrate,
  formatSize,
  qualityFacts,
  qualityLabel,
  qualitySource,
} from '../lib/quality'
import { StarRating } from '../personal/StarRating'
import { usePersonalActions } from '../personal/usePersonalActions'
import { queryKeys } from '../queryKeys'
import { firstError, Notice, PageHeading, RequestError } from '../ui'
import { Provenance } from './Provenance'
import { TrackedPlayer } from './TrackedPlayer'
import { VideoArt } from './VideoArt'

/// One deliberate play action in progress: the variant being tried, the ones left to try, and the
/// ones already attempted, so no occurrence is tried twice and the failure can name them all.
type PlaybackSession = {
  variant: PlaybackVariant
  remaining: PlaybackVariant[]
  attempted: PlaybackVariant[]
  playbackAttemptId: string
  resumePositionMilliseconds: number
}

type TerminalPlaybackFailure = {
  attempted: PlaybackVariant[]
  category: PlaybackFailureCategory
  detail?: string
}

/// One Video, addressed rather than found.
///
/// Playback lives here and nowhere else: one page owns the play action, the order the variants are
/// tried in, and what each kind of failure means. A Video reached from a shelf, from a search, or
/// from a link somebody sent is therefore the same screen with the same evidence.
export function VideoPage({ account }: { account: Account }) {
  const { videoId = '' } = useParams()
  const [parameters, setParameters] = useSearchParams()
  const queryClient = useQueryClient()
  const detail = useQuery({
    queryKey: queryKeys.video(videoId),
    queryFn: () => api.video(videoId),
    retry: false,
  })
  const personal = usePersonalActions(account)
  const [playing, setPlaying] = useState<PlaybackSession>()
  const [failure, setFailure] = useState<TerminalPlaybackFailure>()
  const video = detail.data?.video

  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.video(videoId) })
    void queryClient.invalidateQueries({ queryKey: ['videos'] })
    void queryClient.invalidateQueries({ queryKey: queryKeys.personalLibrary })
  }

  const startPlayback = useMutation({
    mutationFn: ({ variant, remaining, attempted }: {
      variant: PlaybackVariant
      remaining: PlaybackVariant[]
      attempted: PlaybackVariant[]
    }) => api.startPlaybackAttempt(videoId, variant.videoFileId, account.csrfToken)
      .then((result) => ({ result, variant, remaining, attempted })),
    onSuccess: ({ result, variant, remaining, attempted }) => {
      if (result.verdict === 'Started' && result.playbackAttemptId) {
        setPlaying({
          variant,
          remaining,
          attempted,
          playbackAttemptId: result.playbackAttemptId,
          resumePositionMilliseconds: Number(result.resumePositionMilliseconds ?? 0),
        })
        return
      }

      setFailure({
        attempted: [...attempted, variant],
        category: 'Availability',
        detail: 'The Video File could not be opened for playback.',
      })
    },
  })
  const recordOutcome = useMutation({
    mutationFn: ({ videoFileId, outcome, category }: {
      videoFileId: string
      outcome: 'Succeeded' | 'Failed'
      category: PlaybackFailureCategory | null
    }) => api.recordPlaybackOutcome(videoFileId, outcome, category, account.csrfToken),
    onSuccess: refresh,
  })
  const forgetOutcomes = useMutation({
    mutationFn: () => api.forgetPlaybackOutcomes(videoId, account.csrfToken),
    onSuccess: refresh,
  })

  /// One deliberate play action. The server has already ordered the variants by the evidence this
  /// client produced, so this follows that order and tries each Available occurrence at most once.
  /// A variant the client has ruled out is attempted only when it was chosen explicitly.
  const play = (chosen?: PlaybackVariant) => {
    if (!video) return
    const ordered = chosen
      ? [chosen, ...video.videoFiles.filter((variant) => variant.videoFileId !== chosen.videoFileId)]
      : video.videoFiles.filter((variant) => variant.selectionReason !== 'RuledOutHere')
    const [first, ...remaining] = ordered
    if (!first) return
    setFailure(undefined)
    startPlayback.mutate({ variant: first, remaining, attempted: [] })
  }

  /// What a failed attempt does next. Only a media failure says anything about the file, so only
  /// that is remembered and only that falls back: a delivery or network failure would fail the same
  /// way for every other variant, and trying them all would say nothing and cost everything.
  const failed = (category: PlaybackFailureCategory) => {
    const session = playing
    if (!session) return
    setPlaying(undefined)

    if (category === 'Media') {
      recordOutcome.mutate({
        videoFileId: session.variant.videoFileId,
        outcome: 'Failed',
        category,
      })
    }

    const attempted = [...session.attempted, session.variant]
    const next = category === 'Media' ? session.remaining[0] : undefined

    if (next) {
      // Fallback stays inside the same Playback Attempt: one deliberate play action is one
      // attempt, whichever of its Video Files ends up carrying it.
      setPlaying({
        ...session,
        variant: next,
        remaining: session.remaining.slice(1),
        attempted,
        resumePositionMilliseconds: 0,
      })
      return
    }

    setFailure({ attempted, category, detail: undefined })
  }

  /// A confirmed success is the strongest evidence there is for this Account on this client, and
  /// the only one that is not a prediction.
  const succeeded = () => {
    if (playing) {
      recordOutcome.mutate({
        videoFileId: playing.variant.videoFileId,
        outcome: 'Succeeded',
        category: null,
      })
    }
  }

  // A link can ask for playback, which is what makes `Play` in the Library a link rather than a
  // button. The request is consumed once, so closing the player does not start it again.
  const requested = parameters.get('play')
  useEffect(() => {
    if (!video || !requested || playing || startPlayback.isPending) return
    const chosen = video.videoFiles.find((variant) => variant.videoFileId === requested)
    setParameters((current) => {
      const next = new URLSearchParams(current)
      next.delete('play')
      return next
    }, { replace: true })
    play(chosen)
    // Consuming the request depends on the Video having arrived, not on the callbacks it uses.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [video, requested])

  if (detail.isPending) {
    return <p role="status">Opening this Video…</p>
  }

  if (detail.isError || !video) {
    return (
      <>
        <PageHeading title="This Video is not here">
          It may have been removed from the library, or the link may be wrong.
        </PageHeading>
        <Link className="quiet-button" to="/">Back to the library</Link>
      </>
    )
  }

  // This screen is about one Video, so its own actions are the only ones that make it busy.
  const saving = personal.pending(video.id)
  const busy = startPlayback.isPending || saving || playing !== undefined
  // The facts describe the occurrence a play action would reach for, so the sidebar and the corner
  // of the picture are talking about the same Video File.
  const source = qualitySource(video)
  const quality = source ? qualityFacts(source) : []

  return (
    <>
      <PageHeading
        eyebrow="Video"
        title={video.displayTitle}
        actions={<Link className="quiet-button" to="/">Back to the library</Link>}
      />

      {detail.data?.supersededVideoId && (
        <Notice kind="success">
          That link named a Video that has since been merged into this one.
        </Notice>
      )}

      {failure && (
        <TerminalFailure
          video={video}
          failure={failure}
          dismiss={() => setFailure(undefined)}
          retry={() => {
            forgetOutcomes.mutate()
            setFailure(undefined)
          }}
        />
      )}

      <div className="video-detail">
        {/* The player takes the place of the preview rather than sitting above the page: what is
            known about the Video stays beside it while it plays. */}
        <div className="video-detail-main">
          {playing ? (
            <TrackedPlayer
              video={video}
              source={playing.variant.deliveryUrl}
              videoFileId={playing.variant.videoFileId}
              playbackAttemptId={playing.playbackAttemptId}
              resumePositionMilliseconds={playing.resumePositionMilliseconds}
              previousAttempt={playing.attempted[playing.attempted.length - 1]}
              csrfToken={account.csrfToken}
              close={() => setPlaying(undefined)}
              failed={failed}
              succeeded={succeeded}
              refresh={refresh}
            />
          ) : (
            <>
              <VideoArt video={video} large />
              <PlayAction video={video} play={play} pending={busy} />
            </>
          )}
        </div>

        <aside className="video-detail-facts">
          <Provenance identification={video.identification} />
          <dl className="fact-list">
            {/* What the file itself is comes first: somebody deciding whether to watch this asks
                what they would be watching before they ask what the library did with it. */}
            {quality.map((fact) => (
              <div key={fact.label}><dt>{fact.label}</dt><dd>{fact.value}</dd></div>
            ))}
            <div><dt>Discovered</dt><dd>{new Date(video.discoveryDate).toLocaleDateString()}</dd></div>
            <div><dt>Availability</dt><dd>{friendlyState(video.availability)}</dd></div>
            <div><dt>Playability</dt><dd>{friendlyState(video.playability)}</dd></div>
            <div><dt>Play state</dt><dd>{friendlyState(video.personalState.playState)}</dd></div>
            <div><dt>Plays</dt><dd>{Number(video.personalState.playCount)}</dd></div>
            {Number(video.personalState.playbackProgressMilliseconds ?? 0) > 0 && (
              <div>
                <dt>Progress</dt>
                <dd>{formatDuration(Number(video.personalState.playbackProgressMilliseconds))}</dd>
              </div>
            )}
          </dl>

          <div className="personal-actions">
            <button
              className={video.personalState.favourite ? 'selected' : ''}
              aria-pressed={video.personalState.favourite}
              onClick={() => personal.act('favourite', video, !video.personalState.favourite)}
              disabled={saving}
            >Favourite</button>
            <button
              className={video.personalState.watchLater ? 'selected' : ''}
              aria-pressed={video.personalState.watchLater}
              onClick={() => personal.act('watch-later', video, !video.personalState.watchLater)}
              disabled={saving}
            >Watch Later</button>
          </div>
          <StarRating
            title={video.displayTitle}
            value={video.personalState.personalRating}
            onChange={(score) => personal.act('rating', video, score)}
            disabled={saving}
            size="large"
          />
        </aside>
      </div>

      <section className="variants" aria-labelledby="variants-title">
        <div className="section-heading">
          <h3 id="variants-title">Video Files</h3>
          <span className="muted">{video.videoFiles.length}</span>
        </div>
        {video.videoFiles.length === 0 && (
          <p className="muted">No Video File of this Video is currently available.</p>
        )}
        <ul className="variant-list">
          {video.videoFiles.map((variant) => (
            <li key={variant.videoFileId}>
              <span>{variantHeadline(variant)}</span>
              <small>{variantDetail(variant)}</small>
              <button className="quiet-button" onClick={() => play(variant)} disabled={busy}>
                {variant.selectionReason === 'RuledOutHere' ? 'Try anyway' : 'Play this one'}
              </button>
            </li>
          ))}
        </ul>
      </section>

      {(startPlayback.isError || personal.failed || forgetOutcomes.isError) && (
        <RequestError
          error={firstError(startPlayback.error, personal.error, forgetOutcomes.error)}
        />
      )}
    </>
  )
}

/// One occurrence in the list, named by what it is worth watching at before what it is encoded as.
/// Two occurrences of the same Video usually differ in quality first and in format second, so that
/// is the order somebody choosing between them reads them in.
function variantHeadline(variant: PlaybackVariant) {
  const quality = qualityLabel(variant)
  return quality ? `${quality} · ${fileFormat(variant)}` : fileFormat(variant)
}

/// Why this occurrence stands where it does, and what choosing it would cost — the facts that
/// separate two occurrences a client is equally happy with.
function variantDetail(variant: PlaybackVariant) {
  const parts = [
    variantReason(variant),
    formatBitrate(Number(variant.bitrate ?? 0)),
    formatSize(Number(variant.size ?? 0)),
  ].filter(Boolean)

  return parts.join(' · ')
}

/// The three playability states offered as what they are: the ordinary action, a labelled attempt
/// with its reason, or no action at all with the reason there is none.
function PlayAction({ video, play, pending }: {
  video: VideoSummary
  play: (chosen?: PlaybackVariant) => void
  pending: boolean
}) {
  const source = video.videoFiles.find((variant) => variant.selectionReason !== 'RuledOutHere')
  const progress = Number(video.personalState.playbackProgressMilliseconds ?? 0)
  const resume = progress > 0 && video.personalState.playState === 'InProgress'

  if (video.playability === 'ReadyForDirectPlay' && source) {
    return (
      <button className="primary-button" onClick={() => play()} disabled={pending}>
        {resume ? `Resume at ${formatDuration(progress)}` : 'Play'}
      </button>
    )
  }

  if (video.playability === 'CompatibilityUncertain' && source) {
    return (
      <>
        <button className="primary-button uncertain" onClick={() => play()} disabled={pending}>
          Try Direct Play
        </button>
        <small className="uncertain-note">
          This browser has not confirmed {fileFormat(source)}. Playback may fail; nothing is
          converted.
        </small>
      </>
    )
  }

  return <span className="unsupported">{playbackUnavailableReason(video)}</span>
}

/// The end of one deliberate play action that never succeeded. It names what was attempted, says
/// which kind of failure ended it, and offers only the actions that could change the outcome.
function TerminalFailure({ video, failure, dismiss, retry }: {
  video: VideoSummary
  failure: TerminalPlaybackFailure
  dismiss: () => void
  retry: () => void
}) {
  const attempted = failure.attempted.map(fileFormat).join(', ')

  return (
    <div className="terminal-failure" role="alert">
      <strong>{video.displayTitle} could not be played</strong>
      <p>{failureExplanation(failure.category)}</p>
      {failure.detail && <p>{failure.detail}</p>}
      <small>
        {failure.attempted.length === 1 ? 'Attempted variant' : 'Attempted variants'}: {attempted}
      </small>
      <div className="row-actions">
        {failure.category === 'Media' && (
          <button className="quiet-button" onClick={retry}>Forget this and try again</button>
        )}
        {failure.category !== 'Media' && (
          <button className="quiet-button" onClick={dismiss}>Try again later</button>
        )}
        <button className="quiet-button" onClick={dismiss}>Close</button>
      </div>
    </div>
  )
}

function failureExplanation(category: PlaybackFailureCategory) {
  if (category === 'Media') {
    return 'This browser could not decode the media in any Available Video File. Nothing is ' +
      'converted, so another browser or device may still play it.'
  }
  if (category === 'Availability') {
    return 'The Video File could not be read where the library expects it. This is a library ' +
      'problem rather than a browser one, and the next scan will reconcile it.'
  }
  if (category === 'Delivery') {
    return 'The installation answered the playback request incorrectly. Other Video Files would ' +
      'fail the same way, so nothing else was attempted. An Administrator should check the ' +
      'installation and any reverse proxy in front of it.'
  }
  return 'This browser could not reach the installation. Nothing about the Video File is known ' +
    'from this attempt.'
}
