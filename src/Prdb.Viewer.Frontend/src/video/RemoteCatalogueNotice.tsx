import type { components } from '../api/schema'
import { formatDay } from '../lib/format'

export function RemoteCatalogueNotice({ status, proposed = false }: {
  status?: components['schemas']['RemoteCatalogueStatus'] | null
  proposed?: boolean
}) {
  if (!status || (status.state !== 'Deleted' && status.state !== 'Merged')) return null
  const day = formatDay(status.deletedAt)
  return <div className="notice" role="status">
    <p>{status.state === 'Merged' ? 'prdb merged this catalogue work' : 'prdb removed this catalogue work'}
      {day ? ` on ${day}` : ''}. Last known facts are retained.</p>
    {status.mergedIntoId && <p>Surviving prdb work: <code>{status.mergedIntoId}</code>.</p>}
    <p>{proposed ? 'This proposal is stale and cannot be accepted. Review or assign a current work explicitly.'
      : 'Local files, identification and personal viewing state remain intact. Playback follows local file availability.'}</p>
  </div>
}
