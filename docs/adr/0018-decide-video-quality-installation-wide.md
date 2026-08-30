# Decide Video Quality installation-wide

The Library filters and orders by **Video Quality**: the highest **Video File
Quality** among a Video's Available occurrences, projected as one ordinal column
per ADR 0013. A band is derived from inspected dimensions alone, so it is the
same fact for every Account and every browser.

This is a deliberate exception to ADR 0015, which decides admission to Ordinary
Discovery per Account and per client. Quality is not admission: it says what the
library holds, not what this browser may do with it.

## Why

The screens name the band of the occurrence a play action would reach for, which
*is* client-dependent — that is what the card promises and what pressing Play
delivers. Making the filter mean the same thing was the alternative, and it does
not survive contact with sorting.

A filter can be a per-row predicate, as playability's is. An order cannot: which
occurrence a play action reaches for is decided by the selection rule, over
evidence this client produced, and there is no column to sort a library by until
that rule has run for every row. "Best quality first" would therefore either
rebuild the selection rule in SQL for the whole library on every page, or quietly
fall back to a column — which is this decision, arrived at by accident and
without saying so.

So the two are separated instead. The band travels on the wire with each
occurrence, derived once in the Core, and the screens name what they were given
rather than deriving it a second time. Deriving it again in the browser is
exactly how a filter and a card come to disagree about the same file.

## Consequences

- Filtering and ordering by quality cost one indexed comparison over a projected
  column. Adding them changed no per-row decision, so the measurements in
  [performance.md](../performance.md) still hold.
- A Video with occurrences in two bands can be admitted by the better band while
  its card names the worse one, when this browser has ruled the better occurrence
  out. The card is right about what would play; the filter is right about what
  the library holds. It takes more than one occurrence of one Video at different
  qualities to happen at all, and the Video's own page lists every occurrence
  with its band, so the disagreement is visible rather than silent.
- The band is stored as its ordinal rather than as a name, unlike the
  enumerations beside it in the projection, because discovery orders by it and
  ordering by the names would put 1080p above 4K.
- Unknown is a real band — inspection established no dimensions — and it is the
  lowest, so those Videos sort last rather than first. It is not offered as a
  facet value: narrowing a library to what nothing is known about is not a
  question anyone is asking.
- The migration that adds the column clears every projection, because the default
  it leaves behind is indistinguishable from a real Unknown. The rebuild is the
  ordinary bounded one at startup.
