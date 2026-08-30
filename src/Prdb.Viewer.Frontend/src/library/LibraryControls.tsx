import type { ReactNode } from 'react'

import type { LibraryFacets, LibraryFilters } from '../api/client'
import { qualityBandLabel } from '../lib/quality'

/// The sort order and the facets. Search itself is not here: it belongs to the shell, because it
/// is how the Library is reached from anywhere rather than something this screen owns.
export function LibraryControls({
  filters,
  facets,
  narrow,
  toggle,
  clear,
  narrowed,
}: {
  filters: LibraryFilters
  facets?: LibraryFacets
  narrow: (change: Partial<LibraryFilters>) => void
  /// Values inside one facet combine with OR, so a Site or an Actor is added to what is already
  /// chosen rather than replacing it. It goes through the address rather than through `narrow`,
  /// which would compute the new list from a render that a quick second click has already outrun.
  toggle: (key: 'sites' | 'actors' | 'quality', value: string) => void
  clear: () => void
  narrowed: boolean
}) {
  return (
    <div className="library-controls">
      <div className="library-search">
        <label className="field">
          <span>Sort</span>
          <select
            value={filters.sort}
            onChange={(event) => narrow({ sort: event.target.value as LibraryFilters['sort'] })}
          >
            <option value="Newest">Newest</option>
            <option value="TitleAscending">Title A–Z</option>
            <option value="QualityDescending">Best quality first</option>
          </select>
        </label>
        {narrowed && <button className="quiet-button" onClick={clear}>Clear</button>}
      </div>
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
      {facets?.quality?.length ? (
        <FacetGroup label="Quality">
          {facets.quality.map((band) => {
            const label = qualityBandLabel(band.value)
            if (!label) return null

            return (
              <FacetToggle
                key={band.value}
                label={`${label} (${band.count})`}
                selected={filters.quality.includes(band.value)}
                onToggle={() => toggle('quality', band.value)}
              />
            )
          })}
        </FacetGroup>
      ) : null}
      {facets?.sites?.length ? (
        <FacetGroup label="Sites">
          {facets.sites.map((site) => (
            <FacetToggle
              key={site.value}
              label={`${site.value} (${site.count})`}
              selected={filters.sites.includes(site.value)}
              onToggle={() => toggle('sites', site.value)}
            />
          ))}
        </FacetGroup>
      ) : null}
      {facets?.actors?.length ? (
        <FacetGroup label="Actors">
          {facets.actors.map((actor) => (
            <FacetToggle
              key={actor.value}
              label={`${actor.value} (${actor.count})`}
              selected={filters.actors.includes(actor.value)}
              onToggle={() => toggle('actors', actor.value)}
            />
          ))}
        </FacetGroup>
      ) : null}
    </div>
  )
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
