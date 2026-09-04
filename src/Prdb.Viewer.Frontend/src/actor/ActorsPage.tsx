import { useEffect } from 'react'
import { useInfiniteQuery } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router'

import { api, type ActorSortOrder, type ActorSummary } from '../api/client'
import { queryKeys } from '../queryKeys'
import { PageHeading, RequestError } from '../ui'

const pageSize = 60

const orders: { value: ActorSortOrder; label: string }[] = [
  { value: 'Name', label: 'By name' },
  { value: 'MostHere', label: 'Most in this library' },
]

/// Everybody this library's Videos credit, as a picture-led index.
///
/// It is read the way the Library is read — one search, one order, a page at a time, all of it in
/// the address — because it is the same kind of screen and inventing a second set of answers for
/// an empty result or a first page still loading would be inventing them twice.
///
/// The one question that is not the Library's is what this looks like before the profiles have
/// arrived: every Actor a name and a count and no picture, which is a plausible grid of grey
/// rectangles. It says how many are still waiting rather than leaving that to be guessed.
export function ActorsPage() {
  const [parameters, setParameters] = useSearchParams()
  const query = parameters.get('query') ?? ''
  const sort = (parameters.get('sort') as ActorSortOrder | null) ?? 'Name'
  const pages = Math.max(1, Number(parameters.get('pages') ?? 1) || 1)

  const actors = useInfiniteQuery({
    queryKey: queryKeys.actors(query, sort),
    queryFn: ({ pageParam }) => api.actors(query, sort, pageParam, pageSize),
    initialPageParam: 0,
    getNextPageParam: (last, loaded) => (last.hasMore ? loaded.length * pageSize : undefined),
    placeholderData: (previous) => previous,
  })

  const loaded = actors.data?.pages.length ?? 0
  const { hasNextPage, isFetchingNextPage, fetchNextPage } = actors

  // The address carries how much was revealed, so arriving at it reveals that much again.
  useEffect(() => {
    if (loaded > 0 && loaded < pages && hasNextPage && !isFetchingNextPage) {
      void fetchNextPage()
    }
  }, [loaded, pages, hasNextPage, isFetchingNextPage, fetchNextPage])

  const order = (chosen: ActorSortOrder) => {
    const next = new URLSearchParams(parameters)
    if (chosen === 'Name') next.delete('sort')
    else next.set('sort', chosen)
    next.delete('pages')
    setParameters(next)
  }

  const showMore = () => {
    const next = new URLSearchParams(parameters)
    next.set('pages', String(pages + 1))
    setParameters(next, { replace: true })
  }

  if (actors.isPending) {
    return <p role="status">Opening the Actors…</p>
  }

  if (actors.isError) {
    return <RequestError error={actors.error} />
  }

  const revealed = actors.data.pages
  const page = revealed[revealed.length - 1]
  const shown = revealed.flatMap((slice) => slice.actors)
  const total = Number(page.totalMatches)
  const awaiting = Number(page.awaitingProfiles)

  return (
    <>
      <PageHeading eyebrow="Library" title="Actors">
        Everybody the Videos in this library are identified as being in. What prdb knows about
        them is kept here, so their pages read whether or not prdb can be reached.
      </PageHeading>

      <div className="actor-index-controls">
        <label className="sort-field">
          <span>Sort</span>
          <select
            value={sort}
            onChange={(event) => order(event.target.value as ActorSortOrder)}
          >
            {orders.map((choice) => (
              <option key={choice.value} value={choice.value}>{choice.label}</option>
            ))}
          </select>
        </label>
        <span className="muted">
          {query ? `${total} matching “${query}”` : `${total} in this library`}
        </span>
      </div>

      {awaiting > 0 && (
        <p className="muted actor-awaiting">
          {awaiting === total
            ? 'None of their profiles have arrived yet, so this reads as names and counts. The pictures follow on their own.'
            : `${awaiting} of them are still waiting for what prdb knows, so they have no picture yet.`}
        </p>
      )}

      {shown.length === 0
        ? (
          <p className="muted">
            {query
              ? 'No Actor of this library is credited under that name.'
              : 'No Video in this library is identified as being anybody in particular yet. Actors arrive with prdb identification.'}
          </p>
          )
        : (
          <div className="actor-index">
            {shown.map((actor) => <ActorCard key={actor.actorId} actor={actor} />)}
          </div>
          )}

      {hasNextPage && (
        <div className="load-more">
          {/* How much of the match is on screen, so revealing more is decided against the whole. */}
          <span className="muted">{shown.length} of {total} shown</span>
          <button
            className="quiet-button"
            onClick={() => {
              showMore()
              void fetchNextPage()
            }}
            disabled={isFetchingNextPage}
          >{isFetchingNextPage ? 'Loading…' : 'Show more'}</button>
        </div>
      )}
    </>
  )
}

/// One Actor in the index: their picture, their name, and the only number on this screen that
/// means anything to the person reading it — how many Videos they have *here*.
function ActorCard({ actor }: { actor: ActorSummary }) {
  const videos = Number(actor.videoCount)

  return (
    <Link className="actor-card" to={`/actors/${actor.actorId}`}>
      {actor.portraitUrl
        ? <img src={actor.portraitUrl} alt="" loading="lazy" />
        : <span className="actor-placeholder" aria-hidden="true">☺</span>}
      <span className="actor-card-name">{actor.name}</span>
      <small>{videos === 1 ? '1 Video here' : `${videos} Videos here`}</small>
    </Link>
  )
}
