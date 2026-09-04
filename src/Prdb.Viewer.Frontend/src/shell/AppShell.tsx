import { useEffect, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { NavLink, Outlet, useLocation, useNavigate, useSearchParams } from 'react-router'

import { api, type Account } from '../api/client'
import { friendlyState } from '../lib/format'
import { shelfAt, shelves } from '../personal/shelves'
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
  const drawerToggle = useRef<HTMLButtonElement>(null)
  // A grid of Videos is the one thing here that gets better with every column a screen can hold,
  // so the screens made of one take the whole width; prose and forms keep their measure.
  const wide = location.pathname === '/' || shelfAt(location.pathname) !== undefined

  // A narrow viewport navigates by opening the drawer, so arriving somewhere closes it again.
  // Arriving is the external event this synchronises with, which is what an effect is for.
  // oxlint-disable-next-line react/set-state-in-effect
  useEffect(() => setDrawerOpen(false), [location.pathname])

  /// An open drawer covers the screen, so it answers Escape the way anything covering the screen
  /// does, and returns the focus to the control that opened it.
  useEffect(() => {
    if (!drawerOpen) return

    const dismiss = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return
      setDrawerOpen(false)
      drawerToggle.current?.focus()
    }

    document.body.classList.add('drawer-open')
    window.addEventListener('keydown', dismiss)
    return () => {
      document.body.classList.remove('drawer-open')
      window.removeEventListener('keydown', dismiss)
    }
  }, [drawerOpen])

  return (
    <div className={drawerOpen ? 'app-shell drawer-open' : 'app-shell'}>
      <header className="app-header">
        <button
          ref={drawerToggle}
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

      <main className={wide ? 'app-content wide' : 'app-content'}>
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

/// How long typing settles before the address — and the request behind it — follows it.
const searchSettleMilliseconds = 250

/// Search belongs to the chrome rather than to one screen: it is how the Library is reached from
/// anywhere, and it puts what it finds in the URL so the result is the same page for everyone.
///
/// It searches where it is. On the browsing screen and everywhere that is not a list of Videos, it
/// searches the whole Library; on a Personal Shelf it searches that shelf, and says so, because
/// someone who opened their Favourites and typed a word was looking for one of their Favourites.
/// The shelf's screen offers the whole Library for the same words, one link away.
///
/// What is typed lives here rather than in the address, because a controlled field fed by a
/// navigation loses the keystrokes that arrive before that navigation renders. The address stays
/// the truth about what is being searched for; the field is what someone is still typing, and the
/// two meet once typing settles — which also spares the library a request per keystroke.
function GlobalSearch() {
  const [parameters] = useSearchParams()
  const location = useLocation()
  const navigate = useNavigate()
  const shelf = shelfAt(location.pathname)
  // The index of Actors is a list that is searched, like a shelf and unlike every other screen, so
  // typing on it looks for an Actor rather than leading away to the Library.
  const actors = location.pathname === '/actors'
  const scope = actors ? '/actors' : shelf ? shelves[shelf].to : '/'
  const onScope = location.pathname === scope
  const query = onScope ? (parameters.get('query') ?? '') : ''

  const [typed, setTyped] = useState(query)
  /// What this field last put in the address. It tells a change made here apart from one made
  /// elsewhere — Clear, the back button, a link — which is the only kind the field should follow.
  const published = useRef(query)

  useEffect(() => {
    if (query !== published.current) {
      published.current = query
      setTyped(query)
    }
  }, [query])

  useEffect(() => {
    if (typed === published.current) return

    const timer = window.setTimeout(() => {
      published.current = typed
      const next = new URLSearchParams(onScope ? parameters : undefined)
      if (typed) {
        next.set('query', typed)
      } else {
        next.delete('query')
      }
      next.delete('pages')
      // Refining one search does not fill the history with every keystroke, but arriving at the
      // Library from elsewhere is a step worth being able to go back from.
      void navigate({ pathname: scope, search: next.toString() }, { replace: onScope })
    }, searchSettleMilliseconds)

    return () => window.clearTimeout(timer)
  }, [typed, onScope, scope, parameters, navigate])

  const name = actors
    ? 'Search the Actors'
    : shelf ? shelves[shelf].search : 'Search the library'

  return (
    <label className="global-search">
      <span className="visually-hidden">{name}</span>
      <input
        type="search"
        name="query"
        id="global-search"
        autoComplete="off"
        value={typed}
        placeholder={shelf || actors ? name : 'Search title, site, actor or file name'}
        onChange={(event) => setTyped(event.target.value)}
      />
    </label>
  )
}

/// Who is signed in, and the two things that follow from it, behind one control.
///
/// The header used to carry the name as a link and Sign out as a button standing permanently
/// beside it: two controls for one subject, and an irreversible one given the same standing as a
/// destination. It is the corner every application puts the account in, so it behaves the way that
/// corner behaves elsewhere — an avatar that opens a small menu, with the identity stated at its
/// head and Sign out at its foot, apart from what merely navigates.
///
/// A disclosure rather than an ARIA menu: what it holds is a link and a button, which the Tab key
/// already reaches in order. Claiming `role="menu"` would promise arrow-key navigation that this
/// does not implement, and a promise the keyboard does not keep is worse than none.
function AccountMenu({ account }: { account: Account }) {
  const [open, setOpen] = useState(false)
  const menu = useRef<HTMLDivElement>(null)
  const toggle = useRef<HTMLButtonElement>(null)
  const location = useLocation()
  const queryClient = useQueryClient()
  const signOut = useMutation({
    mutationFn: () => api.signOut(account.csrfToken),
    onSuccess: () => {
      queryClient.setQueryData(queryKeys.state, { claimed: true, signedIn: false })
      queryClient.removeQueries({ queryKey: queryKeys.account })
    },
  })

  // Following the link inside it has arrived somewhere; the menu it was chosen from has no reason
  // to stay over the screen it led to.
  // oxlint-disable-next-line react/set-state-in-effect
  useEffect(() => setOpen(false), [location.pathname])

  /// An open menu answers Escape and a press beside it, and Escape returns the focus to the
  /// control that opened it — the same bargain the drawer and the filter sheet make.
  useEffect(() => {
    if (!open) return

    const dismiss = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return
      setOpen(false)
      toggle.current?.focus()
    }
    const dismissOutside = (event: PointerEvent) => {
      if (!menu.current?.contains(event.target as Node)) setOpen(false)
    }

    window.addEventListener('keydown', dismiss)
    document.addEventListener('pointerdown', dismissOutside)
    return () => {
      window.removeEventListener('keydown', dismiss)
      document.removeEventListener('pointerdown', dismissOutside)
    }
  }, [open])

  return (
    <div className="account-menu" ref={menu}>
      <button
        ref={toggle}
        className="account-avatar"
        aria-expanded={open}
        aria-controls="account-popover"
        onClick={() => setOpen((shown) => !shown)}
      >
        <span aria-hidden="true">{account.username.slice(0, 1).toUpperCase()}</span>
        <span className="visually-hidden">{`Account: ${account.username}`}</span>
      </button>

      {open && (
        <div className="account-popover" id="account-popover">
          {/* The name the header no longer has room to show. It is stated rather than linked: the
              entry below it leads to the same place, and one destination needs one control. */}
          <div className="account-identity">
            <strong>{account.username}</strong>
            <span>{friendlyState(account.authority)}</span>
          </div>
          <NavLink to="/account" className="account-action">Your Account</NavLink>
          <button
            className="account-action leaving"
            onClick={() => signOut.mutate()}
            disabled={signOut.isPending}
          >
            {signOut.isPending ? 'Signing out…' : 'Sign out'}
          </button>
        </div>
      )}
    </div>
  )
}

type NavigationBadges = {
  operationalAttention: number
  identificationQueue: number
  accountsWaiting: number
}

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
  // A request for access waits for a person, the same way a candidate does. The Accounts screen
  // opened with "1 request waiting for approval" while the navigation that leads to it said
  // nothing, so the one section that cannot act on its own was the one nothing pointed at.
  const accounts = useQuery({
    queryKey: queryKeys.accounts,
    queryFn: api.accounts,
    enabled: administrator,
    refetchInterval: 60_000,
  })

  return {
    operationalAttention: Number(work.data?.operationalAttentionCount ?? 0),
    identificationQueue: queue.data?.length ?? 0,
    accountsWaiting:
      accounts.data?.filter((candidate) => candidate.state === 'PendingApproval').length ?? 0,
  }
}

function badgeFor(entry: NavigationEntry, badges: NavigationBadges) {
  return entry.badge ? badges[entry.badge] : 0
}
