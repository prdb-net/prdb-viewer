import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import {
  isRecommendationsRequest,
  json,
  libraryVideo,
  personalState,
  recommendationPage,
  recommended,
  renderApp,
  signedInAs,
} from '../test/fixtures'

/// The Recommendations page. What is asked here is what a reader depends on: that each section
/// says what it is and why a card is in it, that the four reactions are on every card, that Not
/// today is undoable and explains itself as a day rather than as a dislike, and that a page with
/// no history says so instead of inventing a preference.
describe('Recommendations', () => {
  afterEach(() => vi.restoreAllMocks())

  const loved = libraryVideo({
    id: '01994dd4-2a0a-7000-8000-0000000000c1',
    displayTitle: 'A Loved Video',
    personalState: personalState({ reaction: 'Love' }),
  })
  const unseen = libraryVideo({
    id: '01994dd4-2a0a-7000-8000-0000000000c2',
    displayTitle: 'A Forgotten Video',
  })
  const fresh = libraryVideo({
    id: '01994dd4-2a0a-7000-8000-0000000000c3',
    displayTitle: 'An Unwatched Video',
  })

  const full = () => recommendationPage({
    ForYouToWatchAgain: [recommended(loved, ['Loved', 'WatchedRepeatedly'])],
    LongUnseen: [recommended(unseen, ['NotWatchedForAWhile'])],
    NotYetDiscovered: [recommended(fresh, ['NeverWatched', 'SomethingDifferent'])],
  })

  it('shows the three sections, what each is for, and why each card is there', async () => {
    signedInAs('User', (input) => (isRecommendationsRequest(input) ? json(full()) : undefined))

    renderApp('/recommendations')

    expect(await screen.findByRole('heading', { name: 'For you to watch again' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Long unseen' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Not yet discovered' })).toBeInTheDocument()

    expect(screen.getByText('You loved this · You have come back to this')).toBeInTheDocument()
    expect(screen.getByText('You have not watched this for a while')).toBeInTheDocument()
    expect(screen.getByText('You have not watched this · Something different')).toBeInTheDocument()

    // No Video appears twice on one page.
    const titles = screen.getAllByRole('link', { name: /Video/ }).map((link) => link.textContent)
    expect(new Set(titles).size).toBe(titles.length)
  })

  it('offers all four reactions on every card, with the one that is set marked', async () => {
    signedInAs('User', (input) => (isRecommendationsRequest(input) ? json(full()) : undefined))

    renderApp('/recommendations')
    await screen.findByRole('heading', { name: 'For you to watch again' })

    const groups = screen.getAllByRole('group', { name: /Your reaction to/ })
    expect(groups).toHaveLength(3)
    for (const group of groups) {
      for (const label of ['Dislike', 'Shrug', 'Like', 'Love']) {
        expect(within(group).getByRole('radio', { name: label })).toBeInTheDocument()
      }
    }
    expect(screen.getByRole('radio', { name: 'Love', checked: true })).toBeInTheDocument()
    // Clearing is offered where something is set, and only there.
    expect(screen.getAllByRole('button', { name: /^Clear your reaction/ })).toHaveLength(1)
  })

  it('puts a Video aside for a day, says it is not a dislike, and takes it back', async () => {
    const written: { url: string; method: string }[] = []
    signedInAs('User', (input, init) => {
      if (typeof input === 'string' && input.includes('/not-today')) {
        written.push({ url: input, method: init?.method ?? 'GET' })
        return json({ dismissed: init?.method === 'POST' })
      }
      if (isRecommendationsRequest(input)) return json(full())
      return undefined
    })

    renderApp('/recommendations')
    await screen.findByRole('heading', { name: 'For you to watch again' })

    fireEvent.click(screen.getAllByRole('button', { name: /^Not today/ })[0])

    await waitFor(() => expect(written).toHaveLength(1))
    expect(written[0].method).toBe('POST')
    expect(await screen.findByText(/is put aside for 24 hours/)).toBeInTheDocument()
    expect(screen.getByText(/It is not a\s+dislike/)).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /^Undo putting A Loved Video aside/ }))
    await waitFor(() => expect(written).toHaveLength(2))
    expect(written[1].method).toBe('DELETE')
  })

  it('asks for a different page when other suggestions is pressed', async () => {
    const asked: string[] = []
    signedInAs('User', (input) => {
      if (isRecommendationsRequest(input)) {
        asked.push(String(input))
        return json(full())
      }
      return undefined
    })

    renderApp('/recommendations')
    await screen.findByRole('heading', { name: 'For you to watch again' })
    expect(asked[0]).not.toContain('seed=')

    fireEvent.click(screen.getByRole('button', { name: 'Other suggestions' }))

    await waitFor(() => expect(asked.length).toBeGreaterThan(1))
    expect(asked[asked.length - 1]).toContain('seed=')
  })

  it('says a section is empty rather than looking broken', async () => {
    signedInAs('User', (input) => (isRecommendationsRequest(input)
      ? json(recommendationPage({
          NotYetDiscovered: [recommended(fresh, ['NeverWatched'])],
        }))
      : undefined))

    renderApp('/recommendations')

    expect(await screen.findByText(/Watch something for a minute or two/)).toBeInTheDocument()
    expect(screen.getByText(/Nothing has been away long enough yet/)).toBeInTheDocument()
  })

  it('claims no preference where there is no history', async () => {
    signedInAs('User', (input) => (isRecommendationsRequest(input)
      ? json(recommendationPage(
          { NotYetDiscovered: [recommended(fresh, ['NeverWatched', 'SomethingDifferent'])] },
          { hasHistory: false },
        ))
      : undefined))

    renderApp('/recommendations')

    expect(await screen.findByText(/rather than anything about your taste/)).toBeInTheDocument()
  })

  it('says so when there is nothing at all to offer', async () => {
    signedInAs('User', (input) => (isRecommendationsRequest(input)
      ? json(recommendationPage({}, { hasHistory: false }))
      : undefined))

    renderApp('/recommendations')

    expect(await screen.findByText('Nothing to suggest yet')).toBeInTheDocument()
  })

  it('reports a page it could not fetch', async () => {
    signedInAs('User', (input) => (isRecommendationsRequest(input)
      ? Promise.resolve(new Response('{"detail":"No."}', { status: 500 }))
      : undefined))

    renderApp('/recommendations')

    expect(await screen.findByRole('alert')).toBeInTheDocument()
  })
})
