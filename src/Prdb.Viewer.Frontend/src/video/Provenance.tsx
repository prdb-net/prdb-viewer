import type { VideoSummary } from '../api/client'
import { provenanceLabel, siteProvenanceLabel } from '../lib/format'

/// What is known about a Video and how it came to be known. A candidate never looks established,
/// and a locally recognised Site never looks like one prdb matched.
export function Provenance({ identification }: { identification?: VideoSummary['identification'] }) {
  if (!identification) return null
  const { work, site } = identification
  const review = work.reviewStatus === 'ReviewNeeded' || site.reviewStatus === 'ReviewNeeded'

  return (
    <div className="provenance">
      <span className={work.resolution === 'Established' ? 'badge established' : 'badge unknown'}>
        {work.resolution === 'Established' ? provenanceLabel(work.source) : 'Unknown Video'}
      </span>
      {site.resolution === 'Established' && site.targetTitle && (
        <span className="badge site">{site.targetTitle} · {siteProvenanceLabel(site.source)}</span>
      )}
      {review && <span className="badge review">Review needed</span>}
      {identification.actors.length > 0 && <small>{identification.actors.join(', ')}</small>}
    </div>
  )
}
