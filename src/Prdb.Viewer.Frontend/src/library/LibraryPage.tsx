import { useEffect } from 'react'
import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { api, type Account, type LibraryPage as LibraryPageResult } from '../api/client'
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
export function LibraryPage({ account }: { account: Account }) {
  const queryClient = useQueryClient()
  const { filters, pages, narrow, toggle, clear, showMore, narrowed } = useLibraryFilters()
  const facets = useQuery({ queryKey: queryKeys.libraryFacets, queryFn: api.libraryFacets })
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
    if (!empty || isFetching) return

    const timer = window.setTimeout(() => void refetch(), emptyLibraryPollMilliseconds)
    return () => window.clearTimeout(timer)
  }, [empty, isFetching, refetch])

  if (videos.isPending) {
    return <p role="status">Opening the shared library…</p>
  }

  if (videos.isError) {
    return <RequestError error={videos.error} />
  }

  // The counts describe the whole match rather than one page of it, so the newest page holds the
  // current answer; the Videos themselves are every page revealed so far.
  const revealed = videos.data.pages
  const page = revealed[revealed.length - 1]
  const shown = revealed.flatMap((slice) => slice.videos)

  return (
    <>
      <PageHeading
        eyebrow="Library"
        title={narrowed ? 'Matching Videos' : 'Browse'}
        actions={<span className="muted">{page.totalMatches} {narrowed ? 'matching' : 'available'}</span>}
      >
        {narrowed
          ? 'These are the Videos your search and filters admit.'
          : 'Everything this browser can play, newest first.'}
      </PageHeading>

      <LibraryControls
        filters={filters}
        facets={facets.data}
        narrow={narrow}
        toggle={toggle}
        clear={clear}
        narrowed={narrowed}
      />

      {shown.length === 0 && (
        <div className="empty-library">
          <strong>{narrowed ? 'Nothing matches' : 'No Videos yet'}</strong>
          <p>{narrowed
            ? 'Adjust the search or the filters.'
            : 'Videos appear here as technical inspection completes.'}</p>
        </div>
      )}

      <VideoGrid videos={shown} act={personal.act} pending={personal.pending} />

      <HiddenMatches
        page={page}
        includeNotReady={() => includeNotReady.mutate(true)}
        showUnavailable={() => narrow({ availability: ['Unavailable'], playability: playabilityValues })}
        pending={includeNotReady.isPending}
      />

      {hasNextPage && (
        <button
          className="quiet-button load-more"
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
