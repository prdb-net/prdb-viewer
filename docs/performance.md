# Production-shaped SQLite workload

This is the performance evidence the first release requires. It is a
measurement, not a promise about anyone's hardware: the numbers below say how
the shipped queries behave on a library far larger than a first installation,
and where they stop being fast.

## How to reproduce

The benchmark lives with the tests and is opt-in, because it takes about half a
minute and is a measurement rather than an assertion:

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
(12 threads) and 15 GiB of memory, against product version 0.1.0.

### 2,000 Videos

2,200 Video Files · 25 Accounts · 4 MiB database

| Operation | Median | Slowest |
| --- | --- | --- |
| Catalogue for one Account | 139 ms | 486 ms |
| Personal library shelves | 5 ms | 65 ms |
| Background work status | 1 ms | 35 ms |
| Identification review queue | 2 ms | 37 ms |
| Outstanding hashing lane query | 1 ms | 14 ms |
| Playback report write | 1 ms | 57 ms |

### 20,000 Videos

22,000 Video Files · 25 Accounts · 39 MiB database

| Operation | Median | Slowest |
| --- | --- | --- |
| Catalogue for one Account | 917 ms | 946 ms |
| Personal library shelves | 50 ms | 55 ms |
| Background work status | 1 ms | 2 ms |
| Identification review queue | 11 ms | 13 ms |
| Outstanding hashing lane query | 6 ms | 7 ms |
| Playback report write | 1 ms | 1 ms |

The slowest samples at the smaller scale are first-call costs — query
compilation and connection setup — rather than a property of the data.

## What this says

- SQLite is comfortably the right database for this product. Background Work
  queries, the identification queue, and every Personal State write stay in
  single-digit milliseconds at both scales, and the lanes never pay a
  library-sized cost to find their next item.
- Personal State scales with what one Account has actually touched rather than
  with the library, which is what the shelves are supposed to cost.
- **The catalogue is the one operation that scales with the whole library.**
  `GET /api/library/videos` returns every Available Video in one response, so at
  20,000 Videos it costs roughly a second on the server and a payload of several
  megabytes in the browser.

## The limit the first release carries

The MVP browses the library as one list. That is honest and fast up to a few
thousand Videos, and it is the size the first release is documented to support
well. Beyond roughly 5,000 Videos the catalogue response becomes the dominant
cost of opening the application, and no amount of indexing fixes it, because the
work is returning the whole library rather than finding it.

This is an unfinished part of the MVP rather than a deliberate boundary.
`VISION.md` lists search and the core filters among the MVP's own contents, and
the library discovery model — search, sorting, facets, and incremental loading —
is fully specified. It is the implementation that is missing. Building it is
what removes this limit; until then, the catalogue's cost is proportional to the
library and an installation much larger than a few thousand Videos will feel it
when the page is opened.

Nothing else in the measurement degrades with library size.
