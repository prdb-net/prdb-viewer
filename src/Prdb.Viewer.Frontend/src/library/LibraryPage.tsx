import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { api, type Account, type LibraryPage as LibraryPageResult } from '../api/client'
import { usePersonalActions } from '../personal/usePersonalActions'
import { queryKeys } from '../queryKeys'
import { PageHeading, RequestError } from '../ui'
import { VideoGrid } from '../video/VideoCard'
import { LibraryControls } from './LibraryControls'
import { useLibraryFilters } from './useLibraryFilters'

const pageSize = 60
const playabilityValues = ['ReadyForDirectPlay', 'CompatibilityUncertain', 'NotDirectlyPlayable']

/// The shared library: everything this Account's client can discover, narrowed by what the address
/// says. It shows Videos and offers what belongs to a list — search results, facets, order and
/// depth. What belongs to one Video belongs to that Video's own page.
export function LibraryPage({ account }: { account: Account }) {
  const queryClient = useQueryClient()
  const { filters, pages, narrow, clear, showMore, narrowed } = useLibraryFilters()
  const facets = useQuery({ queryKey: queryKeys.libraryFacets, queryFn: api.libraryFacets })
  const videos = useQuery({
    queryKey: queryKeys.videos(JSON.stringify(filters), pages),
    queryFn: () => api.videos(filters, 0, pageSize * pages),
    refetchInterval: 5_000,
    placeholderData: (previous) => previous,
  })
  const personal = usePersonalActions(account)
  const includeNotReady = useMutation({
    mutationFn: (included: boolean) => api.setIncludeNotReady(included, account.csrfToken),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['videos'] }),
  })

  if (videos.isPending) {
    return <p role="status">Opening the shared library…</p>
  }

  if (videos.isError) {
    return <RequestError />
  }

  const page = videos.data

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
        clear={clear}
        narrowed={narrowed}
      />

      {page.videos.length === 0 && (
        <div className="empty-library">
          <strong>{narrowed ? 'Nothing matches' : 'No Videos yet'}</strong>
          <p>{narrowed
            ? 'Adjust the search or the filters.'
            : 'Videos appear here as technical inspection completes.'}</p>
        </div>
      )}

      <VideoGrid videos={page.videos} act={personal.act} pending={personal.pending} />

      <HiddenMatches
        page={page}
        includeNotReady={() => includeNotReady.mutate(true)}
        showUnavailable={() => narrow({ availability: ['Unavailable'], playability: playabilityValues })}
        pending={includeNotReady.isPending}
      />

      {page.hasMore && (
        <button className="quiet-button load-more" onClick={showMore} disabled={videos.isFetching}>
          {videos.isFetching ? 'Loading…' : 'Show more'}
        </button>
      )}

      {(personal.failed || includeNotReady.isError) && <RequestError />}
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
