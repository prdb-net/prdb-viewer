# Take Actor identity from prdb and hold Actor Profiles as projections

An **Actor** is prdb's identifier and nothing else. The application never mints
one, never merges two, and never infers one from a name: an Actor exists here
because an Established Work Identification named them, and it stops existing
here when no Video does.

What prdb says about that Actor — the description, the aliases, the links, the
bios, the pictures — is an **Actor Profile**: a retained projection, held so the
Actor's page reads when prdb cannot be reached, refreshed behind a horizon, and
regenerable in full from the identifier alone. It is excluded from the Backup
Archive, like the Site Directory and the retained facts of a proposed work, and
it carries no authority: an Actor Profile never establishes, corrects, or
disputes an Identification Claim.

An **Actor Credit** — one Video's naming of one person — keeps the name its own
metadata spells alongside the Actor it resolves to. A credit that resolves to
nobody is the ordinary state of a library identified before this decision, and
of one whose enrichment lane has not caught up. It faces the same way it always
did: the Library facets and counts by the name.

Every **Actor Image** is fetched over the credential-free artwork transport,
held under the application data directory, and served from this installation's
own origin by a random, non-enumerable identifier.

## Why

The alternative to prdb's identifier is a local one, keyed by a normalised name.
It is available immediately, it needs no lane, and it is wrong in the way that
matters: two people share a stage name and one person is credited under four,
so a local key either merges people who are not the same or splits a person who
is. Neither is recoverable afterwards, because the evidence that would undo it
is the identifier we declined to keep. `VideoDetailActorDto` has carried an `id`
in every identification answer this application has ever received; keeping it
costs a column.

Holding the profile rather than asking for it when the page opens follows the
rule the whole product is built on: a remote outage must not make what is
already known unbrowseable. A page that asks prdb on render is a page that is
blank exactly when the connection is Degraded — which is the state an
Administrator is most likely to be looking at screens in. It also puts the
installation credential behind an ordinary User's navigation, at whatever rate
that navigation happens, against an API with a published hourly and monthly
limit.

Holding it as a *projection* rather than as Shared Library Knowledge is what
keeps the Backup Archive honest. The archive carries what cannot be obtained
again: Accounts, configuration, identifications, corrections, Personal State. An
Actor's height and a hundred kilobytes of portrait can be obtained again from
one identifier, so carrying them would trade the archive's portability for
nothing. ADR 0014 made this trade for the Site Directory and it holds here for
the same reason.

The credit keeps its name because the facet reads names today and an
installation that never re-identifies anything must keep the Library it has.
Making the name a foreign key into a table of Actors would have made the facet
depend on a lane that may not have run, on a page that has nothing to do with
Actors.

## Consequences

- A Favourite Actor is Personal State and **is** in the Backup Archive, alone
  among the things this decision introduces. It references prdb's identifier,
  which outlives the profile the archive omits, so a restored favourite is
  intact before its profile is fetched again and does not need the profile to be
  valid.
- An Actor page has three states, and all three are ordinary: identity with
  profile and pictures, identity with a profile that has not arrived, and — from
  the facet — a name that resolves to no identity at all and therefore has no
  page. Every screen that names an Actor draws all three.
- A failed profile or picture fetch is not a Work Issue. Identification has
  succeeded, the library is browsable, and the loss is a paragraph or a
  placeholder. Calling an Administrator to a lane that is not blocked is the
  mistake `ProposedWorkArtworkRetention` already avoids.
- The Enrichment lane exists to make old identifications carry identities, and
  then keeps existing as a slow refresh. It asks `POST /videos/batch`, which
  costs no hashing and no matching, in batches of fifty.
- Deleting an installation's application data directory loses every profile and
  every picture and costs nothing but the requests to fetch them again. This is
  the property the decision is buying.
