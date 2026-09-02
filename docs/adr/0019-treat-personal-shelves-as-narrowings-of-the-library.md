# Treat Personal Shelves as narrowings of the Library

Continue Watching, Favourites and Watch Later are **Personal Shelves**: ways of
narrowing the Library rather than libraries of their own. The Library's
discovery request takes a shelf as one more facet, its facets are counted inside
the shelf, and a shelf keeps an order of its own beside the orders every view
has. A shelf's page is the Library's screen with that shelf pinned, and the
search in the shell searches the shelf that is open.

## Why

Until `0.12.0` the three shelves were answered by one endpoint that returned
everything on all of them, unfiltered and unpaged, and drawn by a screen of
their own that offered nothing to narrow them. The search field in the shell was
bound to the browsing screen, so typing on a shelf led away from it to the whole
Library. A Favourite could not be searched for among the Favourites, and a
Watch Later queue of a few hundred Videos would have been loaded whole.

Two ways of asking for a list of Videos is one too many. The discovery request
already read the Account's own Personal State to narrow by Personal Play State;
a shelf is the same kind of question, so it joins that request rather than
keeping a path of its own. Everything the Library has — search, facets, order,
paging and the address that reproduces them (ADR 0004) — then holds on a shelf
without being written a second time, and a shelf appears as a facet on the
browsing screen for nothing, so unplayed Favourites from one Site is a question
the Library can answer.

The search searches where it is because the alternative is a control. A scope
switch beside the field would ask everyone to decide, on every search, what they
already said by opening the shelf. The shelf's screen offers the whole Library
for the same words, one link away, which is the decision that is actually left.

## Consequences

- A shelf is a personal reference, which the glossary's Ordinary Discovery allows
  to expose exceptions: a request that names a shelf is answered without the
  admission rule. What the User put there is shown whether or not this client can
  play it, and while its Video is merely unavailable, as the old endpoint did;
  the card says what will not play. An explicit playability or availability
  filter still narrows a shelf. Nothing is hidden from a shelf, so a shelf has
  nothing to count as hidden.
- Continue Watching is decided twice: in memory, when a Personal State is
  summarised for a card, and in SQL, when the shelf narrows the Library. The
  discovery tests hold the two to the same answers, and a change to the rule is a
  change to both.
- The shelf order is one order with three readings — latest qualifying activity,
  latest addition, earliest addition for the queue — and is the default on a
  shelf's page. Where several shelves are chosen at once, the latest entry into
  any of them leads; chosen without a shelf, it is Newest.
- The address does not repeat what the route says. A shelf's page pins its
  shelf, so `/favourites?query=beach` is a search of the Favourites and
  `/?shelf=Favourites&query=beach` is the same question asked of the browsing
  screen; both reproduce what was looked at.
- The personal library endpoint that answered all three shelves at once is
  withdrawn from the product. The browser no longer asks it, and answering a
  question two ways is what this decision exists to stop.
