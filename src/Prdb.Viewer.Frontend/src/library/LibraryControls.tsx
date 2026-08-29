import type { LibraryFacets, LibraryFilters } from '../api/client'

/// The sort order and the facets. Search itself is not here: it belongs to the shell, because it
/// is how the Library is reached from anywhere rather than something this screen owns.
export function LibraryControls({
  filters,
  facets,
  narrow,
  clear,
  narrowed,
}: {
  filters: LibraryFilters
  facets?: LibraryFacets
  narrow: (change: Partial<LibraryFilters>) => void
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
          </select>
        </label>
        {narrowed && <button className="quiet-button" onClick={clear}>Clear</button>}
      </div>
      <div className="facet-row">
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
      </div>
      {facets?.sites?.length ? (
        <div className="facet-row" aria-label="Sites">
          {facets.sites.map((site) => (
            <FacetToggle
              key={site.value}
              label={`${site.value} (${site.count})`}
              selected={filters.sites.includes(site.value)}
              onToggle={(selected) => narrow({
                sites: withValue(filters.sites, site.value, selected),
              })}
            />
          ))}
        </div>
      ) : null}
      {facets?.actors?.length ? (
        <div className="facet-row" aria-label="Actors">
          {facets.actors.map((actor) => (
            <FacetToggle
              key={actor.value}
              label={`${actor.value} (${actor.count})`}
              selected={filters.actors.includes(actor.value)}
              onToggle={(selected) => narrow({
                actors: withValue(filters.actors, actor.value, selected),
              })}
            />
          ))}
        </div>
      ) : null}
    </div>
  )
}

/// One more chosen value, or one fewer.
///
/// Values inside one facet combine with OR, so selecting a second Site widens the set rather than
/// replacing the first. These controls looked like several could be on at once and behaved as a
/// choice of one, which quietly discarded the earlier selection.
function withValue(chosen: string[], value: string, selected: boolean) {
  return selected ? [...chosen, value] : chosen.filter((held) => held !== value)
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
