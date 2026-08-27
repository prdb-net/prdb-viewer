# Ship one container that never owns source media

`prdb-viewer` ships as one Debian-based, multi-stage container for `linux/amd64`
and `linux/arm64`. It contains the ASP.NET Core runtime, `ffprobe` and `ffmpeg`,
serves the built frontend itself, writes only beneath its required application
data mount, and treats every Library Directory mount as source media that it
must never change.

The entrypoint validates `PUID`, `PGID`, and `UMASK`, prepares only the data
directory, drops privileges with `setpriv`, and `exec`s the application as PID
1. It never changes ownership or permissions below a Library Directory. The
image declares no automatic `VOLUME` for application data, because an anonymous
volume would make a missing durable mount appear to work until the container is
replaced.

Environment variables are admitted only when their values are required before
the application can start, such as the listener, data path, and process
identity. Accounts, the prdb credential, Library Directories, and other product
configuration belong to guided application flows; operator-only recovery and
Bootstrap Authorization belong to explicit CLI actions rather than durable
environment configuration.

## Consequences

- Node and frontend build dependencies never enter the runtime image.
- Preview generation writes derived artefacts to application data and never
  beside the source Video File.
- Container smoke tests start both published architectures and verify process
  identity, required mounts, read-only source-media behaviour, signal handling,
  and the presence of the media tools.
