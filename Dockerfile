# syntax=docker/dockerfile:1

FROM --platform=$BUILDPLATFORM node:24-bookworm-slim AS frontend

WORKDIR /source/src/Prdb.Viewer.Frontend

COPY src/Prdb.Viewer.Frontend/package.json src/Prdb.Viewer.Frontend/package-lock.json ./
RUN npm ci

COPY src/Prdb.Viewer.Frontend/ ./
RUN npm run build


FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS backend

WORKDIR /source

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Prdb.Viewer.Core/Prdb.Viewer.Core.csproj src/Prdb.Viewer.Core/
COPY src/Prdb.Viewer.Infrastructure/Prdb.Viewer.Infrastructure.csproj src/Prdb.Viewer.Infrastructure/
COPY src/Prdb.Viewer.Host/Prdb.Viewer.Host.csproj src/Prdb.Viewer.Host/
RUN dotnet restore src/Prdb.Viewer.Host/Prdb.Viewer.Host.csproj

COPY src/Prdb.Viewer.Core/ src/Prdb.Viewer.Core/
COPY src/Prdb.Viewer.Infrastructure/ src/Prdb.Viewer.Infrastructure/
COPY src/Prdb.Viewer.Host/ src/Prdb.Viewer.Host/
COPY --from=frontend /source/src/Prdb.Viewer.Host/wwwroot src/Prdb.Viewer.Host/wwwroot

ARG VERSION=0.1.0-local
ARG COMMIT_SHA=unknown
RUN dotnet publish src/Prdb.Viewer.Host/Prdb.Viewer.Host.csproj \
        --no-restore \
        --configuration Release \
        --output /application \
        -p:SkipFrontendBuild=true \
        -p:OpenApiGenerateDocuments=false \
        -p:Version=${VERSION} \
        -p:InformationalVersion=${VERSION} \
        -p:SourceRevisionId=${COMMIT_SHA}


FROM debian:13-slim AS microsoft-feed

RUN apt-get update \
    && apt-get install --yes --no-install-recommends ca-certificates wget \
    && wget --quiet \
        https://packages.microsoft.com/config/debian/13/packages-microsoft-prod.deb \
        --output-document /packages-microsoft-prod.deb


FROM debian:13-slim AS runtime

ARG VERSION=0.1.0-local
ARG COMMIT_SHA=unknown

LABEL org.opencontainers.image.title="prdb-viewer" \
      org.opencontainers.image.description="Browse and play a local video library with prdb metadata." \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${COMMIT_SHA}" \
      org.opencontainers.image.source="https://github.com/prdb-net/prdb-viewer"

COPY --from=microsoft-feed /packages-microsoft-prod.deb /tmp/packages-microsoft-prod.deb

# Microsoft supports .NET 10 on Debian 13 but does not publish a Debian-based
# ASP.NET 10 image. Install its signed Debian package so the runtime still
# satisfies ADR 0007 without assembling .NET from an unofficial binary source.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends ca-certificates \
    && dpkg --install /tmp/packages-microsoft-prod.deb \
    && rm /tmp/packages-microsoft-prod.deb \
    && apt-get update \
    && apt-get install --yes --no-install-recommends aspnetcore-runtime-10.0 ffmpeg util-linux \
    && rm --recursive --force /var/lib/apt/lists/*

WORKDIR /application
COPY --from=backend /application ./
COPY --chmod=0755 docker/entrypoint.sh /usr/local/bin/viewer-entrypoint

ENV VIEWER_DATA_DIRECTORY=/data \
    ASPNETCORE_HTTP_PORTS=8080 \
    PUID=1000 \
    PGID=1000 \
    UMASK=077

EXPOSE 8080

ENTRYPOINT ["/usr/local/bin/viewer-entrypoint"]
CMD ["dotnet", "Prdb.Viewer.Host.dll"]
