import type { LibraryFacets, LibraryFilters } from '../api/client'

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
  toggle: (key: 'sites' | 'actors', value: string) => void
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
              onToggle={() => toggle('sites', site.value)}
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
              onToggle={() => toggle('actors', actor.value)}
            />
          ))}
        </div>
      ) : null}
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
