import type { Account } from '../api/client'

/// One destination in the application's navigation.
///
/// A badge is a count the entry carries when it is not zero, so a section that needs someone can
/// say so from the navigation rather than only once the page is open.
export type NavigationEntry = {
  to: string
  label: string
  /// Which Accounts see this entry at all. Authority decides visibility here as well as at the
  /// API, because an entry that leads to a refused request is worse than no entry.
  authority?: Account['authority']
  badge?: 'operationalAttention' | 'identificationQueue'
  /// True when the route matches only its exact path. The Library is the shell's index route and
  /// would otherwise stay marked while any of its children is open.
  end?: boolean
}

export type NavigationGroup = {
  title: string
  entries: NavigationEntry[]
}

/// The whole of the application's navigation, in one list.
///
/// It is a list of groups rather than a tree of screens because that is what the sidebar can grow
/// into: a new destination is a line here and a route, and needs no decision about where the
/// chrome puts it. Groups are ordered by who uses them and how often, not alphabetically.
export const navigation: NavigationGroup[] = [
  {
    title: 'Library',
    entries: [
      { to: '/', label: 'Browse', end: true },
      { to: '/continue', label: 'Continue Watching' },
      { to: '/favourites', label: 'Favourites' },
      { to: '/watch-later', label: 'Watch Later' },
    ],
  },
  {
    title: 'Administration',
    entries: [
      { to: '/admin/setup', label: 'Installation', authority: 'Administrator' },
      {
        to: '/admin/identification',
        label: 'Identification',
        authority: 'Administrator',
        badge: 'identificationQueue',
      },
      {
        to: '/admin/work',
        label: 'Background work',
        authority: 'Administrator',
        badge: 'operationalAttention',
      },
      { to: '/admin/accounts', label: 'Accounts', authority: 'Administrator' },
    ],
  },
]

export function visibleGroups(account: Account): NavigationGroup[] {
  return navigation
    .map((group) => ({
      ...group,
      entries: group.entries.filter(
        (entry) => entry.authority === undefined || entry.authority === account.authority,
      ),
    }))
    .filter((group) => group.entries.length > 0)
}
