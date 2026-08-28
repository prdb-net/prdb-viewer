# Maintain a discovery projection for each Video

Every Video carries a durable projection of the facts library discovery filters,
searches, and orders by: its display label, a normalised search text, its
readiness for direct play, whether its Work Identification is Established, its
Established Site, whether it needs identification review, and its current
availability. Established Actors are projected as their own rows so they can be
faceted and navigated.

The projection is derived state, never authority. Identification Claims, Video
Files, metadata, and Personal State remain the only sources of truth, and the
projection is recomputed from them whenever one of those facts changes:
technical inspection admitting or replacing a file, a scan reconciling absence,
identification establishing or retiring a claim, a review decision, a merge or a
split, and retained metadata arriving or being superseded. A Video whose
projection has never been computed is refreshed before it is served.

Discovery therefore filters, orders, and pages in SQL. The cost of a library page
is the page, not the library.

## Why

Ordinary Discovery admits a Video by facts that do not exist as columns. An
Unknown Video's display label is the file name of its oldest active occurrence.
Its title comes from a current claim joined with retained metadata that may no
longer describe it. Its readiness is the most playable of its Available
occurrences. Its Actors live inside a metadata JSON document. Search has to
ignore case, diacritics, and punctuation, which SQLite cannot do over those
values even where they are columns.

Computing any of that in the application means loading the candidate rows first,
so searching or faceting would read the whole library and then discard most of
it. That is precisely the cost the measured catalogue limit in
[performance.md](../performance.md) records, and moving it behind a search box
would make it worse rather than better: the user would wait for the whole
library on every keystroke.

## Consequences

- A write path that changes a projected fact and forgets to refresh the
  projection makes discovery disagree with the Video page. The refresh belongs
  with the write that causes it, and a test covers each path that has one.
- The projection is rebuildable from durable state by definition, so it is
  excluded from the Backup Archive and recomputed after a Restore like any other
  derived result.
- Upgrades that change how a projected value is derived have to refresh existing
  rows. A projection carries the time it was computed so an outdated one can be
  found and rebuilt without guessing.
- Readiness is projected from the installation-wide Direct-Play Classification
  because the account-and-client layers of the direct-play contract are not
  built. When they are, readiness stops being a Video-level column and becomes a
  per-Account, per-client assessment; the projection keeps the shared part and
  the query gains the client part rather than the column being reinterpreted.
- Personal facets stay out of the projection. They are per Account, they already
  have their own indexed table, and putting them in a Video-level row would make
  one Account's activity visible in another's query plan.
