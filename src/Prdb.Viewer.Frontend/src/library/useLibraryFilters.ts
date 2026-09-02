import { useCallback, useEffect, useMemo, useRef } from 'react'
import { useSearchParams } from 'react-router'

import { emptyFilters, type LibraryFilters } from '../api/client'
import type { Shelf } from '../personal/shelves'

export type FacetKey = 'sites' | 'actors' | 'quality' | 'shelf'

/// The Library's search, facets, sort order and paging, kept in the URL.
///
/// ADR 0004: a filter that lives only in component state is a defect, because the address no
/// longer reproduces what the User was looking at. Everything here is therefore read from and
/// written to the query string, and the component holds none of it.
///
/// Paging is part of that: `pages` is how many pages have been revealed, so returning to the
/// address restores the same depth rather than the first page of it.
///
/// A shelf page pins its shelf: the route says which shelf is open, so the address does not repeat
/// it, and the shelf's own order is the default there rather than Newest. What is pinned is not a
/// narrowing — it is the set being narrowed — so it is not counted as one and cannot be cleared.
export function useLibraryFilters(pinned?: Shelf) {
  const [parameters, setParameters] = useSearchParams()
  const defaults = useMemo<LibraryFilters>(() => ({
    ...emptyFilters,
    sort: pinned ? 'ShelfOrder' : emptyFilters.sort,
    shelf: pinned ? [pinned] : [],
  }), [pinned])

  /// The address this hook last wrote, while that write has not come back as a render yet.
  ///
  /// React Router does not sequence two navigations raised in the same tick: the second one's
  /// updater still receives the location the first one started from, so the second overwrites the
  /// first instead of building on it. Two facets clicked quickly lost the earlier choice that way.
  /// Holding what was written — and dropping it the moment the address catches up — is what makes
  /// a second change in the same tick continue the first.
  const pending = useRef<string | null>(null)

  useEffect(() => {
    pending.current = null
  }, [parameters])

  /// Every write goes through here, on the pending address where there is one.
  const change = useCallback((edit: (next: URLSearchParams) => void) => {
    const next = new URLSearchParams(pending.current ?? parameters.toString())
    edit(next)
    pending.current = next.toString()
    setParameters(next, { replace: true })
  }, [parameters, setParameters])

  const filters = useMemo<LibraryFilters>(() => ({
    query: parameters.get('query') ?? '',
    sort: (parameters.get('sort') as LibraryFilters['sort']) || defaults.sort,
    sites: list(parameters.get('sites')),
    actors: list(parameters.get('actors')),
    unknownSite: parameters.get('unknownSite') === 'true',
    work: list(parameters.get('work')),
    review: list(parameters.get('review')),
    playability: list(parameters.get('playability')),
    availability: list(parameters.get('availability')),
    quality: list(parameters.get('quality')),
    playState: list(parameters.get('playState')),
    shelf: pinned ? [pinned] : list(parameters.get('shelf')),
  }), [parameters, pinned, defaults])

  const pages = Math.max(1, Number(parameters.get('pages') ?? 1) || 1)

  /// Narrowing returns to the first page, because the depth reached in a wider set says nothing
  /// about a narrower one.
  const narrow = useCallback((narrowing: Partial<LibraryFilters>) => {
    change((next) => {
      for (const [key, value] of Object.entries(narrowing)) {
        write(next, key, value, defaults)
      }
      next.delete('pages')
    })
  }, [change, defaults])

  /// Adds a value to a multi-valued facet, or takes it out again. Values inside one facet combine
  /// with OR, so a second Site widens the set rather than replacing the first.
  ///
  /// Which of the two it is has to be read from the address being written, for the same reason the
  /// list is. Taking it from the caller means taking it from the last render: two clicks on one
  /// facet inside a single batch both saw it unselected, so both added it, and the address ended
  /// up naming that Site twice while the button drew itself as unselected.
  const toggle = useCallback((key: FacetKey, value: string) => {
    change((next) => {
      const held = list(next.get(key))
      write(next, key, held.includes(value) ? held.filter((one) => one !== value) : [...held, value], defaults)
      next.delete('pages')
    })
  }, [change, defaults])

  const clear = useCallback(() => {
    pending.current = null
    setParameters(new URLSearchParams(), { replace: true })
  }, [setParameters])

  const showMore = useCallback(() => {
    change((next) => {
      next.set('pages', String(Math.max(1, Number(next.get('pages') ?? 1) || 1) + 1))
    })
  }, [change])

  const narrowed = filters.query.trim().length > 0 ||
    filters.sites.length > 0 ||
    filters.actors.length > 0 ||
    filters.unknownSite ||
    filters.work.length > 0 ||
    filters.review.length > 0 ||
    filters.playability.length > 0 ||
    filters.availability.length > 0 ||
    filters.quality.length > 0 ||
    filters.playState.length > 0 ||
    (!pinned && filters.shelf.length > 0)

  return { filters, pages, narrow, toggle, clear, showMore, narrowed }
}

function list(value: string | null) {
  return value ? value.split(',').filter(Boolean) : []
}

/// A default is written as absence. The address then names what was chosen rather than restating
/// what nobody chose, and a cleared filter leaves no trace to explain.
function write(parameters: URLSearchParams, key: string, value: unknown, defaults: LibraryFilters) {
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
  if (!text || text === defaults[key as keyof LibraryFilters]) parameters.delete(key)
  else parameters.set(key, text)
}
