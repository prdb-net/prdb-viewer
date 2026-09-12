#!/usr/bin/env bash

# Starts the committed compose.yaml, so the file a deployer copies cannot rot
# into an example that no longer parses, no longer starts, or mounts something
# other than what the README says it does. The image under test replaces the
# published one; everything else in the file is used exactly as committed.

set -euo pipefail

image="${1:?Usage: docker/compose-test.sh <image> [host-port]}"
port="${2:-18081}"

repository="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly repository
readonly compose_file="$repository/compose.yaml"
readonly startup_timeout_seconds=180

# The identity and the container paths the committed file promises. A change to
# compose.yaml that moves any of them has to move the README with it.
readonly expected_uid=1000
readonly expected_gid=1000
readonly data_mount=/data
readonly library_mount=/libraries/main

workspace="$(mktemp --directory)"
composed=false

compose() {
    docker compose \
        --project-directory "$workspace" \
        --file "$workspace/compose.yaml" \
        --file "$workspace/compose.test.yaml" \
        "$@"
}

cleanup() {
    if [ "$composed" = true ]; then
        compose down --timeout 10 >/dev/null 2>&1 || true
    fi

    # The application data belongs to the identity inside the container and its
    # directories are private, so it is removed from inside one.
    docker run --rm \
        --volume "$workspace:/workspace" \
        --entrypoint /bin/sh \
        "$image" \
        -c 'rm -rf /workspace/data /workspace/library' >/dev/null 2>&1 || true
    rm -rf "$workspace" 2>/dev/null || true
}
trap cleanup EXIT

fail() {
    echo "FAIL: $*" >&2

    if [ "$composed" = true ]; then
        compose logs 2>&1 | sed 's/^/    /' >&2
    fi

    exit 1
}

pass() { echo "ok: $*"; }

pinned_version="$(sed -n 's|^ *image: *prdbnet/prdb-viewer:\(.*\)$|\1|p' "$compose_file")"
[ -n "$pinned_version" ] \
    || fail "compose.yaml does not pin a prdbnet/prdb-viewer image version"

released_version="$(sed -n 's|.*<VersionPrefix>\(.*\)</VersionPrefix>.*|\1|p' \
    "$repository/Directory.Build.props")"
[ "$pinned_version" = "$released_version" ] \
    || fail "compose.yaml pins $pinned_version while this is $released_version. Cutting a release updates both."
pass "compose.yaml pins the version this release publishes ($released_version)"

docker compose --file "$compose_file" config >/dev/null \
    || fail "compose.yaml is not a valid Compose file on its own"
pass "compose.yaml is valid on its own"

cp "$compose_file" "$workspace/compose.yaml"
mkdir -p "$workspace/data" "$workspace/library"
printf 'source media stays untouched\n' > "$workspace/library/marker.txt"

# Only the image and the two host paths are replaced. The published port moves
# so the test does not need 8080 on the machine that runs it. Both mounts keep
# the container paths the committed file names, because those are what is
# checked below.
cat > "$workspace/compose.test.yaml" <<YAML
services:
  viewer:
    image: $image
    ports: !override
      - "$port:8080"
    volumes:
      - ./data:$data_mount
      - ./library:$library_mount:ro
YAML

composed=true
compose up --detach >/dev/null || fail "docker compose up did not start the service"

answered=false
for _ in $(seq "$startup_timeout_seconds"); do
    if curl --silent --fail "http://localhost:$port/api/health" >/dev/null 2>&1; then
        answered=true
        break
    fi

    if [ -z "$(compose ps --quiet --status running)" ]; then
        fail "the service exited before it answered"
    fi

    sleep 1
done

[ "$answered" = true ] \
    || fail "no answer from /api/health within ${startup_timeout_seconds}s"
pass "the committed Compose file brings up an installation that answers"

database_owner="$(compose exec -T viewer stat --format '%u:%g' "$data_mount/prdb-viewer.db")"
[ "$database_owner" = "$expected_uid:$expected_gid" ] \
    || fail "application data belongs to $database_owner rather than the committed $expected_uid:$expected_gid"
pass "the committed PUID and PGID own the application data"

compose exec -T viewer sh -c \
    "test -r $library_mount/marker.txt && ! touch $library_mount/changed.txt 2>/dev/null" \
    || fail "the library mount was not readable and read-only at $library_mount"
[ ! -e "$workspace/library/changed.txt" ] \
    || fail "the container changed the library mount"
pass "the library mount is readable and read-only at $library_mount"

compose down --timeout 10 >/dev/null || fail "docker compose down did not stop the service"
composed=false
pass "docker compose down stops the installation"

echo "All Compose checks passed for $image."
