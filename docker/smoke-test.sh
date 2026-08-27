#!/usr/bin/env bash

set -euo pipefail

image="${1:?Usage: docker/smoke-test.sh <image> [host-port]}"
port="${2:-18080}"

readonly test_uid=1234
readonly test_gid=5678
readonly startup_timeout_seconds=180
readonly stop_timeout_seconds=10

container=""
workspace="$(mktemp --directory)"

cleanup() {
    if [ -n "$container" ]; then
        docker rm --force "$container" >/dev/null 2>&1 || true
    fi

    docker run --rm \
        --volume "$workspace:/workspace" \
        --entrypoint /bin/sh \
        "$image" \
        -c 'rm -rf /workspace/data /workspace/library' >/dev/null 2>&1 || true
    rmdir "$workspace" 2>/dev/null || true
}
trap cleanup EXIT

fail() {
    echo "FAIL: $*" >&2

    if [ -n "$container" ]; then
        docker logs "$container" 2>&1 | sed 's/^/    /' >&2
    fi

    exit 1
}

pass() { echo "ok: $*"; }

if docker run --rm "$image" >/dev/null 2>&1; then
    fail "the image started without a persistent /data mount"
fi
pass "a persistent application-data mount is required"

mkdir -p "$workspace/data" "$workspace/library"
printf 'source media stays untouched\n' > "$workspace/library/marker.txt"

operator_output="$(docker run --rm \
    --volume "$workspace/data:/data" \
    --env "PUID=$test_uid" \
    --env "PGID=$test_gid" \
    "$image" \
    dotnet Prdb.Viewer.Host.dll bootstrap-authorize)"

[[ "$operator_output" == *"The single-use credential was written to /data/operator/bootstrap-authorization.txt."* \
    && "$operator_output" == *"Its value is never written to logs or command output."* ]] \
    || fail "the Bootstrap Authorization command did not report safe file delivery"

credential_mode="$(docker run --rm \
    --volume "$workspace/data:/data:ro" \
    --entrypoint stat \
    "$image" \
    --format='%a' /data/operator/bootstrap-authorization.txt)"
[ "$credential_mode" = 600 ] \
    || fail "the Bootstrap Authorization file mode is $credential_mode rather than 600"
credential_owner="$(docker run --rm \
    --volume "$workspace/data:/data:ro" \
    --entrypoint stat \
    "$image" \
    --format='%u:%g' /data/operator/bootstrap-authorization.txt)"
[ "$credential_owner" = "$test_uid:$test_gid" ] \
    || fail "the Bootstrap Authorization belongs to $credential_owner rather than $test_uid:$test_gid"
pass "operator credentials use restrictive application-data files"

container="$(docker run --detach \
    --publish "$port:8080" \
    --volume "$workspace/data:/data" \
    --volume "$workspace/library:/libraries/source:ro" \
    --env "PUID=$test_uid" \
    --env "PGID=$test_gid" \
    "$image")"

answered=false
for _ in $(seq "$startup_timeout_seconds"); do
    if curl --silent --fail "http://localhost:$port/api/health" >/dev/null 2>&1; then
        answered=true
        break
    fi

    if [ -z "$(docker ps --quiet --filter "id=$container")" ]; then
        fail "the container exited before it answered"
    fi

    sleep 1
done

[ "$answered" = true ] \
    || fail "no answer from /api/health within ${startup_timeout_seconds}s"
pass "the application migrates and answers"

database_owner="$(docker exec "$container" stat --format '%u:%g' /data/prdb-viewer.db)"
[ "$database_owner" = "$test_uid:$test_gid" ] \
    || fail "the database belongs to $database_owner rather than $test_uid:$test_gid"
pass "application data belongs to PUID:PGID"

database_mode="$(docker exec "$container" stat --format '%a' /data/prdb-viewer.db)"
[ "$database_mode" = 600 ] \
    || fail "the database file mode is $database_mode rather than 600"
pass "the database is private to the application identity"

process_identity="$(docker exec "$container" sh -c \
    "awk '/^Uid:/{uid=\$2} /^Gid:/{gid=\$2} END{print uid \":\" gid}' /proc/1/status")"
[ "$process_identity" = "$test_uid:$test_gid" ] \
    || fail "PID 1 runs as $process_identity rather than $test_uid:$test_gid"
pass "the application runs as the requested non-root identity"

docker exec --user "$test_uid:$test_gid" "$container" ffprobe -version >/dev/null
docker exec --user "$test_uid:$test_gid" "$container" ffmpeg -version >/dev/null
pass "ffprobe and ffmpeg are available"

docker exec --user "$test_uid:$test_gid" "$container" sh -c \
    'test -r /libraries/source/marker.txt && ! touch /libraries/source/changed.txt 2>/dev/null' \
    || fail "the source-media mount was not readable and read-only"
[ ! -e "$workspace/library/changed.txt" ] \
    || fail "the container changed the source-media mount"
pass "source media is readable and cannot be changed"

started_stopping="$(date +%s)"
docker stop --timeout "$stop_timeout_seconds" "$container" >/dev/null
took="$(($(date +%s) - started_stopping))"

[ "$took" -lt "$stop_timeout_seconds" ] \
    || fail "the application did not stop before Docker's kill timeout"
pass "docker stop reaches PID 1 (${took}s)"

echo "All checks passed for $image."
