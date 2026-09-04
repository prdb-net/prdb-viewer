import { useEffect, useState } from 'react'
import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useLocation } from 'react-router'

import {
  api,
  emptyFilters,
  type Account,
  type FacetSearch,
  type LibraryPage as LibraryPageResult,
} from '../api/client'
import { shelves, type Shelf } from '../personal/shelves'
import { usePersonalActions } from '../personal/usePersonalActions'
import { queryKeys } from '../queryKeys'
import { firstError, PageHeading, RequestError } from '../ui'
import { VideoGrid } from '../video/VideoCard'
import { LibraryControls } from './LibraryControls'
import { useLibraryFilters } from './useLibraryFilters'

const pageSize = 60
const playabilityValues = ['ReadyForDirectPlay', 'CompatibilityUncertain', 'NotDirectlyPlayable']

/// How often a library with nothing in it looks again.
///
/// This is the one state where the screen waits for something that arrives without anyone doing
/// anything — the first technical inspection to complete — and it says so in as many words. A
/// library that holds Videos is not refreshed on a timer: returning to the tab refreshes it, and so
/// does anything done here.
const emptyLibraryPollMilliseconds = 30_000

/// The shared library: everything this Account's client can discover, narrowed by what the address
/// says. It shows Videos and offers what belongs to a list — search results, facets, order and
/// depth. What belongs to one Video belongs to that Video's own page.
///
/// A Personal Shelf is this screen with the shelf pinned. The three shelves used to be a screen of
/// their own that loaded everything on them and offered nothing to narrow it; the search in the
/// header led away from them to the whole Library. A shelf is a way of narrowing the Library, so
/// it has what the Library has, and the search stays on it.
export function LibraryPage({ account, shelf }: { account: Account; shelf?: Shelf }) {
  const queryClient = useQueryClient()
  const location = useLocation()
  const { filters, pages, narrow, toggle, clear, showMore, narrowed } = useLibraryFilters(shelf)
  const description = shelf ? shelves[shelf] : undefined
  // The facets are counted against what is chosen, so a count says what choosing that value would
  // leave. The sort order is not part of what is chosen, so changing it does not ask again; and
  // the previous answer stays on screen while the next one arrives, so the rows do not empty and
  // refill on every click.
  const narrowing = JSON.stringify({ ...filters, sort: emptyFilters.sort })
  // Looking for a value inside a facet is how its list is read rather than what the Library is
  // narrowed to, so it stays here instead of going into the address — the same standing as how
  // much of a facet has been revealed, and unlike everything ADR 0004 puts in the URL.
  const [finding, setFinding] = useState<FacetSearch>({})
  const facets = useQuery({
    queryKey: queryKeys.libraryFacets(narrowing, JSON.stringify(finding)),
    queryFn: () => api.libraryFacets(filters, finding),
    placeholderData: (previous) => previous,
  })
  const videos = useInfiniteQuery({
    queryKey: queryKeys.videos(JSON.stringify(filters)),
    queryFn: ({ pageParam }) => api.videos(filters, pageParam, pageSize),
    initialPageParam: 0,
    getNextPageParam: (last, loaded) => (last.hasMore ? loaded.length * pageSize : undefined),
    placeholderData: (previous) => previous,
  })
  const personal = usePersonalActions(account)
  const includeNotReady = useMutation({
    mutationFn: (included: boolean) => api.setIncludeNotReady(included, account.csrfToken),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['videos'] }),
  })

  const loaded = videos.data?.pages.length ?? 0
  const { hasNextPage, isFetchingNextPage, fetchNextPage, isFetching, refetch } = videos

  /// The address carries how much was revealed, so arriving at it reveals that much again — a page
  /// at a time rather than as one ever-widening request, which is what made returning to a deep
  /// address cost the whole depth on every refresh.
  useEffect(() => {
    if (loaded > 0 && loaded < pages && hasNextPage && !isFetchingNextPage) {
      void fetchNextPage()
    }
  }, [loaded, pages, hasNextPage, isFetchingNextPage, fetchNextPage])

  const empty = (videos.data?.pages[0]?.videos.length ?? 0) === 0

  useEffect(() => {
    // An empty shelf waits for the User, not for inspection, so it does not look again on its own.
    if (!empty || isFetching || shelf) return

    const timer = window.setTimeout(() => void refetch(), emptyLibraryPollMilliseconds)
    return () => window.clearTimeout(timer)
  }, [empty, isFetching, refetch, shelf])

  if (videos.isPending) {
    return <p role="status">{description ? 'Opening your library…' : 'Opening the shared library…'}</p>
  }

  if (videos.isError) {
    return <RequestError error={videos.error} />
  }

  // The counts describe the whole match rather than one page of it, so the newest page holds the
  // current answer; the Videos themselves are every page revealed so far.
  const revealed = videos.data.pages
  const page = revealed[revealed.length - 1]
  const shown = revealed.flatMap((slice) => slice.videos)

  // The same words, asked of the whole Library: the address without the route that pinned the
  // shelf. It is the one thing a shelf that came up short cannot answer by itself.
  const wholeLibrary = { pathname: '/', search: location.search }

  return (
    <>
      {description
        ? (
          <PageHeading eyebrow="Yours" title={description.title}>
            {narrowed
              ? 'The Videos on this shelf your search and filters admit. Only you can see this.'
              : description.explanation}
          </PageHeading>
          )
        : (
          <PageHeading eyebrow="Library" title={narrowed ? 'Matching Videos' : 'Browse'}>
            {narrowed
              ? 'These are the Videos your search and filters admit.'
              : 'Everything this browser can play, newest first.'}
          </PageHeading>
          )}

      {/* Nothing to narrow is not a narrowing question. An empty shelf offered Filters and Sort
          over none of anything, which is a row of controls that can only ever answer the same
          way. A shelf that came up empty because of the filters keeps them, because taking one
          out is exactly what is left to do. */}
      {(shown.length > 0 || narrowed) && (
        <LibraryControls
          filters={filters}
          facets={facets.data}
          narrow={narrow}
          toggle={toggle}
          clear={clear}
          narrowed={narrowed}
          pinned={shelf}
          total={Number(page.totalMatches)}
          finding={finding}
          find={setFinding}
        />
      )}

      {description && narrowed && shown.length > 0 && (
        <p className="scope-escape">
          Only this shelf is searched. <Link to={wholeLibrary}>Search the whole library instead</Link>
        </p>
      )}

      {shown.length === 0 && (
        <div className="empty-library">
          <strong>{narrowed ? 'Nothing matches' : description ? 'Nothing here yet' : 'No Videos yet'}</strong>
          {narrowed
            ? (
              <p>
                {description ? 'Nothing on this shelf matches. ' : ''}
                Adjust the search or the filters
                {description ? <>, or <Link to={wholeLibrary}>search the whole library</Link></> : ''}.
              </p>
              )
            : description
              ? (
                <>
                  {/* An empty shelf is a dead end otherwise: it explains what would put something here
                      without offering the one screen anything is put here from. */}
                  <p>{description.empty}</p>
                  <p><Link className="quiet-button" to="/">Browse the library</Link></p>
                </>
                )
              : <p>Videos appear here as technical inspection completes.</p>}
        </div>
      )}

      <VideoGrid
        videos={shown}
        act={personal.act}
        pending={personal.pending}
        dismissible={shelf === 'ContinueWatching'}
      />

      <HiddenMatches
        page={page}
        includeNotReady={() => includeNotReady.mutate(true)}
        showUnavailable={() => narrow({ availability: ['Unavailable'], playability: playabilityValues })}
        pending={includeNotReady.isPending}
      />

      {hasNextPage && (
        <div className="load-more">
          {/* How much of the match is on screen, so the decision to reveal more is taken against
              the whole rather than against a button that could go on forever. */}
          <span className="muted">{shown.length} of {Number(page.totalMatches)} shown</span>
          <button
            className="quiet-button"
            onClick={() => {
              showMore()
              void fetchNextPage()
            }}
            // Only the fetch this button asked for disables it. A background refresh used to, which
            // made the control unavailable at intervals for no reason the screen gave.
            disabled={isFetchingNextPage}
          >
            {isFetchingNextPage ? 'Loading…' : 'Show more'}
          </button>
        </div>
      )}

      {(personal.failed || includeNotReady.isError) && (
        <RequestError error={firstError(personal.error, includeNotReady.error)} />
      )}
    </>
  )
}

/// Matches the current rules keep out are reported rather than silently dropped, together with
/// the control that reveals them.
function HiddenMatches({ page, includeNotReady, showUnavailable, pending }: {
  page: LibraryPageResult
  includeNotReady: () => void
  showUnavailable: () => void
  pending: boolean
}) {
  if (Number(page.hiddenNotReadyForDirectPlay) === 0 && Number(page.hiddenUnavailable) === 0) {
    return null
  }

  return (
    <div className="hidden-matches" role="status">
      {Number(page.hiddenNotReadyForDirectPlay) > 0 && (
        <p>
          {page.hiddenNotReadyForDirectPlay} match
          {Number(page.hiddenNotReadyForDirectPlay) === 1 ? '' : 'es'} not ready for direct play.
          <button className="quiet-button" onClick={includeNotReady} disabled={pending}>
            Include them
          </button>
        </p>
      )}
      {Number(page.hiddenUnavailable) > 0 && (
        <p>
          {page.hiddenUnavailable} match
          {Number(page.hiddenUnavailable) === 1 ? '' : 'es'} currently unavailable.
          <button className="quiet-button" onClick={showUnavailable}>Show them</button>
        </p>
      )}
    </div>
  )
}
