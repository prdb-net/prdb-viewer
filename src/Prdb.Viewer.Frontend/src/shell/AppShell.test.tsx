import { fireEvent, screen, within } from '@testing-library/react'

import {
  isFacetRequest,
  isLibraryRequest,
  isVideoRequest,
  json,
  libraryPage,
  libraryVideo,
  renderApp,
  videoDetail,
} from '../test/fixtures'

/// The chrome and the routes beneath it: who sees which destination, what a destination says
/// before it is opened, and whether an address reproduces what someone was looking at.
describe('The application shell', () => {
  afterEach(() => vi.restoreAllMocks())

  it('shows an Administrator every section and a User only their own', async () => {
    signedInAs('User')
    renderApp()

    const navigation = await screen.findByRole('navigation', { name: 'Main' })
    expect(within(navigation).getByRole('link', { name: 'Browse' })).toBeInTheDocument()
    expect(within(navigation).getByRole('link', { name: 'Favourites' })).toBeInTheDocument()
    expect(within(navigation).queryByRole('link', { name: 'Installation' })).not.toBeInTheDocument()
    expect(within(navigation).queryByRole('link', { name: 'Accounts' })).not.toBeInTheDocument()
  })

  it('refuses an administrative address to a User rather than rendering an empty screen', async () => {
    signedInAs('User')
    renderApp('/admin/accounts')

    // A URL typed by hand meets the same answer as the hidden entry: the library.
    expect(await screen.findByRole('heading', { name: 'Browse' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Accounts' })).not.toBeInTheDocument()
  })

  it('counts what is waiting on the navigation entry that leads to it', async () => {
    signedInAs('Administrator', (input) => {
      if (input === '/api/admin/background-work/') {
        return json({
          work: [],
          issues: [],
          operationalAttention: true,
          operationalAttentionCount: 2,
        })
      }
      if (input === '/api/admin/identification/queue') {
        return json([{ candidate: { id: 'a' } }, { candidate: { id: 'b' } }, { candidate: { id: 'c' } }])
      }
      return undefined
    })
    renderApp()

    const navigation = await screen.findByRole('navigation', { name: 'Main' })
    const work = await within(navigation).findByRole('link', { name: /Background work/ })
    expect(within(work).getByText('2')).toBeInTheDocument()
    const identification = within(navigation).getByRole('link', { name: /Identification/ })
    expect(within(identification).getByText('3')).toBeInTheDocument()
  })

  it('reproduces a narrowed library from its address alone', async () => {
    signedInAs('User', (input) => {
      if (isFacetRequest(input)) {
        return json({ sites: [{ value: 'Known Site', count: 3 }], actors: [] })
      }
      return undefined
    })

    // ADR 0004: the address carries the search, the facets and the order, so this link is what
    // another person receives and sees the same page for.
    renderApp('/?query=beach&sites=Known+Site&sort=TitleAscending')

    await screen.findByRole('heading', { name: 'Matching Videos' })
    expect(screen.getByLabelText('Search the library')).toHaveValue('beach')
    expect(screen.getByLabelText('Sort')).toHaveValue('TitleAscending')
    expect(screen.getByRole('button', { name: 'Known Site (3)' })).toHaveAttribute('aria-pressed', 'true')
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('query=beach'),
      expect.anything(),
    )
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('sites=Known+Site'),
      expect.anything(),
    )
  })

  it('puts a narrowing into the address rather than keeping it to itself', async () => {
    signedInAs('User')
    renderApp()

    await screen.findByRole('heading', { name: 'Browse' })
    fireEvent.click(screen.getByRole('button', { name: 'Unplayed' }))

    await vi.waitFor(() => expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('playState=Unplayed'),
      expect.anything(),
    ))
    // The heading follows the address: this is a narrowed library, not the whole of it.
    expect(await screen.findByRole('heading', { name: 'Matching Videos' })).toBeInTheDocument()
  })

  it('follows a link to a Video that was merged into another one', async () => {
    const survivor = libraryVideo({
      id: '01994dd4-2a0a-7000-8000-000000000090',
      displayTitle: 'The Surviving Video',
    })
    signedInAs('User', (input) => {
      if (isVideoRequest(input)) {
        return json(videoDetail(survivor, '01994dd4-2a0a-7000-8000-000000000091'))
      }
      return undefined
    })

    renderApp('/videos/01994dd4-2a0a-7000-8000-000000000091')

    expect(await screen.findByRole('heading', { name: 'The Surviving Video' })).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('has since been merged into this one')
  })

  it('says so plainly when a link leads to a Video that is not there', async () => {
    signedInAs('User', (input) => {
      if (isVideoRequest(input)) {
        return Promise.resolve(new Response(null, { status: 404 }))
      }
      return undefined
    })

    renderApp('/videos/01994dd4-2a0a-7000-8000-0000000000ff')

    expect(await screen.findByRole('heading', { name: 'This Video is not here' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Back to the library' })).toBeInTheDocument()
  })
})

/// The answers every screen needs before it can render, with room for the one a test is about.
function signedInAs(
  authority: 'Administrator' | 'User',
  answer: (input: unknown) => Promise<Response> | undefined = () => undefined,
) {
  vi.spyOn(globalThis, 'fetch').mockImplementation((input) => {
    const specific = answer(input)
    if (specific) return specific

    if (input === '/api/access/state') return json({ claimed: true, signedIn: true })
    if (input === '/api/access/me') {
      return json({
        id: '01994dd4-2a0a-7000-8000-000000000001',
        username: 'viewer',
        email: null,
        authority,
        csrfToken: 'csrf-token',
      })
    }
    if (input === '/api/personal/playback-profiles') return json([])
    if (input === '/api/admin/background-work/') return json({ work: [], issues: [] })
    if (input === '/api/admin/identification/queue') return json([])
    if (isFacetRequest(input)) return json({ sites: [], actors: [] })
    if (isVideoRequest(input)) return json(videoDetail(libraryVideo()))
    if (isLibraryRequest(input)) return json(libraryPage([libraryVideo()]))
    if (input === '/api/personal/library') {
      return json({ continueWatching: [], favourites: [], watchLater: [] })
    }
    return json([])
  })
}
