# Issue tracker: Local Markdown

Issues and specs for this repository live as Markdown files in `.scratch/`. These files are intentionally local and ignored by Git.

`.scratch/README.md` is the entry point: it lists what is open, what is ready to be worked on, what is blocked, and which efforts are finished. It is generated — never edited by hand — by:

```sh
node .scratch/update-index.mjs
```

Run it after creating, claiming, or resolving a ticket, and after moving an effort. It also refreshes the ticket table under a `## Tickets` heading in each effort's `spec.md` or `map.md`.

## Layout

- `.scratch/active/<effort-slug>/` — an effort with at least one open ticket.
- `.scratch/archive/<effort-slug>/` — an effort whose every ticket is resolved. Move the directory here when the last one is resolved; nothing is deleted, and relative links keep working.
- `.scratch/roadmaps/<name>/` — the order several efforts are built in. A spec, not tickets.
- `.scratch/notes/` — findings that belong to no single effort: a review, a measurement, a list of wishes for someone else's API.
- `.scratch/artifacts/` — screenshots, captures, and other disposable working output, deliberately absent from the ticket lists.

## Conventions

- One effort per directory: `.scratch/<active|archive>/<effort-slug>/`
- The spec is `spec.md`, or `map.md` for an effort charted by `/wayfinder`
- Issues are one file per ticket at `issues/<NN>-<slug>.md`, numbered from `01` — never a single combined tickets file
- Comments and conversation history append to the bottom of the file under a `## Comments` heading

## When a skill says "publish to the issue tracker"

Create a new file under `.scratch/active/<effort-slug>/`, creating the directory if needed, then regenerate the index.

## When a skill says "fetch the relevant ticket"

Read the file at the referenced path. The user will normally pass the path or issue number directly.

## Wayfinding operations

Used by `/wayfinder`. The **map** is a file with one **child** file per ticket.

- **Map**: `.scratch/active/<effort>/map.md` — the Notes / Decisions-so-far / Fog body.
- **Child ticket**: `.scratch/active/<effort>/issues/NN-<slug>.md`, numbered from `01`, with the question in the body. A `Type:` line records the ticket type (`research`/`prototype`/`grilling`/`task`); a `Status:` line records `claimed`/`resolved`.
- **Blocking**: a `Blocked by: NN, NN` line near the top, or `<effort>/NN` for a ticket in another effort. A ticket is unblocked when every file it lists is `resolved`.
- **Frontier**: read the Ready-now section of `.scratch/README.md`, or scan the effort's `issues/` for files that are open, unblocked, and unclaimed; first by number wins.
- **Claim**: set `Status: claimed` and save before any work.
- **Resolve**: append the answer under an `## Answer` heading, set `Status: resolved`, then append a context pointer (gist + link) to the map's Decisions-so-far in `map.md`.
- **Close the effort**: when its last ticket is resolved, move the directory from `active/` to `archive/` and regenerate the index.
