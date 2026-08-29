# prdb-viewer

`prdb-viewer` turns mounted home video directories into a fast, shared,
self-hosted library enriched with metadata from prdb.net. Each approved User
sees the same Videos while playback history and personal organisation remain
private to their Account.

The project is in active development. The current executable provides local
Account access, guided Installation Configuration, durable Library Scans, and
technical inspection of discovered Video File Candidates, plus the first
authenticated catalogue, direct browser playback path, Account-private playback
and Personal State surfaces, a searchable and filterable library whose playability
is assessed per Account and browser, prdb identification with local preview
images, local site recognition and an Administrator review workflow, the
observable background-work operations surface, and operator backup and restore,
presented as a navigable application in which every screen — and every Video —
has its own address. See
[VISION.md](VISION.md) for the product contract.

## Prerequisites

- .NET 10 SDK
- Node.js 24 and npm
- FFmpeg (`ffprobe` and `ffmpeg`) when running the Host outside the container
- Docker for the supported container workflow

## Build and test

Install the frontend dependencies once, then run the repository checks:

```bash
npm ci --prefix src/Prdb.Viewer.Frontend
dotnet build
dotnet test --no-build
npm run typecheck --prefix src/Prdb.Viewer.Frontend
npm test --prefix src/Prdb.Viewer.Frontend
npm run lint --prefix src/Prdb.Viewer.Frontend
npm run build --prefix src/Prdb.Viewer.Frontend
```

The browser playback fixtures the tests classify are generated with `ffmpeg`
during the run and skipped where it is unavailable, so nothing copyrighted or
identifying is committed to the repository. The production-shaped SQLite
workload benchmark is opt-in; see [docs/performance.md](docs/performance.md).

Generate the committed OpenAPI document and TypeScript declarations after an
HTTP contract change:

```bash
npm run generate:api --prefix src/Prdb.Viewer.Frontend
```

## Run locally

Build the frontend, create an ignored local data directory, and start the Host:

```bash
npm run build --prefix src/Prdb.Viewer.Frontend
mkdir -p .data
VIEWER_DATA_DIRECTORY="$PWD/.data" dotnet run --project src/Prdb.Viewer.Host
```

The application listens on the address printed by ASP.NET Core. The public
liveness endpoint is `/api/health`.

Before opening a new installation in the browser, create its short-lived,
single-use Bootstrap Authorization:

```bash
VIEWER_DATA_DIRECTORY="$PWD/.data" \
  dotnet run --project src/Prdb.Viewer.Host -- bootstrap-authorize
cat .data/operator/bootstrap-authorization.txt
```

Run every operator command as the identity the application itself runs as. In
the container that means `docker compose exec --user "$PUID:$PGID"`: a command
run as root leaves credential files the application cannot clean up afterwards,
and it warns about each one it had to leave behind.

The command prints only the credential file location. The browser consumes and
deletes the credential when it creates the first Administrator. User
registration requests do not grant access until an Administrator approves
them. If an Administrator loses access, the operator can issue a short-lived,
single-use recovery code without exposing it in command output:

```bash
VIEWER_DATA_DIRECTORY="$PWD/.data" \
  dotnet run --project src/Prdb.Viewer.Host -- recover-administrator <username>
```

## Run the container

The image requires a persistent mount at `/data`. Library Directories are
mounted separately and read-only; the application never changes source media.

```bash
docker build --tag prdb-viewer:local .
mkdir -p .data
docker run --rm \
  --publish 8080:8080 \
  --mount "type=bind,src=$PWD/.data,dst=/data" \
  --mount "type=bind,src=/path/to/videos,dst=/libraries/main,readonly" \
  --env "PUID=$(id -u)" \
  --env "PGID=$(id -g)" \
  prdb-viewer:local
```

The image defaults to `UMASK=077` so newly created database, journal, and
derived files are private to the configured process identity. Keep that default
unless the application-data mount has an equivalent access-control policy.

Create the initial Bootstrap Authorization against the same persistent data
mount before starting the container, or run the equivalent command in an
already running container:

```bash
docker run --rm \
  --mount "type=bind,src=$PWD/.data,dst=/data" \
  --env "PUID=$(id -u)" \
  --env "PGID=$(id -g)" \
  prdb-viewer:local \
  dotnet Prdb.Viewer.Host.dll bootstrap-authorize
```

Run the production-shaped smoke test against a built image:

```bash
docker/smoke-test.sh prdb-viewer:local
```

The smoke test verifies startup migration, the non-root process identity, the
application-data owner, `ffprobe` and `ffmpeg`, read-only source media, and
graceful shutdown.

### Behind a reverse proxy

A proxy that terminates TLS hides the client's scheme and address. The
application does not trust `X-Forwarded-Proto` or `X-Forwarded-For` by default,
because doing so lets anyone who can reach the container claim any address. Set
`VIEWER_BEHIND_REVERSE_PROXY=true` once the container is reachable only through
the proxy:

```bash
docker run --rm \
  --env VIEWER_BEHIND_REVERSE_PROXY=true \
  ... \
  prdb-viewer:local
```

With it enabled, the session cookie keeps its `Secure` flag and anonymous rate
limiting partitions by the real client address instead of the proxy. Video and
preview delivery are anonymous by design, so rate limit them at the proxy if the
installation is reachable from the internet.

## Configure the installation

After creating the first Administrator, complete the guided Installation
Configuration in the browser:

1. Enter the installation's prdb API key. The application verifies it through
   the official `Prdb.Sdk` client before activating it. Temporary service
   failures remain visible and retryable; a rejected replacement never
   overwrites a previously verified credential.
2. Select a readable container path beneath `/libraries`, validate it, review
   the staged result, and explicitly activate the Library Directory. Add or
   change the corresponding host bind mount outside the application first.

The application never returns a stored prdb credential through its API. It must
recover that credential for unattended background work, so it is stored in the
SQLite database without application-level encryption. Treat the complete
`/data` mount as sensitive, restrict its host permissions, and include it only
in protected backups. Always mount source media read-only; validation and later
scans do not write beneath a Library Directory.

## Scan and inspect source media

Activating a Library Directory queues its first Library Scan. Traversal and
technical inspection run in separate bounded background-work lanes and persist
their checkpoints, progress, and Work Issues in SQLite. The Administrator can
follow those runs or coalesce another scan from the Background work screen.

Recognised regular files are hashed and inspected with `ffprobe`. Technical
facts are committed only if the file remains unchanged throughout inspection.
A stable content identity preserves a Video File across a rename, while a
changed file at an existing path is retained as Replaced. One trustworthy
complete absence records Unreachable; a second records Missing. An incomplete
or inaccessible traversal never infers absence for the directory.

## Find your way around

The Viewer is an application rather than a page. A persistent shell carries
search, your Account and a sidebar of destinations grouped by what they are for:
the library and your own shelves under **Library**, your own Account under
**Account**, and — for an Administrator — installation configuration,
identification review, background work and accounts under **Administration**.
Entries that lead to waiting work carry its count, so Operational Attention and
an open identification queue are visible without opening the screen behind them.
On a narrow viewport the same navigation opens as a drawer, which the Escape key
closes and which takes no keyboard focus while it is shut.

Every screen is its own address, and the address carries what you were looking
at: the library's search, facets, sort order and revealed depth, and the
identification case an Administrator has open. A link therefore reproduces the
same page for whoever opens it, and the window is named for the screen that is
open, so the history and a bookmark say which one it was. What an Account may reach is decided in the
navigation and in the routes as well as at the API, so an address typed by hand
meets the same answer as an entry that is not offered.

A Video has a page of its own, reached from anywhere it appears. Following that
link is a Direct Address: it does not apply the admission rule of Ordinary
Discovery, so a Video this browser cannot play directly is still shown when you
ask for it by name. An address whose Video was merged into another one answers
as the Video that survived the merge and says so.

## Browse and directly play Videos

Approved Users receive the shared catalogue of Available Videos. Direct playback
is decided at three levels, and each later one overrides an optimistic earlier
one within its narrower scope:

1. **The Direct-Play Classification** of a Video File, from its inspected
   configuration alone. A conforming WebM carrying VP8 with Vorbis or no audio at
   ordinary dimensions and frame rate is the Baseline Candidate — the narrowest
   expectation that holds across the supported browsers. Ordinary H.264/AAC in
   MP4, VP9, AV1, HEVC and the rest are Client-Dependent: a plausible path whose
   support depends on the exact configuration and the device. Known legacy
   codecs and containers with no browser path are Unsupported, and combinations
   the rules do not settle stay Undetermined.
2. **The Client Playback Assessment** your browser makes of that configuration.
   Inspection retains profile, level, bit depth, dimensions, frame rate, bitrate
   and audio layout, so the browser is asked about the exact codec string with
   Media Capabilities where those facts determine one, and with the coarser type
   test where they do not.
3. **The Observed Playback Outcome** of actually playing the file there, which
   outranks both because it is the only one that is not a prediction.

A Video is therefore Ready for Direct Play, Compatibility Uncertain, or Not
Directly Playable **for one Account on one browser**. Ready Videos get the
ordinary Play action; uncertain ones get a labelled Try Direct Play with the
reason; the rest keep their variant details and an explicit Try Anyway. See
[ADR 0015](docs/adr/0015-decide-playability-per-account-and-client.md).

One deliberate play action tries each Available occurrence at most once, in the
order the evidence dictates: what already played here, then what this browser
assessed positively — smooth and energy-efficient first — then the conservative
baseline, then what is merely untried. A decode failure is remembered about that
file for this browser and the next variant is tried, visibly and without a second
confirmation. A delivery or network failure stops the fallback instead, because
every other variant would fail the same way, and nothing about the media is
concluded from it.

What your browser answered and what it observed are Personal State: they are
scoped to your Account and that browser, never influence another Account, and are
never shown to an Administrator as activity. They expire on their own — a
re-inspected file asks a new question, and a browser update is a new context.

The browser plays original files directly and the Host supports HTTP byte
ranges for seeking. Catalogue access remains authenticated. Video delivery is
intentionally outside the authentication boundary defined by the product
contract and uses a random, non-enumerable public identifier rather than a
filesystem path or sequential database key. Treat a copied delivery URL as a
direct link to that source Video File.

## Discover the library

The library is one searchable, filterable list, loaded a page at a time.

Search covers Established titles, Sites and Actors, plus the local display label
and current file names of Unknown Videos. It ignores case, diacritics and
ordinary punctuation, and every term must match somewhere — though not
necessarily in the same fact, so `known alex` finds a Video whose title matches
one term and whose Actor matches the other. An exact title or label ranks above
other titles, then Sites and Actors, then file names. There is deliberately no
stemming, typo correction or semantic matching.

Filters cover Site, Actor, Work Identification, review status, Client Video
Playability, availability and the Account's own Personal Play State. Values inside one facet
combine with OR and the facets combine with AND, and Unknown Work Identification,
Unknown Site and Review Needed are explicit values rather than the absence of
one. Established Sites and Actors are offered with their counts.

The default order is Discovery Date descending, so later enrichment never makes
an old Video look newly added; Title A-Z is the one alternative.

Ordinary results contain a Video while it is Available and ready for direct
play. When the current rules keep matches out, the view says how many and offers
the control that reveals them rather than dropping them silently: a per-Account
preference widens results to Videos that are not ready for direct play, and an
explicit playability filter overrides that preference for one view.

An unsupported Video is shown, not summarised away. It carries its title, its
locally generated preview, its provenance, and its Personal State like any other
entry, and in place of a Play button it states the container and codecs that
cannot be played directly. The preference is a standing checkbox that can be
turned off again, and `Unsupported only` narrows one view without changing it.

Filtering and ordering happen in the database over a projection each Video
maintains, and admission is decided per Account and per client against that
projection, so the cost of a page stays a page and one indexed question per
Video. See [ADR 0013](docs/adr/0013-maintain-a-discovery-projection-for-each-video.md),
[ADR 0015](docs/adr/0015-decide-playability-per-account-and-client.md) and
[docs/performance.md](docs/performance.md).

## Track playback and Personal State

Starting playback creates a Playback Attempt, while viewing activity begins
only after the browser confirms that playback advanced normally. Periodic,
idempotent reports retain Playback Progress, Accumulated Watch Duration, Play
Count, Viewing Completion, and the current Personal Play State. Pausing,
buffering, seeking by itself, and missing reports do not invent activity.
Concurrent reports for the same Account and Video are combined without
double-counting overlapping watch duration.

Resume positions transfer only to the same Video File until Timeline
Equivalence is established. The browser resumes that file, reports subsequent
activity, and derives Continue Watching from a qualifying unfinished Viewing
Session. Users can dismiss a Continue Watching entry without deleting history,
and can independently maintain Favourites, the oldest-first Watch Later queue,
and an optional one-to-five Personal Rating.

Every Personal State endpoint derives its Account from the authenticated local
session and requires CSRF protection for changes. That token is derived from the
session itself rather than stored and reissued, so every tab of one session
holds the same working token; see
[ADR 0017](docs/adr/0017-derive-the-csrf-token-from-the-session-token.md). No
request accepts another Account identifier, and Administrator authority does not
grant access to a User's Personal State. Personal references survive temporary unavailability;
they remain dormant only while their Video is removed from the active Library.

## Identify Videos and generate previews

After technical inspection admits a Video File, four further bounded lanes run
on their own durable schedule and never write beneath a Library Directory:

1. **Hashing** computes the `osHash` and `pHash` of the inspected content with
   the official `Prdb.Hashing` package, so both values match what the prdb
   Public API stores. A file that yields neither hash stays in the library and
   remains identifiable by its name.
2. **Preview generation** writes one still frame per Video File with `ffmpeg`
   into the application's own data directory. Previews are regenerable
   artefacts rather than identity, and a failed one is a visible Work Issue that
   the next Library Scan retries.
3. **Identification** offers hashed files to the documented public prdb API in
   bounded batches through `Prdb.Sdk`. A missing credential, a refused key, or
   an outage leaves the lane visibly waiting with the condition it needs;
   browsing, playback, and everything already known locally continue unchanged.
4. **Site recognition** reads the path of every file prdb has answered about
   against the retained Site Directory, so a Video the remote ladder could not
   match can still show where it comes from. It reads no content and needs no
   service to decide; only its once-a-day refresh of the site list does.

Every result carries its provenance. A definitive match on the inspected
content is Conclusive evidence and may establish an Unknown claim by itself;
name-derived, ambiguous, or contradictory results are only ever a reviewable
Identification Candidate. Work Identification and Site Recognition stay
independent, and no candidate ever supplies a title, a site, or artwork to
ordinary browsing. Video Files whose established work identity is the same are
associated into one Video, keeping the earliest Discovery Date, both
identification histories, and each Account's private viewing state.

## Recognise sites locally

A Video File whose path names exactly one known site — through the site's name,
the same name written as one word, or the distinctive label of its web address —
gets an Established Site Recognition of its own, sourced as local inference and
labelled as such wherever a Site is shown. The match is whole words of the path,
so a folder called `midnightowl` never names the site `Night Owl`, and the
longest name a path gives wins, so `Harbour Nights` is not read as `Harbour`.

A path that names several known sites, or names one only through a word short
enough to be an ordinary word, proposes an Identification Candidate instead and
establishes nothing. A locally recognised Site never replaces one prdb
established or an Administrator set; it does give way, without review, to the
canonical Site of a work prdb later identifies, and the reading it replaces is
kept as history.

The vocabulary is the Site Directory: the list prdb publishes, fetched at most
once a day and retained locally, together with every Site this installation has
already established. Recognition therefore keeps working while prdb is
unreachable. An installation that has never been able to fetch the list
recognises nothing and says so once, as a Scoped Issue. See
[ADR 0014](docs/adr/0014-recognise-sites-from-a-retained-site-directory.md).

## Review identifications

Administrators resolve open identification work in a queue-first review:
conflicting conclusive evidence first, then suggestive candidates. Selecting an
item opens one focused case that compares current and proposed knowledge side by
side and explains why automation stopped.

Every action — accept, assign, replace, reject, revoke, and split — shows its
consequence before Shared Library Knowledge changes, and the confirmation is
bound to the case version that was displayed, so a decision taken against
knowledge another Administrator or a scan has since changed is refused rather
than applied. Replacing, revoking, splitting, and any decision that merges
another Video require a decision note. Accepting or assigning establishes an
Administrative Override that automation may report against but never replaces.
Rejecting a candidate suppresses the same material evidence until materially
stronger evidence appears.

Ordinary Users see the resulting provenance and a review indicator. Candidate
contents, evidence, administrative attribution, and the queue itself remain
Administrator-only, and no review action exposes another Account's Personal
State.

## Operate background work

Every bounded run — Library Scan, technical inspection, hashing, preview
generation, identification, and site recognition — is visible to an
Administrator with its
trigger, state, current phase, observed counts, and the condition it is waiting
for. A percentage appears only where a stable denominator exists; open-ended
traversal reports concrete counts and phases instead of a fabricated estimate.

Routine diagnosis never requires container logs. Every obstacle is a **Work
Issue** with a stable reference, one of the eight Work Issue Causes, a severity,
and exactly one current Remediation Owner:

- A **Scoped Issue** affects one item or independent scope. Equivalent issues
  aggregate by cause, work category, and shared scope, so thousands of
  independent files report one message with a count and a complete affected-item
  list rather than one alert each. They never establish Operational Attention by
  themselves.
- An **Operational Blocker** prevents a meaningful work area from advancing — an
  unreadable mount root or subtree, a refused prdb credential, missing required
  configuration.
- A **Safety Stop** prevents further writes because continuing could endanger
  durable state; unwritable application storage is the usual cause. It offers no
  blind retry.

Operational Blockers and Safety Stops establish **Operational Attention**, shown
as a persistent administrative banner and count. It cannot be acknowledged or
hidden away: it clears only when every establishing issue has Resolution
Evidence — a new trustworthy observation followed by the blocked work actually
continuing.

Each issue offers only the actions that can advance it: `Retry now` for
repeatable work, `Check again` after an external prerequisite may have changed,
`View affected items`, and `Copy operator handoff`. The Operator Handoff is
copyable, secret-free text naming the exact configured Library Directory, the
exact container path, the safe cause, the retries already attempted, the
requested operator action, and the evidence the application expects afterwards.
The application never guesses a host path and never asks anyone to read
container logs. Every action is bound to the Work Issue version that was
displayed, so a stale retry is refused rather than committed against detail that
a reconfiguration, another Administrator, or a restart has since changed.

An Administrator can pause all Background Work installation-wide. The pause is
durable, survives a restart, lets active units reach a safe boundary before
stopping, and leaves browsing and playback untouched. Resuming continues from
retained checkpoints instead of starting duplicate runs. One bounded run may
also be cancelled: everything already committed is kept, and because a cancelled
scan is not a complete observation of its unvisited scope, it can never advance
a Video File towards Missing.

Interactive playback takes priority over Background Work. While a Video is being
delivered, the lanes reduce their own pressure between slices; throughput drops
and no committed result is lost.

Ordinary Users never see Background Work, Work Issues, configuration, mounts, or
operational diagnostics — only the neutral preparation and availability states
their Videos carry.

## Release and upgrade

The product contract — configuration meanings, HTTP API behaviour, Backup
Archive compatibility, container operation, and source-media safety — carries
the semantic version, and the project stays in `0.x` until its first stable
release. The internal SQLite schema is migrated implementation and is not
versioned separately.

Released images are published as `prdbnet/prdb-viewer`. Every published image
carries an immutable commit-SHA tag; a GitHub release additionally publishes its
semantic version, and a stable release also moves `latest`. The version and
commit are stamped into the assembly, the image labels, the startup log line,
and every Operator Handoff, so any report can be traced to the exact build.

To cut a release, move the `Unreleased` entries in
[CHANGELOG.md](CHANGELOG.md) under the new version heading, set `VersionPrefix`
in `Directory.Build.props`, and publish a GitHub release tagged `vX.Y.Z`.
Publication needs the `DOCKERHUB_USERNAME` and `DOCKERHUB_TOKEN` repository
secrets; without them it warns and skips, while build, test, contract, and
container smoke verification still run.

Database migrations are forward-only. Starting an older image against
application data that a newer version already migrated is unsupported and fails
rather than attempting a downgrade. **Before upgrading, take a snapshot or copy
of the application data directory if you might need to roll back.** A Backup
Archive protects precious portable state, but it is not promised to recreate an
older schema or every regenerable artefact.

Two release documents record what the product was measured and reviewed against:

- [docs/performance.md](docs/performance.md) — the production-shaped SQLite
  workload, its numbers, and the library size the first release supports well.
- [docs/security-review.md](docs/security-review.md) — the reviewed surface,
  what changed as a result, and the risks the release knowingly carries.

## Back up and restore

Backup is an Installation Operator action performed through the container CLI.
It needs no web session, never reads or copies Source Video Files, and never
changes the installation:

```bash
VIEWER_DATA_DIRECTORY="$PWD/.data" \
  dotnet run --project src/Prdb.Viewer.Host -- backup /backups/installation.prdbviewer
```

The command asks for a passphrase and repeats the question to confirm it. The
passphrase is never accepted as a command-line argument, so it cannot appear in
a process listing; piping it in on standard input works for automation. It is
never printed, never logged, and never stored — losing it is deliberately
unrecoverable, and neither an Administrator nor the project can weaken the
archive.

One Backup Archive is one portable file. It is wholly encrypted with AES-256-GCM
under an Argon2id key, its versioned envelope is authenticated together with the
body, and it is written with owner-only permissions to a staged file that is
opened and revalidated before it is published — a failed backup never leaves
behind something that could be mistaken for a successful one.

The archive carries every durable fact that cannot be reconstructed without
loss: Accounts with their roles, approval states, and password hashes;
configuration, the prdb credential, and historical onboarding milestones;
Library Directory identity and history; the Video and Video File identity graph
with its associations and provenance; Identification Claims, Candidates,
decisions, and overrides; Discovery Dates; and all Personal State. Source media,
generated previews, cached artwork, active sessions, Bootstrap Authorizations,
Recovery Codes, and Background Work checkpoints are excluded, because they are
either externally authoritative or regenerable.

An archive can be checked at any time without a restore target:

```bash
VIEWER_DATA_DIRECTORY="$PWD/.data" \
  dotnet run --project src/Prdb.Viewer.Host -- validate-backup /backups/installation.prdbviewer
```

Validation authenticates the whole archive and checks its envelope, format and
product versions, required sections, internal references, Account ownership,
Administrator presence, and identity continuity. A wrong passphrase, a truncated
or altered file, an unsupported format, or a violated invariant fails without
emitting any decrypted data and without touching the archive.

Restore activates an archive into an **empty, unclaimed** application state and
never merges into or overwrites an existing installation:

```bash
VIEWER_DATA_DIRECTORY="$PWD/.recovered" \
  dotnet run --project src/Prdb.Viewer.Host -- restore /backups/installation.prdbviewer
```

To recover a damaged installation, stop it, move its application data aside as a
fallback, and point Restore at a fresh data directory. Restore revalidates the
archive rather than trusting an earlier result, and every failure happens before
activation, so both the archive and the empty target stay usable.

After activation, Accounts, roles, Shared Library Knowledge, provenance, and
Personal State are back, and every earlier session, Bootstrap Authorization, and
Recovery Code is invalid, so Users sign in again through their restored
Accounts. Video Files stay conservatively Unreachable until a Library Scan
observes them again — a missing mount never produces Missing or Removed by
itself. Previews are regenerated, the restored prdb credential is reverified
against current conditions rather than claiming its historical Verified result,
and a Background Work pause that was in force travels with the archive and stays
in force until an Administrator resumes it.

Every archive names its Backup Archive format and producing product version in
its authenticated header. This version writes and restores format 1; an unknown
newer format is refused before any mutation, and an older one names the exact
product version to use rather than reporting a generic incompatibility.

If an Administrator loses their password, use `recover-administrator` rather
than a restore: it issues a single-use code that changes credential authority
only, and leaves identity, role, Shared Library Knowledge, and Personal State
untouched.
