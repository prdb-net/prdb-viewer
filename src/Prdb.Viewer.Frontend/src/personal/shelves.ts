/// The three lists an Account keeps of its own, as the API names them.
export type Shelf = 'ContinueWatching' | 'Favourites' | 'WatchLater'

export type ShelfDescription = {
  /// The address the shelf is opened at.
  to: string
  title: string
  /// What the shelf holds, said under its heading.
  explanation: string
  /// What an empty shelf says, when nothing narrows it.
  empty: string
  /// How the search field names the shelf while it is open.
  search: string
  /// What the shelf's own order is called where the sort orders are offered.
  order: string
}

/// A Personal Shelf is the Library narrowed to what this Account keeps on it: the same search,
/// facets, order and paging, inside a set the Account chose. The three differ in what puts a Video
/// on them and in the order they keep, so they are one table rather than three screens; a fourth
/// shelf is a value in the API, a line here and a route.
export const shelves: Record<Shelf, ShelfDescription> = {
  ContinueWatching: {
    to: '/continue',
    title: 'Continue Watching',
    explanation: 'Videos you started and have not finished. Only you can see this.',
    empty: 'Nothing is part-watched. A Video you start appears here until you finish or dismiss it.',
    search: 'Search Continue Watching',
    order: 'Recently watched',
  },
  Favourites: {
    to: '/favourites',
    title: 'Favourites',
    explanation: 'The Videos you marked as your own. Only you can see this.',
    empty: 'No Video is a Favourite yet. The heart on a Video’s picture puts one here.',
    search: 'Search your Favourites',
    order: 'Recently added',
  },
  WatchLater: {
    to: '/watch-later',
    title: 'Watch Later',
    explanation: 'What you set aside for later, oldest first. Only you can see this.',
    empty: 'Nothing is set aside yet. The bookmark on a Video’s picture sets one aside.',
    search: 'Search Watch Later',
    order: 'Oldest added first',
  },
}

export const shelfNames = Object.keys(shelves) as Shelf[]

/// The shelf open at an address, if the address is one of the shelves.
export function shelfAt(pathname: string): Shelf | undefined {
  return shelfNames.find((shelf) => shelves[shelf].to === pathname)
}
