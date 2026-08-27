# Core owns rules and Infrastructure owns I/O

The solution starts with four production projects and one dependency direction:

```text
Prdb.Viewer.Core             domain rules; no I/O, package, or project references
Prdb.Viewer.Infrastructure   persistence, filesystem, Prdb.Sdk, Prdb.Hashing, ffprobe/ffmpeg
Prdb.Viewer.Host             HTTP, authentication, Background Work, composition, static assets
Prdb.Viewer.Frontend         React and TypeScript

Core <- Infrastructure <- Host
```

This adopts the boundary used by `prdb-fab`, but the Viewer-specific reason is
privacy and source-media safety: Personal State must always remain Account
scoped, while mounted Video Files are read but never mutated. Persistence rows
stay in Infrastructure; Core receives narrow immutable values and returns
decisions and reasons rather than logging or performing effects.

## Consequences

- Only Infrastructure opens database connections, filesystem handles, external
  HTTP connections, or media tools. Core may perform path arithmetic but does
  not inspect the filesystem.
- Host supplies the authenticated Account identity; Infrastructure scopes every
  Personal State read and write with it rather than accepting an arbitrary
  target Account from a public request.
- Architecture tests enforce project-reference direction and the restriction
  on filesystem access. A new production project is a new decision, not a
  default per-feature pattern.
