import { Fragment } from 'react'
import { Link, useLocation } from 'react-router'

import type { VideoSummary } from '../api/client'
import { provenanceLabel, siteProvenanceLabel } from '../lib/format'
import { withReturnTo } from '../lib/returnTo'

/// What is known about a Video and how it came to be known. A candidate never looks established,
/// and a locally recognised Site never looks like one prdb matched.
///
/// Each Actor whose credit resolves to somebody is a way to their own page, and the way back is
/// this Video. A credit that resolves to nobody stays plain text: there is nothing behind it to
/// open, and a link that leads to "not here" is worse than a name.
export function Provenance({ identification }: { identification?: VideoSummary['identification'] }) {
  const location = useLocation()
  if (!identification) return null
  const { work, site } = identification
  const review = work.reviewStatus === 'ReviewNeeded' || site.reviewStatus === 'ReviewNeeded'
  const from = `${location.pathname}${location.search}`

  return (
    <div className="provenance">
      <span className={work.resolution === 'Established' ? 'badge established' : 'badge unknown'}>
        {work.resolution === 'Established' ? provenanceLabel(work.source) : 'Unknown Video'}
      </span>
      {site.resolution === 'Established' && site.targetTitle && (
        <span className="badge site">{site.targetTitle} · {siteProvenanceLabel(site.source)}</span>
      )}
      {review && <span className="badge review">Review needed</span>}
      {identification.actors.length > 0 && (
        <small className="actor-credits">
          {identification.actors.map((actor, index) => (
            <Fragment key={`${actor.name}-${index}`}>
              {index > 0 && <span aria-hidden="true">, </span>}
              {actor.actorId
                ? (
                  <Link to={withReturnTo(`/actors/${actor.actorId}`, from)}>
                    {actor.name}
                  </Link>
                  )
                : <span>{actor.name}</span>}
            </Fragment>
          ))}
        </small>
      )}
    </div>
  )
}
