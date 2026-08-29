# Changelog

All notable user-visible changes to `prdb-viewer` are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.4.1] - 2026-08-29

Six defects found by walking the 0.4.0 shell in a browser, and the small
inconsistencies alongside them. Nothing here changes what the product does; it
corrects places where it did not do what it already said it did.

### Fixed

- Use the Viewer in more than one tab. The token that proves a change came from
  the application is now a property of your session rather than something reissued
  each time a tab asked who you were, so opening a second tab no longer refuses
  every action in the first one until it is reloaded. See
  [ADR 0017](docs/adr/0017-derive-the-csrf-token-from-the-session-token.md).
- Type a search without losing letters. The field kept what you typed rather
  than what the address had caught up to, so a character arriving between the
  two is no longer dropped, and the library is asked once typing settles instead
  of once per keystroke.
- Reach your Account on a narrow viewport. It is a destination in the navigation
  like every other screen, rather than only a shortcut in a header that has no
  room for it below 640 pixels.
- Tab past a closed drawer. A drawer that is shut is hidden rather than only
  moved off-screen, so it no longer holds eight invisible stops for the keyboard
  or reads out to a screen reader. An open one closes on Escape, returns the
  focus to the control that opened it, and holds the page behind it still.
- Read the Lanes section as what it says it is. It shows each lane once, at its
  newest run, with when that run last did something — rather than a row per run,
  which grew by six after every Scan with nothing to say which row was current.
- Act on what a refusal actually said. A request that is turned down reports the
  instruction the API gave, instead of replacing it with "try again" — advice
  which, for a refusal that asks you to reload, could not work.
- Tell the shelves apart from each other in the history and the tab bar: the
  window is named for the screen that is open. Continue Watching, Favourites and
  Watch Later say what their count counts, and Identification review states an
  empty queue the way every other screen states an empty one.

## [0.4.0] - 2026-08-29

The Viewer becomes an application you navigate rather than one page you scroll.
A persistent shell carries search, identity and a sidebar built to hold more
destinations than it has today, every screen is its own address, and a Video has
a page of its own.

### Added

- Navigate the Viewer from a persistent sidebar, grouped by what it is for.
  Administration is a section rather than four panels stacked beneath the
  library, and the entries that lead to waiting work carry its count, so
  Operational Attention and an open identification queue are visible without
  opening the screen behind them. On a narrow viewport the same navigation
  opens as a drawer.
- Reach one Video at its own address. That page owns playback — the play
  action, the order the Video Files are tried in, the visible fallback after a
  decode failure, and what each kind of failure means — together with the
  Video's provenance, its facts and your own organisation of it. Every other
  surface links to it, so a Video reached from a shelf, from a search, or from a
  link somebody sent is the same screen.
- Follow a link to a Video that has since been merged into another one. The
  address answers as the Video that survived the merge and says that it did,
  rather than failing. A Video that has left the library says so plainly.
- Browse Continue Watching, Favourites and Watch Later as their own pages
  instead of as shelves above the library, each stating that it is private to
  your Account.
- Search from anywhere. The search field belongs to the shell, and what it finds
  is the library page at an address you can send to someone else.

### Changed

- The library's search, facets, sort order and revealed depth now live in the
  address, as do the identification case an Administrator has open. Returning
  to an address reproduces what you were looking at, which is what ADR 0004
  asked for and what component state could not give.
- Direct address does not apply the admission rule of Ordinary Discovery. A
  Video your browser cannot play is still shown when you ask for it by name,
  because following a link is your decision to look rather than the library's
  decision to offer.
- Showing unsupported Videos in ordinary results moved to your Account, where
  standing preferences belong; it was a checkbox among the facets, which are
  filters on one view. The control that reveals what the current rules keep out
  still sets it from the library, where you notice it.
- A Work Issue that names what to correct now navigates to it. It previously
  scrolled to a section that happened to be on the same page.
- What an Account may reach is decided in the navigation and in the routes as
  well as at the API, so an address typed by hand meets the same answer as an
  entry that is not offered.
- The signed-in interface no longer opens with a full-height title. An
  application says where you are; the space belongs to the library.

## [0.3.0] - 2026-08-28

Playability becomes a fact about your browser rather than a guess for everyone,
and the two remaining MVP points — local site recognition and visible
unsupported Videos — are built.

### Added

- Decide direct playability for each Account on each browser rather than
  installation-wide. Your browser qualifies the media configurations the library
  holds with Media Capabilities, what it observed when it actually played a file
  outranks that prediction, and Ordinary Discovery admits a Video by the result.
  Both are Personal State: they never influence another Account and are never
  shown to an Administrator as activity.
- Try each Available Video File at most once per play action, in the order the
  evidence dictates, and say which file was chosen and why. A decode failure is
  remembered for that browser and the next variant is tried visibly; a delivery
  or network failure stops the fallback and says so, because every other variant
  would fail the same way.
- Offer the three playability states as what they are: the ordinary Play action,
  a labelled Try Direct Play with its reason, or variant details with an explicit
  Try Anyway. A remembered failure can be forgotten with one explicit retry.
- Retain the exact inspected media configuration — profile, level, bit depth,
  frame rate, bitrate and audio layout — so a browser can be asked about the
  file it would actually play.
- Recognise a Video's originating site from the Video File's own path when prdb
  cannot match it, against a Site Directory fetched from prdb at most once a day
  and joined with every Site the installation has already established. A path
  that names exactly one known site establishes a Site Recognition sourced as
  local inference; a path that names several, or names one only through a short
  word, proposes a reviewable Identification Candidate instead.
- Show where a Site came from wherever it appears — from prdb, recognised
  locally, or set by an Administrator — in browsing and in the identification
  review, so a local reading is never mistaken for an established prdb match.
- Show unsupported Videos with their title, preview, provenance and Personal
  State, stating the container and codecs that cannot be played directly in
  place of a Play button. The per-Account preference is now a standing control
  that can be turned off again, and `Unsupported only` narrows one view without
  changing it.

### Changed

- Baseline Candidate now means what the product contract says: a conforming WebM
  with VP8 and Vorbis or no audio at ordinary demands. Ordinary H.264/AAC in MP4
  is Client-Dependent, which is what it always was — it is now offered once your
  browser has confirmed it rather than assumed for everyone. Existing
  installations keep their classifications and rescan for the facts the new rules
  need, so nothing disappears while that runs.
- The library filter `readiness` became `playability` and reports Client Video
  Playability.
- A locally recognised Site gives way, without review, to the canonical Site of
  a work prdb later identifies, and the reading it replaces is retained as
  history. An Administrator's decision is never replaced this way.
- The identification review names the origin of each proposal and compares the
  claim it is actually about, rather than always showing the Work
  Identification beside a proposed Site.

## [0.2.0] - 2026-08-28

Library discovery: the library is now searchable, filterable, sortable, and
loaded a page at a time. No image was published under this version; its changes
ship in 0.3.0.

### Added

- Search the library by Established title, Site and Actor, and by the local
  display label and file names of Unknown Videos. It ignores case, diacritics
  and ordinary punctuation, requires every term to match somewhere, and ranks an
  exact title or label above other titles, then Sites and Actors, then file
  names.
- Filter by Site, Actor, Work Identification, review status, playability,
  availability and the Account's own Personal Play State. Values inside one
  facet combine with OR and the facets combine with AND, with explicit values
  for Unknown Work Identification, Unknown Site and Review Needed.
- Sort by Newest or Title A-Z. Newest is Discovery Date descending, so later
  enrichment never makes an old Video look newly added.
- Report the matches the current rules keep out — not ready for direct play, or
  currently unavailable — with the control that reveals them, instead of
  dropping them silently.
- Add a per-Account preference that includes Videos which are not ready for
  direct play in ordinary results; an explicit playability filter overrides it
  for one view.
- Maintain a discovery projection for each Video and project Established Actors
  as their own rows, so the library filters and orders in the database.

### Changed

- `GET /api/library/videos` returns a page rather than the whole library, with
  the total, the hidden-match counts and whether more follows. It takes the
  search, facet, sort and paging parameters. Opening a 20,000-Video library
  costs 6 ms instead of 917 ms; see [docs/performance.md](docs/performance.md).

## [0.1.2] - 2026-08-28

### Fixed

- Accept a container path that carries surrounding whitespace or a trailing
  separator when staging a Library Directory. A path is pasted far more often
  than it is typed, and neither says anything about what was meant.

### Changed

- Report an unusable display name and an unusable container path as separate
  staging outcomes, `InvalidName` and `InvalidPath`, so the browser can say
  which field to correct instead of naming both. This replaces the shared
  `InvalidInput` outcome of the Library Directory staging contract.

## [0.1.1] - 2026-08-28

### Fixed

- Complete the first-Administrator claim even when the spent Bootstrap
  Authorization file cannot be removed. Deleting it is cleanup after the Account
  has already been created and committed, so a file the application has no
  permission to delete — one generated by a different identity, which is what
  `docker exec` as root produces — now leaves a warning naming the file instead
  of failing the request. The same applies to a redeemed Recovery Code.

## [0.1.0] - 2026-08-28

The first release: a self-hosted library that scans mounted video directories,
plays them directly in the browser, keeps each Account's viewing private, and
enriches Videos from prdb.net — with observable operations and operator backup
and restore behind it.

### Added

- Add the first runnable application shell, SQLite startup migration, public
  liveness endpoint, generated browser API contract, and production-shaped
  container baseline.
- Add one-time operator-authorized installation claiming, local Account
  sessions, approval-gated registration, account administration, and
  single-use recovery codes.
- Add guided prdb credential verification and staged activation of readable
  Library Directories within the documented read-only mount area.
- Add durable, restart-resumable Library Scans and bounded technical inspection
  with `ffprobe`, rename and absence reconciliation, visible Work Issues, and
  Administrator-triggered rescans.
- Add the authenticated shared Video catalogue, static Direct-Play
  Classification, durable First Playable Video Milestone, and anonymous opaque
  video delivery with HTTP byte-range support.
- Add Account-private playback reporting, resume, watch duration, play count,
  completion and Personal Play State, plus Continue Watching, Favourites,
  Watch Later, and one-to-five Personal Ratings.
- Add content hashing, prdb identification with explicit provenance, retained
  remote metadata, locally generated preview images, automatic association of
  Video Files that share one work identity, and an Administrator identification
  review queue with previewed, version-bound decisions.
- Add the observable operations surface: aggregated Work Issues with stable
  references, severities, remediation owners and secret-free Operator Handoffs,
  installation-wide Operational Attention, the durable Background Work pause,
  cancellation of a bounded run, version-bound retry and recheck actions,
  playback-first throttling of the background lanes, and a refused prdb
  credential identified by a one-way fingerprint rather than by its characters.
- Add the encrypted, integrity-protected Backup Archive with independent
  validation and Restore into an empty target through the container CLI, using
  a passphrase that is never an argument, never logged, and never stored.
- Honour `X-Forwarded-Proto` and `X-Forwarded-For` when
  `VIEWER_BEHIND_REVERSE_PROXY=true`, so a TLS-terminating proxy keeps the
  session cookie `Secure` and rate limiting sees the real client address.
- Publish multi-architecture release images with commit-SHA, semantic-version,
  and `latest` tags, and stamp the version and commit into every Operator
  Handoff.
