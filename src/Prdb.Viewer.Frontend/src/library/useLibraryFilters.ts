import { useCallback, useMemo } from 'react'
import { useSearchParams } from 'react-router'

import { emptyFilters, type LibraryFilters } from '../api/client'

/// The Library's search, facets, sort order and paging, kept in the URL.
///
/// ADR 0004: a filter that lives only in component state is a defect, because the address no
/// longer reproduces what the User was looking at. Everything here is therefore read from and
/// written to the query string, and the component holds none of it.
///
/// Paging is part of that: `pages` is how many pages have been revealed, so returning to the
/// address restores the same depth rather than the first page of it.
export function useLibraryFilters() {
  const [parameters, setParameters] = useSearchParams()

  const filters = useMemo<LibraryFilters>(() => ({
    query: parameters.get('query') ?? '',
    sort: (parameters.get('sort') as LibraryFilters['sort']) || emptyFilters.sort,
    sites: list(parameters.get('sites')),
    actors: list(parameters.get('actors')),
    unknownSite: parameters.get('unknownSite') === 'true',
    work: list(parameters.get('work')),
    review: list(parameters.get('review')),
    playability: list(parameters.get('playability')),
    availability: list(parameters.get('availability')),
    playState: list(parameters.get('playState')),
  }), [parameters])

  const pages = Math.max(1, Number(parameters.get('pages') ?? 1) || 1)

  /// Narrowing returns to the first page, because the depth reached in a wider set says nothing
  /// about a narrower one.
  const narrow = useCallback((change: Partial<LibraryFilters>) => {
    setParameters((current) => {
      const next = new URLSearchParams(current)
      for (const [key, value] of Object.entries(change)) {
        write(next, key, value)
      }
      next.delete('pages')
      return next
    }, { replace: true })
  }, [setParameters])

  const clear = useCallback(() => {
    setParameters(new URLSearchParams(), { replace: true })
  }, [setParameters])

  const showMore = useCallback(() => {
    setParameters((current) => {
      const next = new URLSearchParams(current)
      next.set('pages', String(Math.max(1, Number(next.get('pages') ?? 1) || 1) + 1))
      return next
    }, { replace: true })
  }, [setParameters])

  const narrowed = filters.query.trim().length > 0 ||
    filters.sites.length > 0 ||
    filters.actors.length > 0 ||
    filters.unknownSite ||
    filters.work.length > 0 ||
    filters.review.length > 0 ||
    filters.playability.length > 0 ||
    filters.availability.length > 0 ||
    filters.playState.length > 0

  return { filters, pages, narrow, clear, showMore, narrowed }
}

function list(value: string | null) {
  return value ? value.split(',').filter(Boolean) : []
}

/// A default is written as absence. The address then names what was chosen rather than restating
/// what nobody chose, and a cleared filter leaves no trace to explain.
function write(parameters: URLSearchParams, key: string, value: unknown) {
  if (Array.isArray(value)) {
    if (value.length === 0) parameters.delete(key)
    else parameters.set(key, value.join(','))
    return
  }

  if (typeof value === 'boolean') {
    if (value) parameters.set(key, 'true')
    else parameters.delete(key)
    return
  }

  const text = typeof value === 'string' ? value : ''
  if (!text || text === emptyFilters[key as keyof LibraryFilters]) parameters.delete(key)
  else parameters.set(key, text)
}
