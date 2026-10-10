import type { components } from '../api/schema'

type Site = components['schemas']['RemoteSite']

export function CatalogueSiteFacts({ site }: { site?: Site | null }) {
  if (!site) return null
  const links = (site.links ?? []).filter((link) => safeUrl(link.url))
  return (
    <>
      {site.platformTitle && <p>Creator Platform: {site.platformTitle}</p>}
      {site.accountHandle && <p>Creator Account Handle: {site.accountHandle}</p>}
      {links.length > 0 && (
        <ul aria-label="Site links">
          {links.map((link) => (
            <li key={link.url}>
              <a href={link.url} target="_blank" rel="noreferrer noopener">{link.label || link.url} ↗</a>
            </li>
          ))}
        </ul>
      )}
    </>
  )
}

function safeUrl(value: string) {
  try {
    const url = new URL(value)
    return ['http:', 'https:'].includes(url.protocol) && !url.username && !url.password
  } catch { return false }
}
