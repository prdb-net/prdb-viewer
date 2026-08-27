#!/bin/bash

set -eu

say() { printf 'entrypoint: %s\n' "$*"; }
refuse() { printf 'entrypoint: %s\n' "$*" >&2; exit 1; }

data_directory="${VIEWER_DATA_DIRECTORY:-/data}"
puid="${PUID:-1000}"
pgid="${PGID:-1000}"
umask_value="${UMASK:-022}"

case "$data_directory" in
    /data | /data/*) ;;
    *) refuse "VIEWER_DATA_DIRECTORY must be /data or a path beneath it." ;;
esac

case "$puid" in '' | *[!0-9]*) refuse "PUID must be a positive numeric uid." ;; esac
case "$pgid" in '' | *[!0-9]*) refuse "PGID must be a positive numeric gid." ;; esac
[ "$puid" -gt 0 ] || refuse "PUID must not be root."
[ "$pgid" -gt 0 ] || refuse "PGID must not be root."

case "$umask_value" in
    [0-7][0-7][0-7] | [0-7][0-7][0-7][0-7]) ;;
    *) refuse "UMASK must be a three or four digit octal mask." ;;
esac

mountpoint --quiet /data \
    || refuse "/data is not a mount. Refusing to start without persistent application data."

umask "$umask_value"

if [ "$(id -u)" -ne 0 ]; then
    mkdir -p "$data_directory" \
        || refuse "The application identity cannot create $data_directory."
    say "Starting as the container-provided identity $(id -u):$(id -g)."
    exec "$@"
fi

mkdir -p "$data_directory"
chown --recursive "$puid:$pgid" "$data_directory" \
    || refuse "Could not give $puid:$pgid ownership of $data_directory."

HOME="$data_directory"
export HOME

say "Starting as $puid:$pgid with umask $umask_value and data in $data_directory."
exec setpriv --reuid "$puid" --regid "$pgid" --clear-groups -- "$@"
