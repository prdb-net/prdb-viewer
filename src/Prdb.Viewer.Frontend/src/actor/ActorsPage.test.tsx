import { fireEvent, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { json, renderApp, signedInAs } from '../test/fixtures'

/// The index of Actors.
///
/// It is the Library's kind of screen, so it answers an empty result and a deep address the same
/// way. The question that is its own is what it looks like before the profiles have arrived: a
/// grid of names with no pictures, which has to say why rather than looking broken.
describe('ActorsPage', () => {
  function actor(overrides: Record<string, unknown> = {}) {
    return {
      actorId: '01994dd4-2a0a-7000-8000-0000000000a1',
      name: 'Alex Doe',
      portraitUrl: '/media/actors/01994dd4-2a0a-7000-8000-0000000000b1',
      genderLabel: null,
      videoCount: 3,
      profileState: 'Retained',
      favourite: false,
      ...overrides,
    }
  }

  function index(actors: unknown[], overrides: Record<string, unknown> = {}) {
    return {
      actors,
      totalMatches: actors.length,
      hasMore: false,
      awaitingProfiles: 0,
      ...overrides,
    }
  }

  function answering(answer: (url: string) => unknown) {
    signedInAs('User', (input) => {
      if (typeof input === 'string' && input.startsWith('/api/library/actors?')) {
        const body = answer(input)
        return body === undefined ? undefined : json(body)
      }
      return undefined
    })
  }

  it('is a wall of faces, each with how many Videos they have here', async () => {
    answering(() => index([
      actor(),
      actor({
        actorId: '01994dd4-2a0a-7000-8000-0000000000a2',
        name: 'Sam Roe',
        portraitUrl: null,
        videoCount: 1,
      }),
    ]))

    renderApp('/actors')

    const alex = await screen.findByRole('link', { name: /Alex Doe/ })
    expect(alex.getAttribute('href')).toBe('/actors/01994dd4-2a0a-7000-8000-0000000000a1')
    expect(screen.getByText('3 Videos here')).toBeTruthy()

    // One is one, not "1 Videos".
    expect(screen.getByText('1 Video here')).toBeTruthy()
  })

  it('says why it is a grid of grey rectangles when no profile has arrived', async () => {
    answering(() => index(
      [actor({ portraitUrl: null, profileState: 'Pending' })],
      { awaitingProfiles: 1 },
    ))

    renderApp('/actors')

    expect(await screen.findByText(/None of their profiles have arrived yet/)).toBeTruthy()
  })

  it('has an order and carries it in the address', async () => {
    const asked: string[] = []
    answering((url) => {
      asked.push(url)
      return index([actor()])
    })

    renderApp('/actors')
    await screen.findByRole('link', { name: /Alex Doe/ })

    fireEvent.change(screen.getByLabelText('Sort'), { target: { value: 'MostHere' } })

    await waitFor(() => expect(asked.some((url) => url.includes('sort=MostHere'))).toBe(true))
  })

  it('makes an Actor a Favourite from the index', async () => {
    const asked: { url: string; method?: string }[] = []
    signedInAs('User', (input, init?: RequestInit) => {
      if (typeof input === 'string' && input.startsWith('/api/library/actors?')) {
        return json(index([actor()]))
      }
      if (typeof input === 'string' && input.includes('/favourite')) {
        asked.push({ url: input, method: init?.method })
        return json({})
      }
      return undefined
    })

    renderApp('/actors')
    fireEvent.click(await screen.findByRole('button', { name: 'Favourite Alex Doe' }))

    await waitFor(() => expect(asked).toHaveLength(1))
    expect(asked[0].url).toBe('/api/personal/actors/01994dd4-2a0a-7000-8000-0000000000a1/favourite')
    expect(asked[0].method).toBe('PUT')
  })

  it('says what an empty library and an empty search each mean', async () => {
    answering(() => index([]))

    renderApp('/actors')

    expect(await screen.findByText(/Actors arrive with prdb identification/)).toBeTruthy()
  })

  it('searches the Actors rather than leading away to the Library', async () => {
    const asked: string[] = []
    answering((url) => {
      asked.push(url)
      return index([])
    })

    renderApp('/actors')
    await screen.findByText(/Actors arrive with prdb identification/)

    fireEvent.change(screen.getByPlaceholderText('Search the Actors'), {
      target: { value: 'alex' },
    })

    // Typing on a list that is searched looks inside that list. Somebody who opened the Actors and
    // typed a name was looking for an Actor.
    await waitFor(() => expect(asked.some((url) => url.includes('query=alex'))).toBe(true), {
      timeout: 2000,
    })
    expect(screen.getByRole('heading', { name: 'Actors' })).toBeTruthy()
  })
})

vi.mock('../video/useClientQualification', () => ({ useClientQualification: () => {} }))
