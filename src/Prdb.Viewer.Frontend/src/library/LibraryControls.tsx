import { useState, type ReactNode } from 'react'

import type { LibraryFacets, LibraryFilters } from '../api/client'
import { qualityBandLabel } from '../lib/quality'
import { shelfNames, shelves, type Shelf } from '../personal/shelves'
import type { FacetKey } from './useLibraryFilters'

/// How many values a facet row offers before the rest wait behind one control.
///
/// A library of forty Videos had twenty-seven Sites and twenty-eight Actors, most of them holding
/// one Video each, and the rows of them pushed the first Video off the first screen. The most
/// populated values are the ones worth a click; the long tail is still there, one control away.
const facetPreview = 8

/// The playability values that together admit everything, which is what revealing the unavailable
/// matches asks for. Seen in the address, they are one choice rather than three.
const everyPlayability = ['ReadyForDirectPlay', 'CompatibilityUncertain', 'NotDirectlyPlayable']

/// The sort order, what is chosen, and the facets. Search itself is not here: it belongs to the
/// shell, because it is how the Library is reached from anywhere rather than something this
/// screen owns.
///
/// The Personal Shelves are a facet like the others on the browsing screen, so unplayed Favourites
/// from one Site is a question it can answer. On a shelf's own page the shelf is pinned: the heading
/// says it, so the facet is not offered again and the shelf is not listed among what can be taken
/// out.
export function LibraryControls({
  filters,
  facets,
  narrow,
  toggle,
  clear,
  narrowed,
  pinned,
  total,
}: {
  filters: LibraryFilters
  facets?: LibraryFacets
  narrow: (change: Partial<LibraryFilters>) => void
  /// Values inside one facet combine with OR, so a Site or an Actor is added to what is already
  /// chosen rather than replacing it. It goes through the address rather than through `narrow`,
  /// which would compute the new list from a render that a quick second click has already outrun.
  toggle: (key: FacetKey, value: string) => void
  clear: () => void
  narrowed: boolean
  /// The shelf this screen is, when it is one.
  pinned?: Shelf
  /// How many Videos the current narrowing admits.
  total: number
}) {
  // On a narrow screen the facets wait behind one control, closed until asked for: what is chosen
  // is already said in the row above, and the Videos are what the screen is for.
  const [facetsOpen, setFacetsOpen] = useState(false)
  const chosen = chosenFilters(filters, narrow, toggle, pinned)
  // A shelf keeps an order of its own, named for what it is on that shelf; with several chosen
  // the name has to cover them all.
  const shelfOrder = pinned
    ? shelves[pinned].order
    : filters.shelf.length > 0 ? 'Shelf order' : undefined

  return (
    <div className="library-controls">
      <div className="library-toolbar">
        <span className="result-count" role="status">
          {total} {narrowed ? 'matching' : total === 1 ? 'Video' : 'Videos'}
        </span>
        <div className="toolbar-actions">
          <button
            className="quiet-button filters-toggle"
            aria-expanded={facetsOpen}
            aria-controls="library-facets"
            onClick={() => setFacetsOpen((open) => !open)}
          >
            Filters{chosen.length > 0 && ` (${chosen.length})`}
          </button>
          <label className="sort-field">
            <span>Sort</span>
            <select
              value={filters.sort}
              onChange={(event) => narrow({ sort: event.target.value as LibraryFilters['sort'] })}
            >
              <option value="Newest">Newest</option>
              <option value="TitleAscending">Title A–Z</option>
              <option value="QualityDescending">Best quality first</option>
              <option value="LongestFirst">Longest first</option>
              <option value="RecentlyPlayed">Recently played</option>
              <option value="BestRated">Best rated</option>
              {shelfOrder && <option value="ShelfOrder">{shelfOrder}</option>}
            </select>
          </label>
        </div>
      </div>

      {/* What is chosen, in one row, each with the control that takes it out again. The pills
          below say the same thing, but across several rows and only for the values on offer. */}
      {chosen.length > 0 && (
        <ul className="active-filters" aria-label="Active filters">
          {chosen.map((filter) => (
            <li key={filter.key}>
              <button
                className="active-filter"
                aria-label={`Remove ${filter.label}`}
                onClick={filter.remove}
              >
                {filter.label}
                <span className="remove" aria-hidden="true">×</span>
              </button>
            </li>
          ))}
          <li><button className="clear-filters" onClick={clear}>Clear all</button></li>
        </ul>
      )}

      <div id="library-facets" className={facetsOpen ? 'facet-groups open' : 'facet-groups'}>
        {!pinned && (
          <FacetGroup label="Yours">
            {shelfNames.map((shelf) => (
              <FacetToggle
                key={shelf}
                label={shelves[shelf].title}
                selected={filters.shelf.includes(shelf)}
                onToggle={() => toggle('shelf', shelf)}
              />
            ))}
          </FacetGroup>
        )}
        <FacetGroup label="Only show">
          <FacetToggle
            label="Unknown work"
            selected={filters.work.includes('Unknown')}
            onToggle={(selected) => narrow({ work: selected ? ['Unknown'] : [] })}
          />
          <FacetToggle
            label="Unknown site"
            selected={filters.unknownSite}
            onToggle={(selected) => narrow({ unknownSite: selected })}
          />
          <FacetToggle
            label="Needs review"
            selected={filters.review.includes('ReviewNeeded')}
            onToggle={(selected) => narrow({ review: selected ? ['ReviewNeeded'] : [] })}
          />
          <FacetToggle
            label="Unplayed"
            selected={filters.playState.includes('Unplayed')}
            onToggle={(selected) => narrow({ playState: selected ? ['Unplayed'] : [] })}
          />
          <FacetToggle
            label="Unsupported only"
            selected={filters.playability.includes('NotDirectlyPlayable')}
            onToggle={(selected) => narrow({ playability: selected ? ['NotDirectlyPlayable'] : [] })}
          />
        </FacetGroup>
        {/* The bands the library actually holds, best first. A band it has none of is not offered:
            Video Quality is what the library holds rather than what this browser would be shown, so
            a band with a count has Videos in it whatever the browser. */}
        <FacetValues
          label="Quality"
          values={(facets?.quality ?? []).flatMap((band) => {
            const name = qualityBandLabel(band.value)
            return name ? [{ value: band.value, name, count: Number(band.count) }] : []
          })}
          selected={filters.quality}
          onToggle={(value) => toggle('quality', value)}
        />
        <FacetValues
          label="Sites"
          values={(facets?.sites ?? []).map((site) => ({
            value: site.value,
            name: site.value,
            count: Number(site.count),
          }))}
          selected={filters.sites}
          onToggle={(value) => toggle('sites', value)}
        />
        <FacetValues
          label="Actors"
          values={(facets?.actors ?? []).map((actor) => ({
            value: actor.value,
            name: actor.value,
            count: Number(actor.count),
          }))}
          selected={filters.actors}
          onToggle={(value) => toggle('actors', value)}
        />
      </div>
    </div>
  )
}

type ChosenFilter = { key: string; label: string; remove: () => void }

/// Everything the address narrows by, as one list of things that can be taken out again.
function chosenFilters(
  filters: LibraryFilters,
  narrow: (change: Partial<LibraryFilters>) => void,
  toggle: (key: FacetKey, value: string) => void,
  pinned?: Shelf,
): ChosenFilter[] {
  const chosen: ChosenFilter[] = []
  const query = filters.query.trim()

  if (query) chosen.push({ key: 'query', label: `“${query}”`, remove: () => narrow({ query: '' }) })
  if (!pinned) {
    for (const shelf of filters.shelf) {
      chosen.push({
        key: `shelf:${shelf}`,
        label: shelves[shelf as Shelf]?.title ?? shelf,
        remove: () => toggle('shelf', shelf),
      })
    }
  }
  if (filters.work.includes('Unknown')) {
    chosen.push({ key: 'work', label: 'Unknown work', remove: () => narrow({ work: [] }) })
  }
  if (filters.unknownSite) {
    chosen.push({ key: 'unknownSite', label: 'Unknown site', remove: () => narrow({ unknownSite: false }) })
  }
  if (filters.review.includes('ReviewNeeded')) {
    chosen.push({ key: 'review', label: 'Needs review', remove: () => narrow({ review: [] }) })
  }
  if (filters.playState.includes('Unplayed')) {
    chosen.push({ key: 'playState', label: 'Unplayed', remove: () => narrow({ playState: [] }) })
  }
  if (filters.availability.includes('Unavailable')) {
    // Revealing the unavailable matches widens playability alongside, so taking the one out takes
    // the other with it rather than leaving a widening nobody asked for on its own.
    chosen.push({
      key: 'availability',
      label: 'Unavailable',
      remove: () => narrow({ availability: [], playability: [] }),
    })
  } else if (filters.playability.includes('NotDirectlyPlayable') && filters.playability.length === 1) {
    chosen.push({ key: 'playability', label: 'Unsupported only', remove: () => narrow({ playability: [] }) })
  } else if (filters.playability.length > 0 && !everyPlayability.every((value) => filters.playability.includes(value))) {
    chosen.push({ key: 'playability', label: 'Any playability', remove: () => narrow({ playability: [] }) })
  }
  for (const band of filters.quality) {
    chosen.push({
      key: `quality:${band}`,
      label: qualityBandLabel(band as Parameters<typeof qualityBandLabel>[0]) ?? band,
      remove: () => toggle('quality', band),
    })
  }
  for (const site of filters.sites) {
    chosen.push({ key: `site:${site}`, label: site, remove: () => toggle('sites', site) })
  }
  for (const actor of filters.actors) {
    chosen.push({ key: `actor:${actor}`, label: actor, remove: () => toggle('actors', actor) })
  }

  return chosen
}

/// One row of facet values, with the question it answers written beside it.
///
/// The rows used to name themselves only to a screen reader: four rows of pills sat under each
/// other, and "1080p (1)" next to "Alex Doe (2)" left the reader to work out that one was a
/// quality and the other a person. The label carries the same name the group is announced with,
/// so both readings say the same thing.
function FacetGroup({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="facet-group" role="group" aria-label={label}>
      <span className="facet-label">{label}</span>
      <div className="facet-row">{children}</div>
    </div>
  )
}

type FacetValue = { value: string; name: string; count: number }

/// A facet whose values the library supplies, the most populated first and the rest behind one
/// control. A value that is chosen is always shown, wherever it ranks, and a chosen value the
/// current narrowing no longer counts is shown holding nothing rather than vanishing while it
/// still narrows the Library.
function FacetValues({ label, values, selected, onToggle }: {
  label: string
  values: FacetValue[]
  selected: string[]
  onToggle: (value: string) => void
}) {
  const [expanded, setExpanded] = useState(false)
  const offered = [
    ...values,
    ...selected
      .filter((value) => !values.some((offer) => offer.value === value))
      .map((value) => ({ value, name: qualityBandLabel(value as never) ?? value, count: 0 })),
  ]

  if (offered.length === 0) return null

  const hidden = expanded
    ? []
    : offered.slice(facetPreview).filter((offer) => !selected.includes(offer.value))
  const shown = offered.filter((offer) => !hidden.includes(offer))

  return (
    <FacetGroup label={label}>
      {shown.map((offer) => (
        <FacetToggle
          key={offer.value}
          label={`${offer.name} (${offer.count})`}
          selected={selected.includes(offer.value)}
          onToggle={() => onToggle(offer.value)}
        />
      ))}
      {(hidden.length > 0 || expanded) && offered.length > facetPreview && (
        <button
          className="facet-more"
          aria-expanded={expanded}
          onClick={() => setExpanded((open) => !open)}
        >
          {expanded ? 'Show fewer' : `Show all ${offered.length}`}
        </button>
      )}
    </FacetGroup>
  )
}

function FacetToggle({ label, selected, onToggle }: {
  label: string
  selected: boolean
  onToggle: (selected: boolean) => void
}) {
  return (
    <button
      className={selected ? 'facet selected' : 'facet'}
      aria-pressed={selected}
      onClick={() => onToggle(!selected)}
    >
      {label}
    </button>
  )
}
