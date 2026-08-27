# prdb-viewer

`prdb-viewer` turns mounted home video directories into a fast, shared,
self-hosted library enriched with metadata from prdb.net. Each approved User
sees the same Videos while playback history and personal organisation remain
private to their Account.

The project is in active development. The current executable provides local
Account access, guided Installation Configuration, durable Library Scans, and
technical inspection of discovered Video File Candidates. Browser playback is
the next vertical slice. See [VISION.md](VISION.md) for the product contract.

## Prerequisites

- .NET 10 SDK
- Node.js 24 and npm
- FFmpeg (`ffprobe`) when running the Host outside the container
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
