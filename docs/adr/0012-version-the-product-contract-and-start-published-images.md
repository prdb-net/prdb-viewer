# Version the product contract and start published images

`prdb-viewer` uses semantic versions for its user-facing product contract,
remaining in `0.x` until the first stable release. That contract includes
configuration meanings, HTTP API behaviour, Backup Archive compatibility,
container operation, and documented source-media safety; the internal SQLite
schema is migrated implementation and is not independently versioned for
Users.

Every push and pull request builds and tests the backend and frontend, checks
the committed OpenAPI document and generated TypeScript types for drift, builds
both container architectures from ADR 0007, and starts each image in an
architecture-appropriate environment. Tests use local fixtures and never
require a real prdb credential, a real library, or another external service.

Release images are published as `prdbnet/prdb-viewer` on Docker Hub. Every
published image has an immutable commit-SHA tag; releases additionally receive
their semantic-version tag, and `latest` identifies the newest stable release.
The application stamps its version and commit into the assembly, image labels,
startup diagnostics, and secret-free Operator Handoffs.

## Consequences

- The changelog records user-visible changes and supplies release notes;
  refactors without a contract effect do not require an entry.
- Database migrations are forward-only. Starting an older image against an
  application data directory already migrated by a newer version is unsupported
  and fails rather than attempting a downgrade.
- Release documentation tells the Installation Operator to preserve a snapshot
  or copy of application data before an upgrade when rollback is required. A
  Backup Archive protects precious portable state but is not promised to
  recreate an older schema or every regenerable artefact for rollback.
- Container smoke tests verify the runtime claims of ADR 0007, including the
  process identity, required data mount, read-only source mounts, media tools,
  signal handling, and startup migration ordering. Merely producing image
  layers is not sufficient evidence.
- Publishing is skipped safely when registry credentials are unavailable or in
  a fork; build, test, contract, and smoke verification still run.
