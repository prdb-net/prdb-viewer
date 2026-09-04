import { navigation } from '../shell/navigation'

/// Where a screen was reached from, carried in the address rather than in memory.
///
/// ADR 0004 puts linkable state in the URL, which is what makes a Video's own page shareable and a
/// narrowed library a thing that can be sent to somebody. It is also what made the Video page a
/// one-way door: everything about where the reader came from — the search, the facets, the sort,
/// how far they had paged, which review case was open — lives in the address they left, and `Back
/// to the library` threw all of it away and put them at the top of an unnarrowed library.
///
/// A `from` parameter carries that address along, so the way back is the way in.
const parameter = 'from'

/// A link to `to` that knows how to come back to `from`.
///
/// `from` is an address within this application, given whole — path, search and all — because the
/// search is most of what is worth returning to.
export function withReturnTo(to: string, from: string) {
  return `${to}?${parameter}=${encodeURIComponent(from)}`
}

/// Where to go back to, and what to call it, or nothing when the screen was reached directly.
///
/// Only an address inside this application is honoured. A value that names another origin — or
/// that could be read as one, which is what a second leading slash does — is dropped rather than
/// followed: the parameter is written by our own links, but it arrives from the address bar.
export function returnTo(parameters: URLSearchParams) {
  const value = parameters.get(parameter)
  if (!value) return undefined
  if (!value.startsWith('/') || value.startsWith('//') || value.startsWith('/\\')) return undefined
  if (value.includes('\\')) return undefined

  return { to: value, label: destinationLabel(value) }
}

/// What the place a reader came from is called, taken from the navigation wherever it has a name,
/// so the way back and the way in are named the same thing.
function destinationLabel(address: string) {
  const [path, search] = address.split('?')

  if (path === '/admin/identification') {
    return new URLSearchParams(search ?? '').has('candidate')
      ? 'the review case'
      : 'the review queue'
  }

  // The Library is the shell's index route, and "Back to Browse" is nobody's name for it.
  if (path === '/') return 'the library'

  // The two screens that are about one thing rather than about a list of them. Neither is in the
  // navigation, because neither is a destination anybody sets out for.
  if (path.startsWith('/videos/')) return 'the Video'
  if (path.startsWith('/actors/')) return 'the Actor'

  const entry = navigation
    .flatMap((group) => group.entries)
    .find((candidate) => candidate.to === path)

  return entry ? entry.label : 'where you came from'
}
