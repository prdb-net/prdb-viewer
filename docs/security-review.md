# Security review

A review of the shipped product surface before the first release. It records
what was examined, what was changed as a result, and the risks the release
knowingly carries. It is a review of this codebase rather than a general threat
model for self-hosting.

First reviewed at product version 0.1.0 on 2026-08-28, and revisited at
0.16.0 on 2026-09-04. Sections carry what is true of the current product;
[Since the first review](#since-the-first-review) records what was added
after 0.1.0 and what it changed here.

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
- Leaving that policy and adding CSRF are both per-endpoint decisions, and a
  forgotten one is silent: the route works and only its protection is missing.
  So both are asserted over the whole route table rather than one route at a
  time — every state-changing endpoint is either CSRF-protected or anonymous,
  and the set of anonymous endpoints is exactly the one this document lists.
  Widening either is a failing test until it is a written decision.
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
- Media delivery is anonymous by design, because a browser's `<video>` and
  `<img>` elements fetch it without the application's credentials. There are
  five such routes: the Video File itself, this installation's generated
  preview, and the three kinds of picture prdb offers that are held here — a
  proposed work's, an Established Work's, and an Actor's. Each is addressed by a
  random version-4 identifier that is neither the database key nor derived from
  a path, so URLs cannot be enumerated, and an identifier nothing is held for
  answers 404 rather than saying that something exists.
- A Video File is only ever served if inspection admitted it and its size and
  modification time still match. A retained picture is only ever served from
  beneath the directory that kind of picture belongs to, re-checked at delivery
  in the one place all four kinds pass through.
- `/media/proposals` is the one anonymous route whose subject is an
  Administrator-only surface. What it serves is a picture of a catalogue entry
  rather than anything about this installation, and serving it here is what
  keeps an Administrator's browser from being sent to prdb while reviewing a
  case. The review case itself — what is proposed, for which Video, and on what
  evidence — stays behind the Administrator role.
- **Accepted risk:** anyone holding a delivery URL can stream that file without
  signing in, and delivery is not rate limited. This follows from direct browser
  playback; an installation exposed to the internet should rate limit at the
  reverse proxy.

## Path handling

- Every read of source media resolves through one helper that normalises the
  path, requires it to stay beneath the configured Library Directory, refuses
  reparse points, and returns nothing rather than throwing. Library Scans apply
  the same containment while traversing and refuse links that leave the root.
- Delivery of a generated preview or a retained picture re-checks that the
  resolved file stays beneath the directory its kind belongs to, even though the
  stored path is application-generated. All four kinds pass through one place
  that does it, so a fifth inherits the check rather than having to repeat it.
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
- Library answers are paged, and the page size a request may ask for is clamped
  rather than trusted, so no signed-in caller can ask for a response
  proportional to the library. This replaces the accepted risk the first review
  carried, when that endpoint returned the whole catalogue at once. See
  [performance.md](performance.md).

## Dependencies

- Production dependencies are pinned to exact versions through central package
  management: ASP.NET Core and EF Core 10.0.10, `Prdb.Sdk` 0.13.0,
  `Prdb.Hashing` 0.1.0, and `Konscious.Security.Cryptography.Argon2` 1.3.1.
- The container ships `ffmpeg` and `ffprobe` from the base image's package feed.
  Both are invoked with an explicit argument list and never through a shell, so
  a file name can never become an argument or a command.
- The frontend has no runtime dependency that is not bundled at build time, and
  the served application makes no third-party network request.

## Since the first review

- Client playability assessment, added after 0.1.0, stores two further kinds of
  Personal State: what a browser answered about the library's media
  configurations, and what it observed when it played a Video File. Both are
  keyed by the Account and by an opaque client context the client names for
  itself, are readable and writable only through the Account's own authenticated
  session, are never returned to another Account, and are never exposed to an
  Administrator. The context key is stored as it arrives, reduced to a fixed
  harmless shape, and nothing is derived from request headers or retained about
  the device beyond that key.
- Local site recognition, added after 0.1.0, makes one further outbound request:
  `GET /sites` at most once a day, over the same transport with the same
  redacted `X-Api-Key` header and the same cross-origin redirect protection. It
  sends the installation credential and nothing about the library — no path, no
  file name, and no hash — and it reads the answer into a regenerable local
  copy that no Backup Archive carries. A refusal or an outage leaves the
  existing copy in place and is reported as a Scoped Issue rather than retried
  per file.
- Actors, added in 0.16.0, hold what prdb says about a person as a regenerable
  projection. The identity is prdb's and is never minted here, the profile is
  absent from the Backup Archive, and it never establishes, corrects, or
  disputes an Identification Claim. Reading an Actor is ordinary signed-in
  access; keeping one is Personal State scoped to the Account like every other,
  and no Administrator surface exposes whose it is.
- Retained pictures, added in 0.16.0 for proposed works, Established Works,
  and Actors, are the one place bytes from prdb are served back under this
  installation's own origin. What may be retained is an
  allow-list of raster image types rather than a deny-list, because the risk is
  the format that carries markup: SVG is refused, so nothing served from our
  own origin can be a document in it. A picture is capped at 8 MiB and refused
  rather than truncated, and its stored content type is the one the transport
  actually established rather than one the URL suggested.
- That artwork transport follows redirects, which the credentialed transport
  does not. It carries no installation credential, so a redirect can leak
  nothing; what it does allow is an address prdb names being fetched from
  inside the network the container runs in. The answer is only ever kept if it
  is one of the permitted image types, so what a redirect can achieve is a
  request rather than content. **Accepted risk:** an installation that treats
  its outbound network as a trust boundary should egress-filter the container.
- Backup Archive format 2, written since 0.16.0, adds each Account's Favourite
  Actors to the payload and changes nothing about how the payload is protected.
  Its envelope, cipher, and KDF floor are unchanged; a format 1 archive still
  restores directly, and an archive of either format fails authentication
  before any decrypted data is produced. Actor Profiles are not in it, because
  a projection is regenerated rather than restored.
