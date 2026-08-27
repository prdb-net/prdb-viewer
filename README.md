# prdb-viewer

`prdb-viewer` turns mounted home video directories into a fast, shared,
self-hosted library enriched with metadata from prdb.net. Each approved User
sees the same Videos while playback history and personal organisation remain
private to their Account.

The project is in active development. The current executable is the Walking
Skeleton: it proves the application, database, frontend, HTTP contract, test,
and container paths before product capabilities are added as vertical slices.
See [VISION.md](VISION.md) for the product contract.

## Prerequisites

- .NET 10 SDK
- Node.js 24 and npm
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

Run the production-shaped smoke test against a built image:

```bash
docker/smoke-test.sh prdb-viewer:local
```

The smoke test verifies startup migration, the non-root process identity, the
application-data owner, `ffprobe` and `ffmpeg`, read-only source media, and
graceful shutdown.
