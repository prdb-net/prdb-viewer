import { useQuery } from '@tanstack/react-query'
import { Navigate, Route, Routes } from 'react-router'

import { api, type Account } from './api/client'
import { AccessPanel } from './access/AccessPanel'
import { BootstrapPanel } from './access/BootstrapPanel'
import { AccountPage } from './account/AccountPage'
import { ActorPage } from './actor/ActorPage'
import { ActorsPage } from './actor/ActorsPage'
import { AccountsPage } from './admin/AccountsPage'
import { IdentificationPage } from './admin/IdentificationPage'
import { SetupPage } from './admin/SetupPage'
import { WorkPage } from './admin/WorkPage'
import { LibraryPage } from './library/LibraryPage'
import { shelfNames, shelves } from './personal/shelves'
import { queryKeys } from './queryKeys'
import { AppShell } from './shell/AppShell'
import { CenteredCard, Notice } from './ui'
import { useClientQualification } from './video/useClientQualification'
import { VideoPage } from './video/VideoPage'

/// What the application is before it knows who is asking.
///
/// Three states precede every screen: an installation nobody has claimed, a visitor who is not
/// signed in, and an Account. Only the third has navigation, so the shell and its routes begin
/// here rather than at the router.
export function App() {
  const state = useQuery({ queryKey: queryKeys.state, queryFn: api.state, retry: false })
  const account = useQuery({
    queryKey: queryKeys.account,
    queryFn: api.me,
    enabled: state.data?.signedIn === true,
    staleTime: Number.POSITIVE_INFINITY,
    retry: false,
  })

  if (state.isPending || (state.data?.signedIn && account.isPending)) {
    return <CenteredCard><p role="status">Opening your library…</p></CenteredCard>
  }

  if (state.isError || account.isError) {
    return <CenteredCard><Notice kind="error">The viewer could not reach its API. Try again shortly.</Notice></CenteredCard>
  }

  if (!state.data.claimed) {
    return <BootstrapPanel />
  }

  if (!state.data.signedIn || !account.data) {
    return <AccessPanel />
  }

  return <SignedIn account={account.data} />
}

/// Every screen an Account can reach, in one route definition.
///
/// ADR 0004: one definition rather than parallel lists that can drift, so the navigation and the
/// routes cannot disagree about what exists. Administrator routes are guarded here as well as at
/// the API, because a URL typed by hand should meet the same answer as a hidden menu entry.
function SignedIn({ account }: { account: Account }) {
  useClientQualification(account)
  const administrator = account.authority === 'Administrator'

  return (
    <Routes>
      <Route element={<AppShell account={account} />}>
        <Route index element={<LibraryPage account={account} />} />
        <Route path="videos/:videoId" element={<VideoPage account={account} />} />
        <Route path="actors" element={<ActorsPage />} />
        <Route path="actors/:actorId" element={<ActorPage account={account} />} />
        {/* A shelf is the Library narrowed to it, so it is the Library's screen with the shelf
            pinned rather than a screen of its own. */}
        {shelfNames.map((shelf) => (
          <Route
            key={shelf}
            path={shelves[shelf].to.slice(1)}
            element={<LibraryPage account={account} shelf={shelf} />}
          />
        ))}
        <Route path="account" element={<AccountPage account={account} />} />
        {administrator && (
          <Route path="admin">
            <Route path="setup" element={<SetupPage account={account} />} />
            <Route path="identification" element={<IdentificationPage account={account} />} />
            <Route path="work" element={<WorkPage account={account} />} />
            <Route path="accounts" element={<AccountsPage account={account} />} />
          </Route>
        )}
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  )
}
