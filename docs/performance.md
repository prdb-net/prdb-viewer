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
write path every playback report takes. Each measurement is the median and
slowest of twenty samples against a real SQLite database in WAL mode, opened
exactly the way the Host opens it.

## Result

Measured on 2026-08-28 with .NET 10.0.111 on Ubuntu 26.04, an Intel Core 5 210H
(12 threads) and 15 GiB of memory. The seeded library is ordinary H.264/AAC in
MP4 — a Client-Dependent configuration each Account's client has qualified — so
the library measurements include the per-Account, per-client admission question
rather than the one classification that can skip it.

### 2,000 Videos

2,200 Video Files · 25 Accounts · 5 MiB database

| Operation | Median | Slowest |
| --- | --- | --- |
| Library, first page | 15 ms | 213 ms |
| Library, deep page | 16 ms | 17 ms |
| Library, search | 4 ms | 18 ms |
| Library, title order | 17 ms | 20 ms |
| Library facets | 1 ms | 20 ms |
| Personal library shelves | 10 ms | 71 ms |
| Background work status | 1 ms | 33 ms |
| Identification review queue | 2 ms | 39 ms |
| Outstanding hashing lane query | 1 ms | 5 ms |
| Playback report write | 2 ms | 53 ms |

### 20,000 Videos

22,000 Video Files · 25 Accounts · 52 MiB database

| Operation | Median | Slowest |
| --- | --- | --- |
| Library, first page | 69 ms | 80 ms |
| Library, deep page | 81 ms | 86 ms |
| Library, search | 24 ms | 27 ms |
| Library, title order | 69 ms | 74 ms |
| Library facets | 3 ms | 4 ms |
| Personal library shelves | 54 ms | 64 ms |
| Background work status | 1 ms | 2 ms |
| Identification review queue | 12 ms | 14 ms |
| Outstanding hashing lane query | 7 ms | 7 ms |
| Playback report write | 1 ms | 2 ms |

The slowest samples at the smaller scale are first-call costs — query
compilation and connection setup — rather than a property of the data.

## What this says

- **Opening the library costs a page and one question per Video.** The
  projection in [ADR 0013](adr/0013-maintain-a-discovery-projection-for-each-video.md)
  still keeps the page itself off the library's back — search, title order and
  the facet lists are unchanged — but admission is now Client Video Playability
  (ADR 0015), which is per Account and per client and therefore cannot be a
  column. Deciding it for 20,000 Videos costs 69 ms against the 6 ms the
  installation-wide approximation cost, and the approximation was wrong: it
  offered Videos this browser cannot play and hid ones it can.
- **Two thirds of that is the exact match count.** The page itself stops after
  the rows it needs; counting the matches decides admission for every Video in
  the library. The count that says how many matches were kept out is arithmetic
  rather than a second pass, which is what keeps this at one full decision per
  request instead of two.
- SQLite is comfortably the right database for this product. Background Work
  queries, the identification queue, and every Personal State write stay in
  single-digit milliseconds at both scales, and the lanes never pay a
  library-sized cost to find their next item.
- Paging deeper costs a little more, because SQLite still walks the rows it
  skips. At 20,000 Videos the last page of the library costs 81 ms, which is not
  worth trading for a cursor the ordering rules would have to encode.
- Search costs less than browsing here, because a query that matches three
  Videos asks the admission question three times rather than twenty thousand.
  Every term is a substring test against one projected column rather than a join
  across claims, metadata and file names.

## What has not been measured

Personal library shelves scale with what one Account has touched rather than
with the library, which is what they are supposed to cost. At 54 ms for 400
retained entries they were the slowest read here, and an Account with many
thousands of Favourites would have wanted the same paging treatment the library
has. Since ADR 0019 a shelf is the library narrowed to it — one more predicate
over the Account's Personal State, paged like every other answer — so that cost
is now the library's cost. The figures above predate the change and have not
been taken again.

## History

Before library discovery existed, `GET /api/library/videos` returned every
Available Video in one response. That cost **917 ms** and several megabytes at
20,000 Videos, and the first release documented it as the size it supported
well. Building discovery removed it.
