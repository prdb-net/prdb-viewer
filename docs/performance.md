# Production-shaped SQLite workload

This is the performance evidence the release carries. It is a measurement, not a
promise about anyone's hardware: the numbers below say how the shipped queries
behave on a library far larger than a first installation.

## How to reproduce

The benchmark lives with the tests and is opt-in, because it is a measurement
rather than an assertion:

```bash
dotnet build
VIEWER_BENCHMARK=1 VIEWER_BENCHMARK_REPORT=/tmp/prdb-viewer-benchmark.txt \
  dotnet test --project tests/Prdb.Viewer.Infrastructure.Tests --no-build \
  --filter-method "*A_production_shaped_library_stays_responsive*"
```

It builds a library at two scales, each with several Accounts holding private
state, multi-file Videos, and established Identification Claims, then measures
the read paths a signed-in User and an Administrator actually wait for and the
write path every playback report takes. Each of those is the median and slowest
of twenty samples against a real SQLite database in WAL mode, opened exactly the
way the Host opens it. The perceptual neighbourhood search is measured
differently, because it is a backlog rather than a request: it is drained once,
end to end, and what is reported is what an installation pays for the whole
library.

## Result

Measured on 2026-09-13 with .NET 10.0.112 on Ubuntu 26.04, an Intel Core 5 210H
(12 threads) and 15 GiB of memory. The seeded library is ordinary H.264/AAC in
MP4 — a Client-Dependent configuration each Account's client has qualified — so
the library measurements include the per-Account, per-client admission question
rather than the one classification that can skip it.

### 2,000 Videos

2,200 Video Files · 25 Accounts · 6 MiB database

| Operation | Median | Slowest |
| --- | --- | --- |
| Library, first page | 18 ms | 260 ms |
| Library, deep page | 17 ms | 22 ms |
| Library, search | 3 ms | 22 ms |
| Library, title order | 16 ms | 21 ms |
| Library facets | 1 ms | 25 ms |
| Personal library shelves | 3 ms | 28 ms |
| Recommendations page | 94 ms | 226 ms |
| Recommendations, cold start | 53 ms | 64 ms |
| Background work status | 1 ms | 32 ms |
| Identification review queue | 1 ms | 25 ms |
| Outstanding hashing lane query | 1 ms | 5 ms |
| Playback report write | 5 ms | 72 ms |

### 20,000 Videos

22,000 Video Files · 25 Accounts · 56 MiB database

| Operation | Median | Slowest |
| --- | --- | --- |
| Library, first page | 57 ms | 62 ms |
| Library, deep page | 66 ms | 68 ms |
| Library, search | 15 ms | 16 ms |
| Library, title order | 53 ms | 54 ms |
| Library facets | 6 ms | 7 ms |
| Personal library shelves | 16 ms | 17 ms |
| Recommendations page | 364 ms | 385 ms |
| Recommendations, cold start | 275 ms | 294 ms |
| Background work status | 1 ms | 1 ms |
| Identification review queue | 1 ms | 3 ms |
| Outstanding hashing lane query | 8 ms | 12 ms |
| Playback report write | 6 ms | 13 ms |

### The perceptual neighbourhood search

This one is not a request anybody waits for, so it is not measured as one. It is
the whole backlog, drained once, over the same seeded libraries — every Video
File compared against every other that carries a Perceptual Hash.

| Library | Whole backlog | Slices | Neighbourhoods found |
| --- | --- | --- | --- |
| 2,200 Video Files | 2.0 s | 36 | 200 |
| 22,000 Video Files | 71 s | 345 | 2,000 |

The slowest samples at the smaller scale are first-call costs — query
compilation and connection setup — rather than a property of the data.

## What this says

- **Opening the library costs a page and one question per Video.** The
  projection in [ADR 0013](adr/0013-maintain-a-discovery-projection-for-each-video.md)
  still keeps the page itself off the library's back — search, title order and
  the facet lists are unchanged — but admission is now Client Video Playability
  (ADR 0015), which is per Account and per client and therefore cannot be a
  column. Deciding it for 20,000 Videos costs 57 ms against the 6 ms the
  installation-wide approximation cost, and the approximation was wrong: it
  offered Videos this browser cannot play and hid ones it can.
- **Two thirds of that is the exact match count.** The page itself stops after
  the rows it needs; counting the matches decides admission for every Video in
  the library. The count that says how many matches were kept out is arithmetic
  rather than a second pass, which is what keeps this at one full decision per
  request instead of two.
- **A page of recommendations is the most expensive thing a User asks for.** At
  20,000 Videos it costs 364 ms, against 57 ms for the library, and the reason is
  that it asks the library's own admission question several times rather than
  once: Long unseen narrows to the Account's candidates, and discovery reads a
  bounded window over the never-watched pool twice — once led by affinity and
  once independent of it. Each of those is a full Ordinary Discovery answer,
  count included, and the count is the part that decides admission for every
  Video in the library. A cold start costs 275 ms and does the same work with
  nothing to rank, which is what shows that the cost is the pool rather than the
  ranking: the ranking itself reads one Account's own history and is bounded by
  what one person has watched.
- **Finding which of a library's own files look alike is paid once.** Seventy
  seconds for 22,000 Video Files is the whole backlog, and the second pass over
  an unchanged library does nothing at all: a file is compared once for the hash
  value it carries, and again only if it is hashed to a different one. The cost
  is quadratic in the library — ten times the files cost thirty-five times the
  work — which is why it is a durable backlog in bounded slices rather than a
  sweep: an installation that is restarted, paused, or has its Library Directory
  reconfigured keeps every comparison it has already made, and a library that
  grows by one file pays for one file.
- SQLite is comfortably the right database for this product. Background Work
  queries, the identification queue, and every Personal State write stay in
  single-digit milliseconds at both scales, and the lanes never pay a
  library-sized cost to find their next item.
- Paging deeper costs a little more, because SQLite still walks the rows it
  skips. At 20,000 Videos the last page of the library costs 66 ms, which is not
  worth trading for a cursor the ordering rules would have to encode.
- Search costs less than browsing here, because a query that matches three
  Videos asks the admission question three times rather than twenty thousand.
  Every term is a substring test against one projected column rather than a join
  across claims, metadata and file names.

## What has not been measured

Whether a page of recommendations stays acceptable on a library much larger than
20,000 Videos. At that size it is a third of a second for a screen somebody opens
deliberately, which is slower than anything else here and still well short of
feeling broken. What has not been established is where that stops being true, and
the shape of the fix is known rather than measured: each window over the
discovery pool pays for an exact match count purely to decide where the seed's
rotation starts, and nothing displays that number. An approximate size would
place the window just as well for a great deal less.

An Account with many thousands of retained shelf entries. Since ADR 0019 a
Personal Shelf is the library narrowed to it — one more predicate over the
Account's Personal State, paged like every other answer — and at 400 retained
entries it costs less than browsing does. What has not been measured is an
Account whose shelves hold a meaningful fraction of the library, where the
predicate stops narrowing anything.

The perceptual neighbourhood search above compares one hash against every other
in a single pass, which is the honest shape of the work and the reason the cost
is quadratic. Whether an index over the hash — banding the 64 bits so that only
candidates sharing a band are compared — is worth its own durable structure has
not been measured, and is not worth deciding before a real installation finds
the pass too slow. What is measured is that the pass is paid once.

## History

Before library discovery existed, `GET /api/library/videos` returned every
Available Video in one response. That cost **917 ms** and several megabytes at
20,000 Videos, and the first release documented it as the size it supported
well. Building discovery removed it.
