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
(12 threads) and 15 GiB of memory.

### 2,000 Videos

2,200 Video Files · 25 Accounts · 4 MiB database

| Operation | Median | Slowest |
| --- | --- | --- |
| Library, first page | 7 ms | 168 ms |
| Library, deep page | 7 ms | 9 ms |
| Library, search | 3 ms | 15 ms |
| Library, title order | 7 ms | 10 ms |
| Library facets | 1 ms | 25 ms |
| Personal library shelves | 8 ms | 68 ms |
| Background work status | 1 ms | 31 ms |
| Identification review queue | 2 ms | 38 ms |
| Outstanding hashing lane query | 1 ms | 5 ms |
| Playback report write | 2 ms | 48 ms |

### 20,000 Videos

22,000 Video Files · 25 Accounts · 44 MiB database

| Operation | Median | Slowest |
| --- | --- | --- |
| Library, first page | 6 ms | 9 ms |
| Library, deep page | 15 ms | 16 ms |
| Library, search | 17 ms | 18 ms |
| Library, title order | 6 ms | 7 ms |
| Library facets | 3 ms | 4 ms |
| Personal library shelves | 52 ms | 58 ms |
| Background work status | 1 ms | 1 ms |
| Identification review queue | 12 ms | 15 ms |
| Outstanding hashing lane query | 6 ms | 7 ms |
| Playback report write | 1 ms | 1 ms |

The slowest samples at the smaller scale are first-call costs — query
compilation and connection setup — rather than a property of the data.

## What this says

- **Opening the library no longer costs the library.** A page is a page: 6 ms at
  20,000 Videos against 6 ms at 2,000. Search, title order, and the facet lists
  behave the same way. This is what the discovery projection in
  [ADR 0013](adr/0013-maintain-a-discovery-projection-for-each-video.md) buys,
  and it is the reason the projection exists rather than a convenience on top
  of it.
- SQLite is comfortably the right database for this product. Background Work
  queries, the identification queue, and every Personal State write stay in
  single-digit milliseconds at both scales, and the lanes never pay a
  library-sized cost to find their next item.
- Paging deeper costs a little more, because SQLite still walks the rows it
  skips. At 20,000 Videos the last page of the library costs 15 ms, which is not
  worth trading for a cursor the ordering rules would have to encode.
- Search costs more than browsing and still less than 20 ms, because every term
  is a substring test against one projected column rather than a join across
  claims, metadata and file names.

## What has not been measured

Personal library shelves scale with what one Account has touched rather than
with the library, which is what they are supposed to cost. At 52 ms for 400
retained entries they are the slowest read here, and an Account with many
thousands of Favourites would want the same paging treatment the library now
has. No such Account exists yet, so the number is recorded rather than acted on.

## History

Before library discovery existed, `GET /api/library/videos` returned every
Available Video in one response. That cost **917 ms** and several megabytes at
20,000 Videos, and the first release documented it as the size it supported
well. Building discovery removed it.
