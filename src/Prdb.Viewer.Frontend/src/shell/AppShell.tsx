import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { NavLink, Outlet, useLocation, useNavigate, useSearchParams } from 'react-router'

import { api, type Account } from '../api/client'
import { queryKeys } from '../queryKeys'
import { Brand } from '../ui'
import { visibleGroups, type NavigationEntry } from './navigation'

/// The application's chrome: what is always there, whichever screen is open.
///
/// It owns navigation and identity and nothing else. Every screen is a route beneath it, so a new
/// destination costs a line of navigation and a route rather than another section stacked onto a
/// page that already carries several.
export function AppShell({ account }: { account: Account }) {
  const [drawerOpen, setDrawerOpen] = useState(false)
  const location = useLocation()
  const badges = useNavigationBadges(account)

  // A narrow viewport navigates by opening the drawer, so arriving somewhere closes it again.
  useEffect(() => setDrawerOpen(false), [location.pathname])

  return (
    <div className={drawerOpen ? 'app-shell drawer-open' : 'app-shell'}>
      <header className="app-header">
        <button
          className="drawer-toggle"
          aria-expanded={drawerOpen}
          aria-controls="main-navigation"
          onClick={() => setDrawerOpen((open) => !open)}
        >
          <span aria-hidden="true">☰</span>
          <span className="visually-hidden">{drawerOpen ? 'Close navigation' : 'Open navigation'}</span>
        </button>
        <Brand compact />
        <GlobalSearch />
        <AccountMenu account={account} />
      </header>

      <nav className="app-navigation" id="main-navigation" aria-label="Main">
        {visibleGroups(account).map((group) => (
          <div className="navigation-group" key={group.title}>
            <span className="navigation-title">{group.title}</span>
            <ul>
              {group.entries.map((entry) => (
                <li key={entry.to}>
                  <NavigationItem entry={entry} badge={badgeFor(entry, badges)} />
                </li>
              ))}
            </ul>
          </div>
        ))}
      </nav>

      {/* Closing by clicking beside the drawer is a convenience the toggle already provides in an
          accessible way, so this stays a presentational surface rather than a control. */}
      <div className="drawer-scrim" onClick={() => setDrawerOpen(false)} aria-hidden="true" />

      <main className="app-content">
        <Outlet />
      </main>
    </div>
  )
}

function NavigationItem({ entry, badge }: { entry: NavigationEntry; badge: number }) {
  return (
    <NavLink
      to={entry.to}
      end={entry.end}
      className={({ isActive }) => (isActive ? 'navigation-item active' : 'navigation-item')}
    >
      <span>{entry.label}</span>
      {badge > 0 && (
        <span className="navigation-badge" aria-label={`${badge} waiting`}>{badge}</span>
      )}
    </NavLink>
  )
}

/// Search belongs to the chrome rather than to one screen: it is how the Library is reached from
/// anywhere, and it puts what it finds in the URL so the result is the same page for everyone.
function GlobalSearch() {
  const [parameters] = useSearchParams()
  const location = useLocation()
  const navigate = useNavigate()
  const onLibrary = location.pathname === '/'
  const query = onLibrary ? (parameters.get('query') ?? '') : ''

  return (
    <label className="global-search">
      <span className="visually-hidden">Search the library</span>
      <input
        type="search"
        value={query}
        placeholder="Search title, site, actor or file name"
        onChange={(event) => {
          const next = new URLSearchParams(onLibrary ? parameters : undefined)
          if (event.target.value) {
            next.set('query', event.target.value)
          } else {
            next.delete('query')
          }
          next.delete('pages')
          // Typing refines one search rather than filling the history with every keystroke, but
          // arriving at the Library from elsewhere is a step worth being able to go back from.
          void navigate({ pathname: '/', search: next.toString() }, { replace: onLibrary })
        }}
      />
    </label>
  )
}

function AccountMenu({ account }: { account: Account }) {
  const queryClient = useQueryClient()
  const signOut = useMutation({
    mutationFn: () => api.signOut(account.csrfToken),
    onSuccess: () => {
      queryClient.setQueryData(queryKeys.state, { claimed: true, signedIn: false })
      queryClient.removeQueries({ queryKey: queryKeys.account })
    },
  })

  return (
    <div className="account-menu">
      <NavLink to="/account" className="account-link">{account.username}</NavLink>
      <button className="quiet-button" onClick={() => signOut.mutate()} disabled={signOut.isPending}>
        Sign out
      </button>
    </div>
  )
}

type NavigationBadges = { operationalAttention: number; identificationQueue: number }

/// What the navigation needs to count, asked for only where it can be answered.
///
/// The intervals are the shell's own: slower than an open screen's, because a badge is a reason to
/// look rather than a live view. An open screen observes the same keys more often, and Query gives
/// both the faster of the two while it is there.
function useNavigationBadges(account: Account): NavigationBadges {
  const administrator = account.authority === 'Administrator'
  const work = useQuery({
    queryKey: queryKeys.backgroundWork,
    queryFn: api.backgroundWork,
    enabled: administrator,
    refetchInterval: 30_000,
  })
  const queue = useQuery({
    queryKey: queryKeys.identificationQueue,
    queryFn: api.identificationQueue,
    enabled: administrator,
    refetchInterval: 60_000,
  })

  return {
    operationalAttention: Number(work.data?.operationalAttentionCount ?? 0),
    identificationQueue: queue.data?.length ?? 0,
  }
}

function badgeFor(entry: NavigationEntry, badges: NavigationBadges) {
  return entry.badge ? badges[entry.badge] : 0
}
