import { fireEvent, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import {
  credits,
  identification,
  isVideoRequest,
  json,
  libraryVideo,
  renderApp,
  signedInAs,
  videoDetail,
} from '../test/fixtures'

/// An Actor's own page.
///
/// The three states it has to draw are all ordinary: a full profile, a profile that has not
/// arrived, and — from a Video — a credit that resolves to nobody and therefore has no page. The
/// last two are what a fresh installation looks like for as long as the lane takes.
describe('ActorPage', () => {
  const actorId = '01994dd4-2a0a-7000-8000-0000000000a1'

  function actor(overrides: Record<string, unknown> = {}) {
    return {
      actorId,
      name: 'Alex Doe',
      profileState: 'Retained',
      profileFetchedAt: '2026-09-01T00:00:00Z',
      genderLabel: null,
      birthday: null,
      birthdayPrecisionLabel: null,
      deathday: null,
      birthplace: null,
      haircolourLabel: null,
      eyecolourLabel: null,
      breastTypeLabel: null,
      heightCentimetres: null,
      braSizeLabel: null,
      waistCentimetres: null,
      hipCentimetres: null,
      nationalityLabel: null,
      ethnicityLabel: null,
      careerStart: null,
      careerEnd: null,
      tattoos: null,
      piercings: null,
      aliases: [],
      links: [],
      bios: [],
      images: [],
      offeredImageCount: 0,
      videos: [],
      totalVideos: 0,
      creditedNames: ['Alex Doe'],
      favourite: false,
      ...overrides,
    }
  }

  function answering(body: unknown) {
    signedInAs('User', (input) =>
      typeof input === 'string' && input === `/api/library/actors/${actorId}`
        ? json(body)
        : undefined)
  }

  it('leads with the Videos this library holds them in, and states what prdb knows beside them', async () => {
    answering(actor({
      genderLabel: 'Female',
      birthday: '1994-03-17T00:00:00Z',
      birthdayPrecisionLabel: 'Exact',
      heightCentimetres: 170,
      birthplace: 'Example City',
      aliases: ['Alex D'],
      links: [{ url: 'https://example.invalid/alex', siteLabel: 'Twitter' }],
      bios: ['Alex Doe has been in front of a camera since 2014.'],
      images: [{ url: '/media/actors/01994dd4-2a0a-7000-8000-0000000000b1', kindLabel: 'Thumbnail' }],
      offeredImageCount: 1,
      videos: [libraryVideo({ displayTitle: 'A Known Work' })],
      totalVideos: 1,
    }))

    renderApp(`/actors/${actorId}`)

    // Somebody opens an Actor to decide what to watch, so the Videos are the body of the page.
    expect(await screen.findByText('A Known Work')).toBeTruthy()
    expect(screen.getByRole('heading', { name: 'Alex Doe' })).toBeTruthy()
    expect(screen.getByText('Female')).toBeTruthy()
    expect(screen.getByText('170 cm')).toBeTruthy()
    expect(screen.getByText(/Also credited as/)).toBeTruthy()
    expect(screen.getByText(/since 2014/)).toBeTruthy()

    // The one place on the page that leaves it, and it says so.
    const away = screen.getByRole('link', { name: /Twitter/ })
    expect(away.getAttribute('href')).toBe('https://example.invalid/alex')
    expect(away.getAttribute('rel')).toContain('noopener')
  })

  it('is still a page when prdb has said nothing about them', async () => {
    answering(actor({
      profileState: 'Pending',
      videos: [libraryVideo({ displayTitle: 'A Known Work' })],
      totalVideos: 1,
    }))

    renderApp(`/actors/${actorId}`)

    // An Actor with a name and no facts must not look like a page that failed to load: it says
    // which of the two states it is in, and shows the half that is always there.
    expect(await screen.findByText('A Known Work')).toBeTruthy()
    expect(screen.getByText(/has not arrived yet/)).toBeTruthy()
  })

  it('sends the reader to the Library, under the name the Videos use, when there are more', async () => {
    answering(actor({
      name: 'Alexandra Doe',
      creditedNames: ['Alex Doe'],
      videos: [libraryVideo({ displayTitle: 'A Known Work' })],
      totalVideos: 4,
    }))

    renderApp(`/actors/${actorId}`)

    // prdb leads with a name no Video here uses, and the Library's facet is keyed by the name the
    // Videos use. A link that carried the profile's name would narrow to nothing.
    const all = await screen.findByRole('link', { name: /All 4 in the Library/ })
    expect(all.getAttribute('href')).toBe('/?actors=Alex+Doe')
  })

  it('is reached from a Video, and leads back to it', async () => {
    const video = libraryVideo({
      displayTitle: 'A Known Work',
      identification: identification({
        work: { resolution: 'Established', targetTitle: 'A Known Work', reviewStatus: 'Clear', source: 'PrdbIdentification', dimension: 'WorkIdentification', targetUrl: null, evidenceClass: 'Conclusive', administrativeOverride: false, establishedAt: null, lastConfirmedAt: null },
        actors: credits([{ name: 'Alex Doe', actorId }, { name: 'Sam Roe', actorId: null }]),
      }),
    })
    signedInAs('User', (input) => {
      if (typeof input === 'string' && input === `/api/library/actors/${actorId}`) {
        return json(actor({ videos: [], totalVideos: 0 }))
      }
      if (isVideoRequest(input)) return json(videoDetail(video))
      return undefined
    })

    renderApp(`/videos/${video.id}`)

    const link = await screen.findByRole('link', { name: 'Alex Doe' })

    // A credit that resolves to nobody has nothing behind it to open, so it stays a name.
    expect(screen.queryByRole('link', { name: 'Sam Roe' })).toBeNull()
    expect(screen.getByText('Sam Roe')).toBeTruthy()

    fireEvent.click(link)

    // The way out is the way back in.
    await waitFor(() => expect(screen.getByRole('heading', { name: 'Alex Doe' })).toBeTruthy())
    expect(screen.getByRole('link', { name: 'Back to the Video' })).toBeTruthy()
  })

  it('keeps an Actor without keeping any of their Videos', async () => {
    const asked: { url: string; method?: string }[] = []
    let kept = false
    signedInAs('User', (input, init?: RequestInit) => {
      if (typeof input === 'string' && input === `/api/library/actors/${actorId}`) {
        return json(actor({ favourite: kept }))
      }
      if (typeof input === 'string' && input.includes('/favourite')) {
        asked.push({ url: input, method: init?.method })
        kept = true
        // What the endpoint actually answers. It used to answer nothing, and the client reads
        // every answer as JSON — so the request succeeded, the mutation failed, and the screen
        // went on saying the Actor was not kept. Only the browser suite saw it.
        return json({ favourite: true })
      }
      return undefined
    })

    renderApp(`/actors/${actorId}`)
    fireEvent.click(await screen.findByRole('button', { name: /Make a Favourite/ }))

    // A Favourite Actor is a reference to a person, not to a set of Videos. It is Personal State,
    // and it is the one thing kept about an Actor that a Backup Archive carries.
    await waitFor(() => expect(asked).toHaveLength(1))
    expect(asked[0].url).toBe(`/api/personal/actors/${actorId}/favourite`)
    expect(asked[0].method).toBe('PUT')

    // And the screen says so afterwards, which is the half a request assertion cannot see.
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Favourite' }).getAttribute('aria-pressed'))
        .toBe('true'))
  })

  it('says so when the link names nobody this library credits', async () => {
    signedInAs('User', (input) =>
      typeof input === 'string' && input.startsWith('/api/library/actors/')
        ? Promise.resolve(new Response('', { status: 404 }))
        : undefined)

    renderApp(`/actors/${actorId}`)

    expect(await screen.findByText('This Actor is not here')).toBeTruthy()
  })

  it('does not ask prdb for a picture', async () => {
    answering(actor({
      images: [{ url: '/media/actors/01994dd4-2a0a-7000-8000-0000000000b1', kindLabel: 'Thumbnail' }],
      offeredImageCount: 1,
    }))

    const { container } = renderApp(`/actors/${actorId}`)

    await screen.findByRole('heading', { name: 'Alex Doe' })

    // Every picture is served from this installation's own origin, by a random identifier.
    const sources = [...container.querySelectorAll('img')].map((image) => image.getAttribute('src'))
    expect(sources.length).toBeGreaterThan(0)
    expect(sources.every((source) => source?.startsWith('/media/actors/'))).toBe(true)
  })
})

vi.mock('../video/useClientQualification', () => ({ useClientQualification: () => {} }))
