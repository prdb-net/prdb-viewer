import { describe, expect, it } from 'vitest'

import { returnTo, withReturnTo } from './returnTo'

describe('returnTo', () => {
  it('carries the whole address a reader left, search and all', () => {
    const link = withReturnTo('/videos/abc', '/?sites=Example%20Pictures&sort=Newest')

    expect(returnTo(new URLSearchParams(link.split('?')[1])))
      .toEqual({ to: '/?sites=Example%20Pictures&sort=Newest', label: 'the library' })
  })

  it('names the place it goes back to, so the way back reads like the way in', () => {
    expect(returnTo(new URLSearchParams({ from: '/favourites' }))?.label).toBe('Favourites')
    expect(returnTo(new URLSearchParams({ from: '/admin/identification' }))?.label)
      .toBe('the review queue')
    expect(returnTo(new URLSearchParams({ from: '/admin/identification?candidate=abc' }))?.label)
      .toBe('the review case')
  })

  // The parameter is written by our own links and arrives from the address bar, so an address that
  // names another origin — or that a browser would read as one — is dropped rather than followed.
  it.each([
    '//evil.invalid/steal',
    'https://evil.invalid/steal',
    '/\\evil.invalid',
    '/videos\\..\\elsewhere',
    'videos/abc',
  ])('refuses %s', (from) => {
    expect(returnTo(new URLSearchParams({ from }))).toBeUndefined()
  })

  it('says nothing when the screen was reached directly', () => {
    expect(returnTo(new URLSearchParams())).toBeUndefined()
  })
})
