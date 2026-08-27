# Use SQLite as the only database

`prdb-viewer` uses embedded SQLite through EF Core and does not offer a
PostgreSQL provider. The Viewer is one application instance for a person or a
small trusted group, and its deployment promise benefits more from one durable
data mount without a database service, network credential, second lifecycle,
or second backup concern than it would from PostgreSQL's higher write
concurrency and multi-instance capabilities.

This follows the operating model proven by comparable self-hosted media
servers and by `prdb-fab`, while keeping the choice explicit rather than
accidental. Supporting both engines is rejected because provider-specific
migrations, query behaviour, tests, and operational documentation would create
two persistence products and constrain both to their common subset.

## Consequences

- SQLite's single-writer model is an architectural constraint. Transactions
  remain short, Background Work writes in bounded batches, and no transaction
  spans filesystem inspection, media-tool execution, or a network request.
- The database lives in the application data directory on storage local to the
  container host with reliable locking semantics. Library Directories may
  remain host-mounted network shares.
- EF Core migrations run before requests are accepted or Background Work
  starts; a migration failure stops startup.
- Journal mode, synchronization level, timeouts, connection initialization,
  checkpointing, and backup mechanics require a measured follow-up decision
  against concurrent scanning, playback reporting, and browsing.
- Reconsider PostgreSQL only if the product adds multiple application
  instances, remote database hosting, substantially more concurrent Users, or
  a measured write workload that cannot meet its latency and durability goals
  with correctly configured SQLite.
