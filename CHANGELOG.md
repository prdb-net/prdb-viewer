# Changelog

All notable user-visible changes to `prdb-viewer` are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.14.0] - 2026-09-04

The browsing screen and the three Personal Shelves open on Videos. The facets
stood open on every viewport but a narrow one, where five groups took the upper
two thirds of the screen and left a single row of cards beneath them; they now
wait behind the `Filters` control everywhere and open as a sheet — beside the
grid where there is room for a column, up over the Videos where there is not.
What is chosen stays outside it, in a row that keeps its place while the Videos
scroll past. Nothing about the HTTP API, configuration, the Backup Archive or
the schema changes.

### Changed

- The facets wait behind the `Filters` control on every screen, not only on a
  narrow one. A wide screen was not the exception it looked like: the five
  groups filled its upper two thirds, so the browsing screen and each Personal
  Shelf opened on a single row of Videos. They now open as a sheet — beside the
  grid where there is room for a column, up over the Videos where there is not
  — which answers Escape, gives the focus back to the control that opened it,
  and says how many Videos the narrowing being assembled admits.
- What is chosen stays out of the sheet. The row that names each chosen filter
  with the control that takes it out again, the count of what they leave, the
  sort order and the control that opens the facets stay put while the Videos
  scroll past, so narrowing a long list no longer begins with a scroll back to
  the top.

### Fixed

- The sort order's list opens in the colours of the rest of the application. The
  browser had been told nothing about the dark surface it draws on, so it opened
  a light list and filled it with the pale text the dark page asks for, leaving
  every order but the chosen one barely readable.

## [0.13.0] - 2026-09-02

Continue Watching, Favourites and Watch Later have what the browsing screen has.
Each shelf used to be a list of everything on it, with no search, no filters, no
order and no paging, and typing into the search field while one was open led
away from it to the whole library. A shelf is now the library narrowed to it:
the same search, facets, sort orders and paging, kept in the address, and the
search searches the shelf that is open. Nothing about configuration or the
Backup Archive changes. The HTTP API's library endpoints gain a `shelf` facet
and a shelf order; the personal library endpoint that answered all three shelves
at once is withdrawn.

### Added

- The search in the header searches where it is. On Continue Watching,
  Favourites or Watch Later it searches that shelf and says so, and the shelf's
  screen offers the whole library for the same words one link away. Everywhere
  else it searches the whole library, as before.
- The three shelves take the same narrowing the browsing screen does — the
  search, every facet, the sort order and how much has been revealed — and keep
  it in the address, so a search of the Favourites is a page that can be
  returned to. The facet counts describe the shelf. A shelf with nothing
  matching says so and offers the whole library; a shelf with nothing on it
  still explains what would put something there.
- Each shelf opens in the order it keeps: Continue Watching by what was watched
  last, Favourites by what was added last, Watch Later oldest first, because it
  is a queue. The order is named for what it is on that shelf and can be
  exchanged for any of the library's.
- The browsing screen offers the three shelves as a facet under **Yours**, so
  unplayed Favourites from one Site is a question the library can answer. With
  a shelf chosen, its order is offered beside the others without being imposed.

### Changed

- A shelf shows what was put on it whether or not this browser can play it, as
  it did before, while the browsing screen keeps its admission rule. A card
  still says when a Video will not play here.

### Removed

- The HTTP API no longer answers `GET /api/personal/library`. The three shelves
  are answered by the library's own endpoints with the `shelf` facet, which is
  what the browser now asks.

## [0.12.0] - 2026-09-02

The browsing screen shows Videos rather than controls. A library of forty Videos
opened on seven rows of facet pills and forty cards each carrying nine controls
and the same three sentences; on a phone the first Video was two screens down.
The facets now offer their most populated values and keep the rest behind one
control, what is chosen is listed in one row with the control that takes it out
again, and a card says only what is exceptional about its Video. Nothing about
configuration or the Backup Archive changes. The HTTP API gains three sort
orders and lets the facets take the same narrowing the Videos do; nothing it
already answered changes.

### Changed

- A card carries its runtime on the picture, opposite the quality, and its
  Favourite and Watch Later controls on the picture too, where a thumb or a
  pointer finds them; they show themselves on hover, on focus, wherever nothing
  can hover, and whenever one of them is set. A Personal Rating is shown where
  there is one and can be changed where it is shown; five empty stars on every
  unrated card were a control nobody had asked for, forty times over, and the
  Video's own page is where a first rating is given.
- A card says only what distinguishes its Video. "prdb match", "from prdb" and
  "expected to play smoothly here" were true of every card and told nobody
  anything; the Site is said plainly, the Actors are named, and an Unknown
  Video, a review, a Site recognised only locally or a file that will not play
  here are still said in as many words. A title that is nothing but the Actors'
  names is not followed by the Actors' names: the Site and the Discovery Date
  take that line, because they are the two facts such a title leaves out.
- The facets offer their eight most populated values at once and the rest
  behind one control; a chosen value is always shown, wherever it ranks. What
  is chosen is listed in its own row, each entry with the control that takes it
  out again, beside one that clears everything. On a narrow viewport the facet
  rows wait behind a Filters control until asked for, so the first Video is on
  the first screen.
- Facet counts follow the current narrowing. A count used to describe the whole
  library whatever was chosen, so choosing an Actor left every Site claiming
  the Videos it had before; it now says what choosing that value would leave,
  and a facet is never narrowed by its own values, because a second Site widens
  the set rather than narrowing it.
- The Library can be ordered longest first, most recently played first and best
  rated first, beside newest, title and best quality. The two personal orders
  read the Account's own Personal State and put the Videos it has none for last.
- The count, the sort order and the control that clears the narrowing share one
  row above the facets; the count sat in the heading, the order under a label of
  its own and the clearing control beside it, three places for what one row says.
  The control that reveals more of a match says how much of it is on screen.
- The browsing screen and the personal shelves take the whole width of a wide
  screen, and a card grows with it, so a wide screen shows bigger pictures as
  well as more of them; the measure stays on the prose. A card answers the
  pointer as a whole, because the whole picture is the link.

## [0.11.0] - 2026-09-02

Identification review stops asking about answers the library already has. A
proposal naming what its Video has already established now confirms that claim
instead of becoming a candidate, and local Site Recognition no longer reads the
path of a Video prdb has already placed. The candidates the earlier releases
made this way are closed as part of the upgrade, so a queue holding nothing but
those empties itself. **This release migrates**, and the migration writes to
existing identification data, so take a copy of the data directory before
upgrading. Nothing about configuration, the HTTP API, or the Backup Archive
changes.

### Changed

- A proposal that names what is already established confirms the claim rather
  than opening a review. Agreement was the one thing a proposal could not
  express: an Identification Candidate repeating the current claim put its Video
  into review, both columns of the comparison said the same thing, and where the
  Site had come with the Work Identification the only decision the review
  offered was to reject evidence that was never wrong. Agreement is now recorded
  where agreement belongs, on the claim it agrees with.
- Local Site Recognition reads the paths of Videos whose Site is still open. It
  waited for prdb to answer before reading a path, which is what makes it local
  recognition rather than a race — but it then read the path whether or not the
  answer had named a Site, so a Video prdb had already placed was asked a
  question it had answered. A path that names a second known site along the way
  — a work title that happens to be a site's name — turned that into a review
  with nothing to decide. The condition that already governs re-reading a path
  now governs reading it the first time.
- Background work reports Site Recognition as the lane it now is: on an
  installation whose catalogue knows its library, it runs and finds nothing
  outstanding, which the screen shows as nought of nought rather than as a lane
  that never started.

### Fixed

- Identification Candidates proposing what their own Video already has
  established are closed on upgrade. They are marked Superseded rather than
  Rejected, because the evidence was never wrong — it was overtaken by an answer
  the library already held — and rejecting it would have suppressed the same
  path until materially stronger evidence appeared. Each affected case gets a
  new version, so a review screen holding one finds it changed and reloads.

## [0.10.0] - 2026-09-02

An identification review that names the decision it is waiting for. A case whose
Site came with the Work Identification refuses four of its five decisions, and
the one it is left with was identified only by the colour of the four that were
disabled; a candidate proposing what is already established has nothing for
accepting it to add. Both now reach an Administrator as a sentence above the
actions that names the decision, says why the proposal adds nothing where it
adds nothing, and says what the library keeps and what still waits once the
decision is made. Nothing about configuration, the HTTP API, the Backup Archive,
or the stored data changes, so an upgrade from `0.9.0` is the new image and
nothing else.

### Changed

- The identification review says what an open case is asking for, above the
  decisions rather than after one has been pressed. A case that refuses four of
  its five decisions has already made the choice, and one whose candidate
  proposes the identification that is already established leaves accepting it
  nothing to establish; both reached an Administrator as a row of buttons that
  named no answer, so finding the single decision a case still had meant reading
  the colour of the four that did not. The case now names that decision, says
  why the proposal adds nothing where it adds nothing, and says what the library
  keeps and what still waits once the decision is made.

## [0.9.0] - 2026-09-02

An identification review that says what the open case can do before anything is
pressed. A decision the case would refuse is refused by the screen itself, with
the reason underneath the actions, and the target fields appear only under the
two decisions that read them. A candidate proposing what is already established
now says so in the queue and beside the comparison, so a case that needs no
decision no longer looks like one that does. Nothing about configuration, the
HTTP API, the Backup Archive, or the stored data changes, so an upgrade from
`0.8.0` is the new image and nothing else.

### Changed

- Identification review offers the decisions the open case actually has. A
  decision the case would refuse — a Site that came with the Work Identification
  and so cannot be given a second truth of its own, replacing or withdrawing a
  claim where nothing is established, assigning one where something already is,
  splitting a Video without ticking a Video File — is now refused by the screen,
  which says why underneath the actions. All five used to be offered as though
  any of them were available, so the way to find out what a case does not do was
  to press things and read the sentence that came back.
- The target fields wait for the decision that reads them. `Assign directly` and
  `Replace claim` now ask for a target and name what they are asking for; the
  other decisions never read those fields and no longer open with a form
  suggesting they do.
- A candidate that proposes what is already established says so, in the queue
  and beside the comparison. Local recognition reads a Video File's own path
  whether or not prdb has already answered the same question, so a Video can
  wait for a decision between an established Site and a proposal naming that
  same Site — which reads as a screen asking for a decision it does not need
  until it admits that is what happened.

## [0.8.0] - 2026-09-02

Discovery that does not wait to be asked. A file copied into the mounted
library becomes a Video on its own: every Active Library Directory falls due
for another Library Scan six hours after the last one read it, and the
Background work and Installation screens say when each one is next. Until now
the row of `Scan` buttons was the whole of discovery, which made remembering to
press one part of using the product. **It migrates**: the schedule of an
existing installation is derived from the last Library Scan that finished
beneath each Library Directory, so a library scanned recently waits out the
rest of its period and one last scanned long ago is read shortly after the
upgrade. Migrations are forward-only, so a return to `0.7.0` needs a copy of
the application data taken before the upgrade.

### Added

- A file copied into the mounted library is found without anyone asking for it.
  Every Active Library Directory falls due for another Library Scan six hours
  after the last one read it, and the Background work and Installation screens
  say when each one is next — the row of `Scan` buttons used to be the only way
  a new file was ever discovered, and a screen that says nothing else reads as
  though it were the only way one ever could be. When a Scan is due is durable
  state per [ADR 0009](docs/adr/0009-persist-background-work-and-run-bounded-resource-lanes.md)
  rather than a timer the process holds: an installation that was down over its
  period scans when it starts again, one restarted twice within it does not scan
  twice, and a period that elapses while background work is paused is served
  when work resumes rather than skipped. A Scan that falls due never competes
  with one already running or with an Administrator's own request, and any Scan
  — asked for or not — starts the period again, so `Scan now` on a library
  already being read does not cost it a second traversal. **It migrates**: the
  schedule of an existing installation is derived from the last Library Scan
  that finished beneath each Library Directory, so a library scanned recently
  waits out the rest of its period and one last scanned long ago is read shortly
  after the upgrade.

## [0.7.0] - 2026-08-30

What a Video is worth watching at, and screens that say what they mean. The
library now states quality — in the corner of every preview, as a facet, and as
an order — and a Personal Rating is the five stars it always was rather than a
dropdown that had to be opened to be read. The rest came from walking every
screen of a seeded installation: five things the application was saying in its
storage's words, a row of decisions drawn in the browser's, and the smaller
gaps a real library would notice first. **It migrates**: Video Quality is a
column of its own, and because Unknown is a real answer as well as what a new
column defaults to, an upgrade clears every projection and rebuilds it at the
next start. Migrations are forward-only, so a return to `0.6.1` needs a copy of
the application data taken before the upgrade.

### Added

- Every Video says what it is worth watching at. The resolution of the occurrence
  a play action would reach for sits in the corner of its preview, with the frame
  rate beside it where it is above the ordinary thirty, and the Video's own page
  states the runtime, bitrate, audio layout and size of that occurrence and
  repeats the quality line against each of its Video Files. Inspection had been
  retaining all of this since the first Scan and the catalogue had been answering
  with it; only the screens had nothing to say about it, so a person could see
  that a Video would play here but not whether it was worth playing. A resolution
  is named the way a release is named rather than by its height alone — a film
  with the bars cut off and a recording held upright are both the 1080p they are
  — and where inspection established no dimensions, nothing is claimed.
- Narrow the Library to the quality bands it holds, and order it by best quality
  first. The facet offers only the bands there are something in, with their
  counts, and combines with OR inside itself and with AND across the other facets
  like every facet beside it. A Video's Video Quality is the best band among its
  Available Video Files, projected once per ADR 0013 and the same for every
  Account, so narrowing and ordering by it cost one indexed comparison rather
  than a decision taken per row. That is a deliberate exception to ADR 0015 —
  a filter could have meant what this browser would be shown, an order could not,
  and the reasoning is in
  [ADR 0018](docs/adr/0018-decide-video-quality-installation-wide.md). An
  upgrade rebuilds every projection once at startup to fill the new column.

### Changed

- A Personal Rating is shown as the five stars it is, rather than as a dropdown
  that had to be opened to be read. A rating is read far more often than it is
  set, and a shelf of cards used to carry one closed control per card, each
  hiding the one thing it was there to say. The stars state the score across a
  grid at a glance and take the click that changes it, so there is no separate
  reading form and setting form of a rating to keep in agreement. Underneath it
  stays one choice out of a fixed scale — a radio group, so the keyboard walks
  the scale unaided and a screen reader is told which Video the scale belongs to
  — and clearing is its own action, because "not rated" is an absence rather
  than a sixth score. Hovering shows what a click would make it before the click
  lands, and the Video's own page states the score in words beside the stars.
- `seed` writes its four video files at four different sizes rather than all at
  one. A screen that says what a Video is worth watching at looks the same on
  every card when every card is the same size, which is the one thing looking at
  a seeded installation is meant to catch. Only the WebM's size still carries
  weight, because the conservative baseline is the one classification with
  dimensions in it.
- Each facet row says what it is a row of. The rows named themselves only to a
  screen reader, so four rows of pills sat under one another and "1080p (1)"
  beside "Alex Doe (2)" left the reader to work out that one was a quality and
  the other a person. The label carries the same name the group is announced
  with, and stacks above its values where a narrow screen has no room beside
  them.
- A card states how long a Video runs and draws how far this Account got through
  it. The line beneath the title opened with the container and codecs —
  `mov,mp4,m4a,3gp,3g2,mj2 (h264 + aac)` — which is what the file is made of
  rather than anything somebody browsing asks, and it was long enough to push
  the rest of the line out of the card. What a Video would be watched at is in
  the corner of its picture and what it is made of is on its own page against
  every occurrence; the runtime was in neither. Progress is a bar along the
  bottom edge of the picture, so a shelf of part-watched Videos is read at a
  glance rather than by doing arithmetic against a runtime the card was not
  stating.
- An empty shelf offers the library. Favourites, Watch Later and Continue
  Watching explained what would put something on them without offering the one
  screen anything is put there from.
- Accounts waiting for a decision are listed first. The heading counts the
  requests waiting and the request itself sat wherever the list happened to put
  it, which on an installation with more than a handful of Accounts is past the
  fold.
- A verified prdb key is replaced deliberately rather than by default. The
  Installation screen kept a replacement field open under a connection that
  needed nothing done to it, which made the rarest action there — and the one
  that can cost the installation its identification — look like the thing the
  step was for.
- Scanning leads on the Background work screen while work is running, and
  resuming leads while it is paused. The two controls were drawn as the same
  kind of thing, so the everyday action and the one that stops every lane were
  indistinguishable.
- The controls a finger and a keyboard reach say so. Favourite, Watch Later,
  Dismiss and the rating stars were half the height a touch target needs on the
  screens most likely to be touched; an empty star was drawn at a quarter of the
  contrast of a filled one; and everything but the text fields relied on
  whatever the browser draws for focus over a dark surface, which is close to
  nothing.

### Fixed

- The decisions an identification review offers are drawn like the rest of the
  application. Five of them carried no class at all and arrived as the browser's
  own buttons — the only unstyled controls in the product — so the screen that
  ends a review said nothing about which of them ends it, and withdrawing
  knowledge the library had established looked exactly like accepting what prdb
  proposed. Accepting leads, revoking is coloured like what it does, and the
  rest are quiet.
- A runtime shorter than a minute is stated in seconds. Rounding to minutes
  printed "0 min" for a short clip, beside a Progress line counting the seconds
  it did have.
- Client Video Playability, an Account's state and a count of Video Files are
  written the way they are read rather than the way they are stored. "Ready For
  Direct Play" was the enum split on its capitals rather than the name the
  domain gives that state, "PendingApproval" was not split at all — on the one
  row an Administrator opens that screen to act on — and "1 Video File(s)" left
  the reader to do the agreement.
- A Video finished without a play being counted no longer states both as though
  they contradicted each other. A play is counted once enough of the Video was
  actually watched, and completion is reaching its end, so "Completed" beside
  "0 plays" was two different facts reading as one mistake. A count of none is
  left out, where it said nothing the state beside it had not already said.

## [0.6.1] - 2026-08-30

A dependency bump, and the certificate it made unnecessary. Nothing about the
running product changes.

### Changed

- The prdb SDK moves to 0.13.0, and the stand-in catalogue in `tools/` serves
  plain `http` because of it. The SDK now exempts loopback addresses from its
  https requirement — a request to `127.0.0.1` never leaves the machine, so there
  is no wire the API key could be read from — which removes the TLS certificate a
  server that only ever answers itself used to need, and the four lines of
  certificate handling that came with it. Every other host still requires https.

## [0.6.0] - 2026-08-30

Two defects the screens themselves caused, and the tools that found them. What
this release mostly adds is the ability to look: a local installation can now be
filled with the state a real one reaches, and the parts that talk to prdb and to
a browser are exercised as they actually ship.

### Fixed

- A prdb credential is reported as verified only when prdb actually answered. A
  200 carrying JSON with none of the documented fields — a proxy, a gateway, an
  endpoint that has moved — deserialises into an object with nothing in it, and
  that counted as proof. The Installation screen could therefore show a verified
  connection that had been checked against nothing, while identification went on
  failing for a reason the screen contradicted.
- Clicking one Site or Actor twice in quick succession turns it off again. It
  had been turning it on twice: the button decided between adding and removing
  from what it had last been drawn as, and inside a single batch that is a
  reading from before the first click. The address ended up naming that Site
  twice — and asking the server for it twice — while the button drew itself as
  unchosen. 0.5.3 fixed where such a write starts from; this fixes what it
  decides to write.

### Added

- Seed a local installation with `seed`, so that looking at a screen is cheap
  enough to be done before a release rather than after one. It writes real video
  files, claims the installation, registers one Account in each state the
  Accounts screen has a row for, and runs the lanes to a standstill twice — the
  second Scan being what leaves each derived lane with a run that had nothing to
  do, the state an installation sits in almost all of the time. It refuses to run
  against an installation that has already been claimed, on the same reasoning as
  Restore. `VIEWER_SEED_PRDB_KEY` gives it a credential to verify, so the
  identification lanes run through instead of waiting; it is read from the
  environment rather than taken as an argument, which would put it in the process
  list. Video files already under the library mount are left alone and scanned as
  they are, because prdb recognises content and nothing generated here is in its
  catalogue.
- A stand-in for prdb in `tools/Prdb.FakeCatalogue`, so the product can be run
  against a catalogue that answers on demand. The real service recognises
  content, so files made for a test come back unknown however good the credential
  is, which leaves the browsing screens with no Site, no Actor and no facet row
  to look at. It answers for the files the seed writes, and its replies are built
  from the same code the test suite's stand-in uses, because two imitations drift
  and drift here is quiet.
- `VIEWER_PRDB_BASE_URL` points the installation at a different prdb. It exists
  for exercising the product against a catalogue that answers on demand: the real
  service recognises content, so a library assembled for a test draws the same
  answer every time, and the failures that decide what an Administrator is told —
  a refusal, an outage, a rate limit — cannot be asked of it at all. The
  credential travels to whatever this names, and it defaults to the real service.
- `VIEWER_LIBRARY_MOUNT_ROOT` sets the root beneath which Library Directories may
  be staged. It defaults to the container's `/libraries`, which a working copy
  has no way to create, so those screens could not be reached outside a container.

## [0.5.3] - 2026-08-29

### Fixed

- Background work says what each lane got done, in words. The right-hand side of
  a lane row was a bare `completed/discovered` pair with no unit beside it, and
  the two kinds of lane did not mean the same thing by it: `0/0` on a lane that
  had nothing left to do was indistinguishable from a lane that had never run,
  and a Library Scan's denominator is only ever what it has found so far. A lane
  now reports what it found, how far through its admitted files it is, or that it
  had nothing to do. `Completed · Settled` said the same thing twice, so a
  settled run no longer repeats its phase.
- A Library Scan that finished in a single pass reported none of the files it had
  just recorded, and kept reporting that for good. The tally was taken before the
  pass rather than after it, so it always trailed by one, and a library small
  enough to be walked in one go never caught up.

## [0.5.2] - 2026-08-29

### Fixed

- Choose two Sites, or two Actors, as fast as you can click them. 0.5.1 claimed
  this and did not deliver it: reading the list out of the address inside the
  updater is not enough, because two navigations raised in the same tick both
  receive the address the first one started from. Every filter this screen writes
  now builds on what was last written while that write is still on its way, so a
  second change in the same tick continues the first rather than replacing it.

## [0.5.1] - 2026-08-29

### Fixed

- Attempted to make two quick Site or Actor choices both stick, and did not
  succeed. The change was real but insufficient, and the test covering it passed
  for the wrong reason. 0.5.2 fixes it.

## [0.5.0] - 2026-08-29

The six functional gaps the 0.4.0 UI review wrote down rather than closed. A
Library Directory can now be read and withdrawn rather than only added, a
disabled Account can come back, the library pages instead of widening, and one
thing being saved no longer stops the screen around it.

### Added

- Withdraw a Library Directory you no longer want read. It takes its Video Files
  out of the active library and keeps everything established about them —
  identity, path history, technical facts, identification and its provenance, and
  every Account's own viewing and organisation. A Video also backed by another
  Library Directory stays available. Any Scan of the withdrawn directory stops,
  and work queued before the removal cannot reach back across it.
- Read a configured Library Directory on the Installation screen: its state and
  health, how many Video Files are available beneath it, and what its last
  completed Library Scan found and when. An empty library is explained where an
  Operator looks for the explanation, rather than having to be inferred from two
  zeroes on another screen.
- Reinstate a disabled Account. Disabling was a one-way door: approval needs a
  request waiting for it and a disabled Account has none, so nothing could bring
  one back. It keeps everything it established, and signs in again once it is
  reinstated.
- Narrow the library by more than one Site or Actor at a time. Values inside one
  facet combine with OR, which is what the facets always looked like they did.

### Changed

- Reveal more of the library by asking for the next page rather than for a longer
  first one, so returning to a deep address no longer costs the whole depth on
  every refresh.
- Poll only when something can change. The library refreshes on a timer only
  while it has nothing to show — the one state where it waits for something
  arriving on its own — and Background work watches closely while a lane is
  running and loosely once everything has settled.
- One Video being saved no longer disables every other Video's controls, and one
  Account's decision no longer disables every other Account's. Show more is
  likewise no longer taken away by a background refresh.
- Say what a refused Account decision meant. "This is the only approved
  Administrator" and "that Account is no longer in the state this action applies
  to" replace a generic failure, and the Accounts screen says how somebody asks
  for access in the first place.

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
