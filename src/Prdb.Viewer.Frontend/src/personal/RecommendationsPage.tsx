import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useLocation } from 'react-router'

import {
  api,
  type Account,
  type RecommendationSection,
  type RecommendationSectionPage,
  type VideoSummary,
} from '../api/client'
import { queryKeys } from '../queryKeys'
import { usePersonalActions } from '../personal/usePersonalActions'
import { firstError, PageHeading, RequestError } from '../ui'
import { VideoGrid } from '../video/VideoCard'

const sectionSize = 12

/// What each section is, in the reader's words rather than the domain's, and what an empty one
/// says. The empty text is the part that matters: a section with nothing in it has to say why, or
/// it reads as something that failed.
const sections: Record<RecommendationSection, {
  title: string
  explanation: string
  empty: string
}> = {
  ForYouToWatchAgain: {
    title: 'For you to watch again',
    explanation: 'Videos you have come back to, or said something about.',
    empty: 'Nothing yet. Watch something for a minute or two, or react to a Video, and it will show up here.',
  },
  LongUnseen: {
    title: 'Long unseen',
    explanation: 'Videos you liked or watched properly, and have not watched for a while.',
    empty: 'Nothing has been away long enough yet. Videos you watched a fortnight ago or more appear here.',
  },
  NotYetDiscovered: {
    title: 'Not yet discovered',
    explanation: 'Videos in the library you have never watched.',
    empty: 'You have watched everything this browser can play. That is quite a thing.',
  },
}

const order: RecommendationSection[] = ['ForYouToWatchAgain', 'LongUnseen', 'NotYetDiscovered']

/// The Recommendations page: three sections answering "what do I feel like watching?" from this
/// Account's own history on this installation.
///
/// Everything here is private. Nobody else can see what it offers, what it is offered from, or
/// what has been put aside — and nothing about it leaves the installation.
export function RecommendationsPage({ account }: { account: Account }) {
  const queryClient = useQueryClient()
  const location = useLocation()
  const personal = usePersonalActions(account)
  // The seed the page is drawn from. It is held here rather than in the address because it is not
  // a narrowing anybody would want to come back to: a reader who returns tomorrow wants today's
  // suggestions, not the ones a link happened to freeze.
  const [seed, setSeed] = useState<number>()
  // What has just been put aside, so the card can offer the undo in the place it disappeared from
  // rather than as a message somewhere else on the screen.
  const [asideNow, setAsideNow] = useState<VideoSummary[]>([])

  const page = useQuery({
    queryKey: queryKeys.recommendations(String(seed ?? 'today')),
    queryFn: () => api.recommendations(seed, sectionSize),
  })

  const notToday = useMutation({
    mutationFn: ({ video, dismissed }: { video: VideoSummary; dismissed: boolean }) =>
      api.setNotToday(video.id, dismissed, account.csrfToken),
    onSuccess: (_, { video, dismissed }) => {
      setAsideNow((aside) => dismissed
        ? [...aside.filter((held) => held.id !== video.id), video]
        : aside.filter((held) => held.id !== video.id))
      void queryClient.invalidateQueries({ queryKey: ['recommendations'] })
    },
  })

  if (page.isPending) {
    return <p role="status">Working out what you might feel like…</p>
  }

  if (page.isError) {
    return <RequestError error={page.error} />
  }

  const aside = new Set(asideNow.map((video) => video.id))
  const empty = page.data.sections.every((section) => section.videos.length === 0)

  return (
    <>
      <PageHeading
        eyebrow="Yours"
        title="For you"
        actions={
          <button
            className="quiet-button"
            onClick={() => {
              setAsideNow([])
              setSeed(Math.floor(Math.random() * 2_147_483_647))
            }}
            disabled={page.isFetching}
          >
            Other suggestions
          </button>
        }
      >
        Worked out from what you have watched and said here. Only you can see this.
      </PageHeading>

      {!page.data.hasHistory && !empty && (
        <p className="scope-escape">
          You have not watched anything here yet, so these are simply Videos from the library rather
          than anything about your taste.
        </p>
      )}

      {empty && (
        <div className="empty-library">
          <strong>Nothing to suggest yet</strong>
          <p>
            The library has nothing this browser can play, so there is nothing to offer.{' '}
            <Link to="/">Browse the library</Link> to see what is there.
          </p>
        </div>
      )}

      {order.map((name) => (
        <Section
          key={name}
          name={name}
          page={page.data.sections.find((section) => section.section === name)}
          aside={aside}
          asideVideos={asideNow}
          from={`${location.pathname}${location.search}`}
          act={personal.act}
          pending={personal.pending}
          busy={notToday.isPending}
          notToday={(video) => notToday.mutate({ video, dismissed: true })}
          undo={(video) => notToday.mutate({ video, dismissed: false })}
        />
      ))}

      {(personal.failed || notToday.isError) && (
        <RequestError error={firstError(personal.error, notToday.error)} />
      )}
    </>
  )
}

function Section({
  name,
  page,
  aside,
  asideVideos,
  from,
  act,
  pending,
  busy,
  notToday,
  undo,
}: {
  name: RecommendationSection
  page?: RecommendationSectionPage
  aside: Set<string>
  asideVideos: VideoSummary[]
  from: string
  act: ReturnType<typeof usePersonalActions>['act']
  pending: ReturnType<typeof usePersonalActions>['pending']
  busy: boolean
  notToday: (video: VideoSummary) => void
  undo: (video: VideoSummary) => void
}) {
  const described = sections[name]
  const offered = (page?.videos ?? []).filter((offer) => !aside.has(offer.video.id))
  // What was put aside during this visit to the page, kept on screen so the undo is where the
  // card was. Once the reader leaves, the dismissal simply holds for the day.
  const putAside = asideVideos.filter((video) =>
    page?.videos.some((offer) => offer.video.id === video.id))

  return (
    <section className="recommendation-section" aria-labelledby={`section-${name}`}>
      <div className="section-heading">
        <h3 id={`section-${name}`}>{described.title}</h3>
        <span className="muted">{described.explanation}</span>
      </div>

      {offered.length === 0 && putAside.length === 0
        ? <p className="muted section-empty">{described.empty}</p>
        : (
          <>
            <VideoGrid
              videos={offered.map((offer) => offer.video)}
              act={act}
              pending={pending}
              from={from}
              reactions="always"
              recommendation={(video) => ({
                reasons: offered.find((offer) => offer.video.id === video.id)?.reasons ?? [],
                notToday,
                busy,
              })}
            />
            {putAside.length > 0 && (
              <ul className="put-aside">
                {putAside.map((video) => (
                  <li key={video.id}>
                    <span>
                      <strong>{video.displayTitle}</strong> is put aside for 24 hours. It is not a
                      dislike, and nothing about your history has changed.
                    </span>
                    <button
                      className="quiet-button"
                      aria-label={`Undo putting ${video.displayTitle} aside`}
                      onClick={() => undo(video)}
                      disabled={busy}
                    >
                      Undo
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </>
          )}
    </section>
  )
}
