import { fireEvent, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import {
  isLibraryRequest,
  isPlaylistsRequest,
  json,
  libraryPage,
  libraryVideo,
  playlist,
  renderApp,
  signedInAs,
} from '../test/fixtures'

/// The Playlists an Account keeps, and the Playlist that is the Library narrowed to it. What is
/// asked here is what a reader depends on: that an empty state says how a Playlist is filled, that
/// a Playlist's page is the Library with its own order, and that the arrangement is offered only
/// where every entry is on screen in that order.
describe('Playlists', () => {
  afterEach(() => vi.restoreAllMocks())

  it('says how a Playlist is filled when there are none', async () => {
    signedInAs('User')

    renderApp('/playlists')

    expect(await screen.findByText('No Playlists yet')).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'New Playlist' })).toBeInTheDocument()
  })

  it('lists what is kept, and leads to the Playlist itself', async () => {
    signedInAs('User', (input) => (isPlaylistsRequest(input)
      ? json({ playlists: [playlist()] })
      : undefined))

    renderApp('/playlists')

    const row = await screen.findByRole('link', { name: 'Sunday evening' })
    expect(row).toHaveAttribute('href', '/playlists/01994dd4-2a0a-7000-8000-0000000000f1')
    expect(screen.getByText('2 Videos')).toBeInTheDocument()
  })

  it('opens a Playlist as the Library in the order it was arranged, with that arrangement offered', async () => {
    const asked: string[] = []
    signedInAs('User', (input) => {
      if (isPlaylistsRequest(input)) return json({ playlists: [playlist()] })
      if (isLibraryRequest(input)) {
        asked.push(String(input))
        return json(libraryPage([
          libraryVideo({ id: '01994dd4-2a0a-7000-8000-000000000051', displayTitle: 'First' }),
          libraryVideo({ id: '01994dd4-2a0a-7000-8000-000000000052', displayTitle: 'Second' }),
        ]))
      }
      return undefined
    })

    renderApp('/playlists/01994dd4-2a0a-7000-8000-0000000000f1')

    expect(await screen.findByRole('heading', { name: 'Sunday evening' })).toBeInTheDocument()
    await waitFor(() => expect(asked.length).toBeGreaterThan(0))
    expect(asked[asked.length - 1]).toContain('playlist=01994dd4-2a0a-7000-8000-0000000000f1')
    expect(asked[asked.length - 1]).toContain('sort=PlaylistOrder')

    // The first card cannot go up and the last cannot go down, so the ends of the arrangement
    // cannot be pushed off it.
    expect(screen.getByRole('button', { name: 'Move First up' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Move First down' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Move Second down' })).toBeDisabled()
  })

  it('withdraws the arrangement while a search hides part of the Playlist, and says why', async () => {
    signedInAs('User', (input) => {
      if (isPlaylistsRequest(input)) return json({ playlists: [playlist()] })
      if (isLibraryRequest(input)) {
        return json(libraryPage([
          libraryVideo({ id: '01994dd4-2a0a-7000-8000-000000000051', displayTitle: 'First' }),
        ]))
      }
      return undefined
    })

    renderApp('/playlists/01994dd4-2a0a-7000-8000-0000000000f1?query=first')

    expect(await screen.findByRole('heading', { name: 'Sunday evening' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Move First/ })).toBeNull()
    expect(
      screen.getByText(/Reordering needs the whole Playlist/),
    ).toBeInTheDocument()
  })

  it('says so when the Playlist is not one of this Account’s', async () => {
    signedInAs('User', (input) => (isPlaylistsRequest(input)
      ? json({ playlists: [playlist()] })
      : undefined))

    renderApp('/playlists/01994dd4-2a0a-7000-8000-0000000000ff')

    expect(await screen.findByText('No such Playlist')).toBeInTheDocument()
  })

  it('files a Video from its own page, and reads back where it is filed', async () => {
    const written: { url: string; method: string }[] = []
    signedInAs('User', (input, init) => {
      if (isPlaylistsRequest(input)) {
        if (init?.method && init.method !== 'GET') {
          written.push({ url: String(input), method: init.method })
          return json({ verdict: 'Updated', playlist: playlist({ contains: true }) })
        }
        return json({ playlists: [playlist({ contains: false })] })
      }
      return undefined
    })

    renderApp('/videos/01994dd4-2a0a-7000-8000-000000000010')

    const membership = await screen.findByRole('checkbox', { name: 'Sunday evening' })
    expect(membership).not.toBeChecked()
    fireEvent.click(membership)

    await waitFor(() => expect(written).toHaveLength(1))
    expect(written[0].method).toBe('PUT')
    expect(written[0].url).toContain('/videos/01994dd4-2a0a-7000-8000-000000000010')
  })
})
