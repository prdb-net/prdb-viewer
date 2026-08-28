# prdb-viewer

`prdb-viewer` turns mounted home video directories into a fast, shared,
self-hosted library enriched with metadata from prdb.net. Each approved User
sees the same Videos while playback history and personal organisation remain
private to their Account.

The project is in active development. The current executable provides local
Account access, guided Installation Configuration, durable Library Scans, and
technical inspection of discovered Video File Candidates, plus the first
authenticated catalogue, direct browser playback path, Account-private playback
and Personal State surfaces, and prdb identification with local preview images
and an Administrator review workflow. See
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
follow those runs or coalesce another scan from the Background work panel.

Recognised regular files are hashed and inspected with `ffprobe`. Technical
facts are committed only if the file remains unchanged throughout inspection.
A stable content identity preserves a Video File across a rename, while a
changed file at an existing path is retained as Replaced. One trustworthy
complete absence records Unreachable; a second records Missing. An incomplete
or inaccessible traversal never infers absence for the directory.

## Browse and directly play Videos

Approved Users receive the shared catalogue of Available Videos. Inspected
H.264/AAC MP4 files are Baseline Candidates; formats whose browser support
varies remain Client-Dependent, while known legacy codecs are Unsupported and
unknown combinations remain Undetermined. These installation-wide classes are
technical guidance rather than a guarantee for a particular browser.

The browser plays original files directly and the Host supports HTTP byte
ranges for seeking. Catalogue access remains authenticated. Video delivery is
intentionally outside the authentication boundary defined by the product
contract and uses a random, non-enumerable public identifier rather than a
filesystem path or sequential database key. Treat a copied delivery URL as a
direct link to that source Video File.

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
session and requires CSRF protection for changes. No request accepts another
Account identifier, and Administrator authority does not grant access to a
User's Personal State. Personal references survive temporary unavailability;
they remain dormant only while their Video is removed from the active Library.

## Identify Videos and generate previews

After technical inspection admits a Video File, three further bounded lanes run
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

Every result carries its provenance. A definitive match on the inspected
content is Conclusive evidence and may establish an Unknown claim by itself;
name-derived, ambiguous, or contradictory results are only ever a reviewable
Identification Candidate. Work Identification and Site Recognition stay
independent, and no candidate ever supplies a title, a site, or artwork to
ordinary browsing. Video Files whose established work identity is the same are
associated into one Video, keeping the earliest Discovery Date, both
identification histories, and each Account's private viewing state.

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
