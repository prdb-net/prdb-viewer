# Structure the application as a navigable shell

`prdb-viewer` presents itself as an application rather than a page: a persistent
shell owns identity, search and navigation, and every screen is a route beneath
it. Navigation is a sidebar of titled groups whose entries are declared in one
list, and each entry may carry a count of what is waiting behind it. A new
destination is a line in that list and a route, not another section appended to
a screen that already carries several.

Until `0.3.0` one signed-in screen stacked the library and all four
administrative sections on top of each other, and the router matched a single
catch-all path. That does not survive its own roadmap: the overlapping ways to
organise a library the Vision promises — playlists, voting, saved filters,
recommendations — would each be one more section on the same scroll, and the
sections already there could only be reached by scrolling past the ones above
them. The choice of a sidebar over a
horizontal bar follows from the same reasoning: a group of destinations grows
downwards, where there is room, rather than competing for a finite width.

Playback belongs to one screen. A Video has its own address, and that page owns
the play action, the order the variants are tried in, and what each kind of
failure means; every other surface links to it. The Direct Address that reaches
it is not Ordinary Discovery and does not apply its admission rule.

## Consequences

- ADR 0004 is finally met rather than merely declared. Search, facets, sort
  order, revealed depth and the open administrative case live in the query
  string, so an address reproduces what someone was looking at and can be sent
  to someone else.
- Authority decides visibility in the navigation and in the routes, not only at
  the API. An address typed by hand meets the same answer as a hidden entry.
- A Work Issue that names what to correct navigates there instead of scrolling
  to a section that happened to be on the same page.
- One Video is answered on its own by the API. A merged identity answers as the
  Video that survived it, so a link taken before a merge keeps leading
  somewhere true, and the page says that it did.
- A standing preference is Personal State and lives with the Account; a filter
  narrows one view and lives in that view's address. The control that reveals
  what the rules keep out still sets the preference from where it is noticed.
- The shell asks for what its badges count on its own slower interval. An open
  screen observing the same server state refreshes it faster, and Query gives
  both the faster of the two while that screen is there.
