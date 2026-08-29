import { useQuery } from '@tanstack/react-query'

import { api, type Account, type PersonalLibrary } from '../api/client'
import { queryKeys } from '../queryKeys'
import { PageHeading, RequestError } from '../ui'
import { VideoGrid } from '../video/VideoCard'
import { usePersonalActions } from './usePersonalActions'

type Shelf = keyof PersonalLibrary

const shelves: Record<Shelf, { title: string; explanation: string; empty: string }> = {
  continueWatching: {
    title: 'Continue Watching',
    explanation: 'Videos you started and have not finished. Only you can see this.',
    empty: 'Nothing is part-watched. A Video you start appears here until you finish or dismiss it.',
  },
  favourites: {
    title: 'Favourites',
    explanation: 'The Videos you marked as your own. Only you can see this.',
    empty: 'No Video is a Favourite yet.',
  },
  watchLater: {
    title: 'Watch Later',
    explanation: 'What you set aside for later. Only you can see this.',
    empty: 'Nothing is set aside yet.',
  },
}

/// One shelf of an Account's Personal State, on its own page.
///
/// The three shelves differ only in which list they show, so they are one screen: a fourth shelf is
/// an entry above and a line in the table, not another component.
export function PersonalShelfPage({ account, shelf }: { account: Account; shelf: Shelf }) {
  const personalLibrary = useQuery({
    queryKey: queryKeys.personalLibrary,
    queryFn: api.personalLibrary,
  })
  const personal = usePersonalActions(account)
  const description = shelves[shelf]

  if (personalLibrary.isPending) {
    return <p role="status">Opening your library…</p>
  }

  if (personalLibrary.isError) {
    return <RequestError error={personalLibrary.error} />
  }

  const videos = personalLibrary.data[shelf]

  return (
    <>
      <PageHeading
        eyebrow="Yours"
        title={description.title}
        actions={<span className="muted">{videos.length} here</span>}
      >
        {description.explanation}
      </PageHeading>

      {videos.length === 0
        ? <div className="empty-library"><strong>Nothing here yet</strong><p>{description.empty}</p></div>
        : (
          <VideoGrid
            videos={videos}
            act={personal.act}
            pending={personal.pending}
            dismissible={shelf === 'continueWatching'}
          />
        )}

      {personal.failed && <RequestError error={personal.error} />}
    </>
  )
}
