# Open SQLite in WAL and initialise every connection

The SQLite database is opened with `journal_mode=WAL`, `synchronous=FULL`,
`busy_timeout=5000`, and `foreign_keys=ON`. WAL is established while the
database is prepared; the other pragmas are applied whenever a physical
connection is opened because they are connection state and pooled connections
cannot be assumed to carry the intended values. Migrations complete before the
HTTP listener accepts requests or Background Work starts.

This adopts the measured WAL and connection-initialisation baseline of
`prdb-fab` for the same .NET, EF Core, and SQLite stack, but deliberately keeps
`FULL` synchronization. The Viewer stores Personal State, Administrative
Overrides, Account state, and other facts that cannot be reconstructed from
source media after a power loss, while ADR 0002 requires the database to live
on storage local to the container host. A production-shaped benchmark must
still exercise concurrent Library Scans, technical inspection, content
hashing, preview generation, browsing, and playback reporting before the first
release. Lowering durability or changing timeouts or checkpoint policy requires
evidence from that workload and a reopened decision rather than an
Administrator setting.

## Consequences

- EF Core contexts are short-lived per request or bounded Background Work run,
  and read-only paths do not track entities.
- SQLite serialises writers. The application adds no global write lock or
  single-writer service; instead, transactions remain short and never span a
  filesystem operation, media-tool execution, or network request as required
  by ADR 0002.
- The default automatic WAL checkpoint is retained until measurement shows a
  latency, recovery, or growth problem. Checkpoint controls are operational
  implementation, not product settings.
- A Backup Archive is produced from one logically consistent committed state;
  it never copies a live database file while ignoring its WAL and SHM files.
- Infrastructure tests verify the effective pragmas on independently opened
  and pooled connections and verify that migration failure prevents startup.
