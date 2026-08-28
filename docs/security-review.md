# Security review

A review of the shipped product surface before the first release. It records
what was examined, what was changed as a result, and the risks the release
knowingly carries. It is a review of this codebase rather than a general threat
model for self-hosting.

Reviewed at product version 0.1.0 on 2026-08-28.

## Scope

The trust boundaries this product actually has:

- The browser application and the HTTP API it calls.
- Anonymous media and preview delivery.
- The Installation Operator CLI, including Backup, Restore, and administrator
  recovery.
- Source media on read-only mounts, and the application's own data directory.
- The outbound connection to prdb.net.

## Authentication and session handling

- Session and CSRF tokens are 256 bits from a cryptographic RNG, stored only as
  SHA-256 hashes, compared in fixed time, and bound to both the session and the
  Account. Length is validated before any comparison.
- Passwords are hashed with ASP.NET Core's `PasswordHasher`. No endpoint returns
  a password, a hash, or a recovery code that was issued elsewhere.
- The session cookie is `HttpOnly`, `SameSite=Strict`, scoped to `/`, and
  expires with the session record.
- Every state-changing request carries the session's CSRF token in
  `X-CSRF-Token` and is refused without it. `SameSite=Strict` alone is not
  relied upon.
- Bootstrap Authorizations and Recovery Codes are single-use, short-lived, and
  delivered through an owner-only file rather than command output or logs.
  Issuing a recovery code ends the target Account's sessions.
- **Changed in this review:** the session cookie's `Secure` flag followed the
  scheme of the immediate hop, so terminating TLS at a reverse proxy silently
  dropped it, and anonymous rate limiting partitioned by the proxy's address
  rather than the client's. `VIEWER_BEHIND_REVERSE_PROXY=true` now makes the
  application honour `X-Forwarded-Proto` and `X-Forwarded-For`. It is off by
  default, because trusting those headers is only safe when nothing but the
  proxy can reach the container.

## Authorization

- The default authorization policy requires an authenticated Account, so a new
  endpoint is authenticated unless it opts out explicitly.
- Every administrative group additionally requires the Administrator role.
  Background Work, Work Issues, affected-item lists, Operator Handoffs,
  configuration, identification review, and account administration are all
  behind it, and the tests assert that an approved ordinary User receives 403.
- Personal State is always scoped by the authenticated Account at the boundary;
  no endpoint accepts an Account identifier from the caller.
- Approval-gated registration means a self-registered Account can read nothing
  until an Administrator approves it.

## Anonymous surface

- `/api/health` returns a fixed liveness value and nothing about the
  installation.
- Sign-in, registration, recovery, and bootstrap are rate limited to 20 requests
  per minute per client address.
- Video and preview delivery are anonymous by design, because a browser's
  `<video>` and `<img>` elements fetch them without the application's
  credentials. They are addressed by a random version-4 identifier that is
  neither the database key nor derived from a path, so URLs cannot be
  enumerated, and they only ever serve a file that inspection admitted and whose
  size and modification time still match.
- **Accepted risk:** anyone holding a delivery URL can stream that file without
  signing in, and delivery is not rate limited. This follows from direct browser
  playback; an installation exposed to the internet should rate limit at the
  reverse proxy.

## Path handling

- Every read of source media resolves through one helper that normalises the
  path, requires it to stay beneath the configured Library Directory, refuses
  reparse points, and returns nothing rather than throwing. Library Scans apply
  the same containment while traversing and refuse links that leave the root.
- Preview delivery re-checks that the resolved artefact stays beneath the
  application's own previews directory even though the stored path is
  application-generated.
- Library Directory staging only accepts paths beneath the documented mount
  root, and nothing beneath a Library Directory is ever written.

## Secrets and disclosure

- The prdb credential is never returned by any endpoint; the configuration
  surface reports only whether one exists and its connection status. Outbound
  logging redacts the `X-Api-Key` header.
- **Changed in this review:** a refused-credential Work Issue identified the key
  by its last four characters. It now carries a one-way fingerprint — the first
  four bytes of its SHA-256 — which still distinguishes a refused key from its
  replacement but discloses nothing about the key itself.
- Operator Handoffs, Work Issue details, and diagnostics carry the exact
  container path and a sanitised cause class, and never a credential, a stack
  trace, a remote response body, Personal State, or another Account's activity.
  The application never guesses a host path.
- **Accepted risk, per ADR 0011:** the prdb credential is stored recoverably in
  the application database rather than encrypted at rest, because the
  installation must reconnect unattended after a restart. Its protection is the
  data directory's permissions, which the image enforces with `UMASK=077` and a
  non-root process identity.

## Backup Archive

- Archives are wholly encrypted with AES-256-GCM under a 256-bit key derived by
  Argon2id (64 MiB, 3 passes, 2 lanes). The non-secret envelope — magic, format
  version, product version, creation time, and KDF parameters — is authenticated
  as associated data, so a modified header fails decryption rather than steering
  it.
- The KDF parameters travel with the archive, and this version refuses costs
  below its own floor, so an archive cannot be downgraded into a cheap one.
- The passphrase is never accepted as a command-line argument, never echoed,
  never logged, and never stored. Losing it is unrecoverable by design.
- A wrong passphrase, a truncated file, or a single altered byte fails
  authentication before any decrypted data is produced. Unknown members in the
  payload fail rather than being dropped.
- Archives are written owner-only through a staged file that is opened and
  revalidated before being published, and never overwrite an existing file.
- Restore only ever activates into an empty, unclaimed state, so an archive can
  never be used to overwrite or merge into a running installation, and it
  requires an active Administrator so activation cannot lock everyone out.

## Data handling

- All database access goes through EF Core with parameterised queries; the
  product contains no raw SQL outside its migrations.
- SQLite runs in WAL mode with pragmas applied on every connection, and the
  archive snapshot reads inside one transaction so it cannot capture a torn
  state.
- The data-protection provider is registered as ephemeral. Nothing in the
  product persists data-protected payloads — sessions, CSRF tokens, and recovery
  codes are all database-backed — so a restart invalidates no user-visible
  state.

## Denial of service

- Every background lane is bounded: one item or one small batch per slice, with
  a durable commit between slices, so no run holds an unbounded working set.
- Remote identification honours backoff and stops entirely against unchanged
  authority rather than retrying a rejected credential.
- Unwritable application storage stops durable writes as a Safety Stop instead
  of probing in a loop.
- Interactive playback throttles the lanes, so background work cannot starve
  playback.
- **Accepted risk:** `GET /api/library/videos` returns the whole catalogue in
  one response. It is a signed-in-only endpoint and the cost is proportional to
  the library rather than to the request, but it is the largest response the
  product produces. See [performance.md](performance.md).

## Dependencies

- Production dependencies are pinned to exact versions through central package
  management: ASP.NET Core and EF Core 10.0.10, `Prdb.Sdk` 0.11.0,
  `Prdb.Hashing` 0.1.0, and `Konscious.Security.Cryptography.Argon2` 1.3.1.
- The container ships `ffmpeg` and `ffprobe` from the base image's package feed.
  Both are invoked with an explicit argument list and never through a shell, so
  a file name can never become an argument or a command.
- The frontend has no runtime dependency that is not bundled at build time, and
  the served application makes no third-party network request.
