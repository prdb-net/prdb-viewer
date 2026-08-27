# Vision

`prdb-viewer` gives people a comfortable, tube-like way to browse and play the
videos they keep at home. It turns mounted directories into a visual library,
enriches that library with metadata from prdb.net, and remains useful for files
that prdb does not know.

It is an open-source, self-hosted web application for a trusted local network.
It serves the videos already on the user's storage; it does not acquire them,
move them into a new library, or transcode them during playback. Multiple
people can use one installation, while every person's viewing activity and
ways of organising the library remain their own.

The application is independent of `prdb-fab`. The two may be useful together,
but neither is a prerequisite for the other and they share no database or
runtime contract. A directory built by `prdb-fab` is just one possible input to
`prdb-viewer`.

## The problem

A large local video collection is easy to store and surprisingly hard to use.
File browsers expose filenames and folders, not the videos, sites and actors a
person is looking for. Generic media servers can play the files, but their
metadata models and browsing experience are not built around this material.
Over time the collection becomes something its owner has but can no longer
comfortably explore.

prdb.net can identify many of those files and provide the title, site, actors
and artwork that make them browsable. It cannot be the only answer, however. A
personal library will always contain files that have not been catalogued, are
named poorly, or cannot be identified with enough confidence. Those files must
not disappear merely because a remote catalogue has no entry for them.

The missing product is a local viewer that treats both groups well: rich prdb
metadata where a reliable identification exists, and useful thumbnails and
local organisation where it does not.

## Why not just Jellyfin?

Jellyfin and similar media servers solve a broad playback problem: many kinds
of media, many clients and, where necessary, live transcoding. `prdb-viewer`
solves a narrower discovery problem for this particular kind of video library.
Its promise is not a more capable playback engine, but a much better answer to
"what do I want to watch from the collection I already have?"

Three things create that difference: prdb-native identification and metadata;
useful previews and local recognition when prdb has no answer; and personal
ways to resume, organise and rediscover a large library. General-purpose folder
and media views do not become this experience merely by displaying cover art.

The products can coexist, but `prdb-viewer` does not require Jellyfin and does
not grow towards Jellyfin's transcoding and client ecosystem. It stays focused
on direct browser playback, domain-specific browsing and personal discovery.

## Who it is for

The primary user is comfortable running a self-hosted application with Docker
Compose and mounting one or more existing video directories into it. They
usually run the installation for themselves and want to use current desktop,
tablet and phone browsers in their local network rather than install a
dedicated client everywhere.

One installation may also serve another person or a small, trusted group. This
is a multi-user product from the beginning so that sharing does not require a
later redesign, but it is not a multi-tenant service. Every approved user sees
the complete shared library and uses the same installation-wide prdb
connection; per-directory and per-video access rules are deliberately out of
scope. Accounts, viewing history, playback progress, favourites, votes,
playlists and recommendations remain per user.

The installation owner needs an active prdb.net subscription and API key. That
subscription is separate from this application: `prdb-viewer` itself is open
source and distributed under the MIT License.

## The experience

The home screen should feel closer to a good tube site than to a filesystem.
Videos are presented as fast, thumbnail-led grids and rows. Search and filters
make a large library approachable by title, site, actor, play state and other
useful facets. A Video page explains what the Video is, provides its available
metadata and file variants, and starts playback without sending the user through
a separate media server.

The application remembers what each user watches, how often they return to it,
and how long they watch. That history is useful immediately for resuming and
revisiting videos, and later becomes the basis for local recommendations. The
goal is not merely to answer "what files do I have?", but also "what do I feel
like watching?", "what have I forgotten about?", and "how do I make sense of
this collection in my own way?"

The user will gain several overlapping ways to organise and rediscover the
library: favourites, watch-later lists, playlists, voting, saved filters and
recommendations. No single taxonomy will fit every large collection, so the
product should provide building blocks instead of imposing one perfect folder
structure.

## The library

The application scans explicitly configured, mounted directories and records
the video files it finds in its local database. Its central product unit is the
**Video**, not the file: grids, search results, favourites, watch-later entries,
playback progress and recommendations refer to a Video. A **Video File** is one
technical representation that carries that Video on disk.

One Video may be backed by several Video Files, for example different encodes
or copies found in different mounted directories. Those files do not create
duplicate cards throughout the interface. The Video page can expose the
available variants and their technical facts, and playback chooses a compatible
file while retaining the ability to explain that choice. Personal viewing state
belongs to the Video rather than being fragmented between its files.

An unidentified file initially represents its own local Video because there is
no evidence that it belongs with another one. A later prdb identification,
local identification or user assignment may associate files with the same
Video. That association must not erase file-level facts such as path, container,
codec, playability or scan state.

Scans are repeatable and incremental. A Video that cannot be identified is
still a library item, and a temporarily unavailable prdb.net API does not make
already indexed local Videos unbrowseable or unplayable.

Source video files belong to the user. Scanning and viewing do not rename,
move, replace or delete them. The database and generated artefacts such as
thumbnails live in the application's own data directory and can be rebuilt from
the mounted library where possible.

Every Video carries the provenance of what the application knows about it:

- A **prdb identification** is metadata returned by the public prdb.net API for
  this file.
- A **local identification** is an inference made from local evidence such as
  the path, filename or similarities between files.
- A **user assignment** is a decision made by a person using the installation.
- An **unknown video** has not been identified beyond the facts available from
  its Video Files.

These states are not interchangeable. A local inference must never be presented
as a confirmed prdb match, and an uncertain result must never silently acquire
the identity of its best candidate. The UI should show what is known, how it was
known, and offer a review path when human judgement is needed.

## Working with prdb.net

prdb.net is the authoritative remote source for known videos, sites, actors and
their artwork. Its documented public API is the only integration point: no
scraping, private endpoints, database access or copied metadata corpus. If the
public API lacks something the viewer needs, that gap should be addressed in
the API and its SDK rather than bypassed here.

The API is accessed through the public `Prdb.Sdk` package, not through a
hand-written client. File hashes are produced by `Prdb.Hashing`, because an
`osHash` or `pHash` must be computed exactly the same way everywhere to be
useful. The viewer asks the API to identify files; it does not mirror prdb's
hash database locally.

The prdb API key is an installation credential, not a local user's password.
Metadata obtained with it belongs to the shared library, while viewing activity
and personal organisation remain local and per user. The key, rate limits and
remote availability are visible operational concerns, but a remote outage must
not unnecessarily interrupt playback of files and metadata already available
locally.

The viewer does not rely on prdb for every file. It generates its own preview
images and retains basic file metadata for unknown videos. In the MVP it also
tries to determine the site from local evidence. Actor recognition and deeper
matching for videos without a prdb identification belong to the roadmap.

An expired subscription, rejected key or sustained API outage is a visible
degraded state, not a lockout. New identifications and metadata updates stop,
and the administrator is told why, but local accounts, browsing, personal
state, existing metadata and direct playback continue to work. Replacing the
installation key must not silently discard established Video identities or
personal data. Any account-specific prdb state introduced later must define its
own migration behaviour before key replacement can affect it.

## Direct playback, deliberately without transcoding

`prdb-viewer` streams the original file to the browser. It supports seeking and
the other HTTP behaviour needed for direct playback, but it does not transcode,
remux or convert codecs during playback. Rebuilding Jellyfin's media pipeline
is outside the product boundary.

Only Videos with at least one Video File in a container and codecs suitable for
direct playback on the supported browsers are shown in the default library
view. Compatibility is a property of a Video File and the playback client, so
the application should describe its decision rather than promise that every
browser can play every file.

A Video is unsupported when none of its Video Files can be played directly.
Unsupported Videos are not ignored: they remain indexed, retain their prdb or
local metadata, and are marked as not directly playable. By default they are
hidden from browsing so that selecting a visible result leads to playback. A
per-user preference can include them, either as normal entries or as
title-and-preview entries that clearly explain why playback is unavailable.

This boundary keeps installation and operation small and predictable. Users who
need live transcoding should use a media server designed for it; this product's
value lies in identification, exploration, personal organisation and learning
from viewing behaviour.

The supported clients are current stable versions of Chrome, Firefox and Safari
on desktop, tablet and phone. The interface is responsive and browser-first.
Native mobile applications, television applications and television browsers are
not supported clients. The frontend and backend remain clearly separated so a
dedicated client could be built later without replacing the domain and playback
backend, but no such application is part of the current product promise.

## Users and authentication

There is no anonymous catalogue or account surface. Apart from sign-in,
registration and the delivery of video and preview data, the web UI and its
application APIs require an authenticated local account. The first account
created during onboarding is the administrator.

Local authentication uses a username and password. An email address is
optional and is not required to create or recover an account in the MVP. People
may submit registration requests, but a new account cannot sign in until an
administrator explicitly approves it. Invitations, email delivery, email
verification and two-factor authentication are not part of the MVP.

Authentication protects access to the application, its catalogue and each
user's viewing behaviour. An ordinary user cannot retrieve another user's
history, progress or personal organisation through the UI or application API,
and those data are not anonymously readable on the local network.

Video streams and preview images do not need authentication. Their URLs are not
an access-control boundary, and possession of such a URL may be sufficient to
retrieve the content. Protecting those URLs, mounted storage, direct media URLs
exposed by another server, and the local network itself remains the deployer's
responsibility. The application is designed for a private network and must not
be presented as an internet-facing streaming platform.

Administrative responsibilities are installation-wide: configuring the prdb
credential, managing library directories, running or observing scans, and
approving or disabling users. Ordinary users can browse the shared library and
manage only their own viewing and organisational data.

All users see the same Videos and shared metadata. A manual assignment or
correction changes the shared Video rather than creating a private version of
it; exactly which role may make such changes is a permission detail to settle
before the review workflow is built.

## Deployment and setup

Docker Compose is the supported way to run `prdb-viewer`. The application is
delivered as a container with a persistent data directory and any number of
explicitly mounted video directories. A normal installation should not require
the user to assemble an application server, frontend and database by hand.

The Compose configuration contains only what the container needs before it can
start: image version, port, persistent data mount, video mounts and the user and
group identity needed to read them. Product configuration such as the prdb API
key and account setup belongs in guided browser onboarding rather than in a
growing collection of environment variables. Documentation must make ownership,
permissions and read-only mounts understandable to someone deploying on a home
server.

Adding a library directory has two parts. The directory is first made available
to the container as a volume in the Compose configuration; the corresponding
container path is then selected in the application's administrative UI. The UI
cannot grant the container access to a host path that Docker did not mount, and
it should explain that boundary clearly instead of accepting an unusable path.

Home libraries frequently live on NAS systems or network shares. The supported
MVP pattern is to mount the SMB, NFS or other share on the Docker host and then
bind-mount that local host path into the container. In other words, every MVP
library appears to the container as a local filesystem path, regardless of
where its storage physically lives. The application does not initially mount
network shares itself or store NAS credentials. Common NAS permission,
reconnection and availability behaviour should still be documented and handled
gracefully enough that a temporarily missing share does not look like an empty
library that should be discarded.

Setup should take the user from a working Compose file to a browsable first
library through a short guided path: create the administrator, verify the prdb
API key, select a mounted directory, scan it and explain any permission or
playability problems in actionable language.

## Background work and operational visibility

Scanning directories, inspecting codecs, hashing files, generating previews and
retrieving metadata are background work. A large library may take hours to
process, but that must not turn onboarding into an hours-long loading screen or
make playback sluggish. Videos appear as they become usable while the remaining
work continues with bounded use of CPU, storage and remote API capacity.

Background work is durable and resumable. Restarting or upgrading the container
does not restart a complete library scan, lose completed work or leave an
operation in an unexplained state. Repeating work is safe, and one broken file
does not stop the rest of a directory from being processed.

An administrative status view shows what the installation is doing and whether
anything needs attention. It reports progress and recent outcomes for scans,
file inspection, hashing, preview generation and prdb synchronization. Errors
identify the affected directory or file and provide an actionable reason, such
as a missing mount, insufficient permissions, an unsupported format or a
rejected API key. Recoverable failures can be retried without recreating the
installation.

A temporarily unavailable directory or network share is shown as unavailable.
Its Videos are not interpreted as deleted merely because one scan could not
reach the mount. Removing library records is a separate, deliberate operation
from detecting temporary absence.

## Backup and restore

The application provides a command-line backup and restore operation that can
be run through the container, including through `docker compose exec`. Backup
produces one portable file that can be copied away from the server and supplied
to a fresh installation. Restore must not depend on the original container
still being healthy enough to serve its web UI.

The backup contains the important state that cannot simply be regenerated:
configuration, account and approval state, password hashes, personal
preferences and organisation, viewing history and progress, local assignments,
and the credentials required to reconnect the installation. It does not contain
video files, generated previews or other large caches that can be rebuilt by
scanning and synchronizing again. Whether the library index itself is restored
or rebuilt is an implementation decision, but neither choice may lose local
assignments or personal state.

Credentials and other secrets must not be stored in plaintext inside the backup
file. The non-secret application data does not necessarily need encryption; the
exact envelope and passphrase mechanism can be decided with the backup format.
The command avoids printing secret values and creates its output with
appropriately restrictive permissions.

The same container CLI provides recovery for a forgotten administrator
password, without depending on email delivery or an existing authenticated web
session. Restore validates the backup before changing the installation and
supports the defined migration path from older backup versions. Backup, restore
and administrator recovery are part of the product contract from the first
release rather than tools improvised after data has been lost.

## What success looks like

`prdb-viewer` succeeds when a large directory tree becomes a library people
actually browse rather than a collection they only know is on disk:

- A normal user can go from a running Compose file to the first playable Video
  within minutes, without understanding hashes or editing application data.
- The first useful Videos appear while the initial scan continues in the
  background; onboarding never waits for the entire library to finish.
- Libraries containing tens of thousands of Video Files remain responsive to
  browse, search, filter and playback actions.
- Unknown and unsupported Videos remain understandable and recoverable rather
  than becoming invisible failures.
- Routine restarts, temporary NAS outages and temporary prdb.net failures do
  not require rebuilding the installation or repairing its database by hand.
- The status view answers both "is it still working?" and "what needs my
  attention?" without requiring container logs for ordinary problems.
- Continue Watching, favourites and Watch Later give users an immediate reason
  to return even before personalised recommendations exist.
- Day-to-day browsing and playback require no administrative maintenance once
  directories and the prdb connection are configured.

## The MVP

The first useful release proves the complete local viewing loop without trying
to solve every kind of recognition or organisation. It includes:

1. A supported Docker Compose deployment with persistent application data and
   one or more video directories mounted from the Docker host.
2. Guided onboarding that creates the administrator, verifies a valid prdb.net
   API key and configures at least one directory already exposed to the
   container.
3. Local username-and-password accounts, registration requests and explicit
   administrator approval, with the complete library visible to every approved
   user.
4. Repeatable scanning of existing video directories without modifying the
   source files.
5. Durable background processing and an administrative status view covering
   scanning, technical inspection, hashing, preview generation and prdb
   synchronization.
6. Technical inspection of every discovered file and a clear direct-play or
   unsupported classification.
7. File identification through the public prdb.net API, using `Prdb.Hashing`
   and `Prdb.Sdk`, with prdb metadata and artwork stored locally as needed for a
   responsive library.
8. Locally generated preview images for both identified and unidentified files.
9. Local site recognition for files that do not receive a full prdb match, with
   visible provenance and no guess presented as fact.
10. An authenticated, thumbnail-first library of Videos with direct playback,
    search and the core filters needed to browse by prdb match, site and
    playability.
11. Per-user playback progress, watch duration and play-count tracking, so the
   product does not lose the history that later recommendations require.
12. Per-user Continue Watching, favourites, Watch Later and Personal Rating
    surfaces.
13. A preference and filter for showing unsupported files, including their
    titles and previews when available.
14. Command-line operations that create and restore a protected, portable
    backup and recover access when the administrator password is lost.

The emphasis is reliable prdb matching, useful previews, site recognition and a
fast tube-like browsing experience. A narrow feature that works across a large
library matters more than an early catalogue of unfinished social or
recommendation features.

## After the MVP

The product grows by making unknown files easier to understand and known files
easier to rediscover:

- richer matching for files prdb cannot currently identify, including actor
  recognition, perceptual similarity and efficient human review;
- personal recommendations derived from local viewing behaviour, with reasons
  the user can understand and controls that prevent a narrow feedback loop;
- playlists, voting, saved filters and other user-defined ways to organise the
  shared library;
- better resurfacing of unfinished, frequently watched, long-unseen and newly
  added videos;
- improved explanations and diagnostics for browser compatibility, scanning,
  thumbnail generation and prdb synchronization;
- broader NAS integration where it can simplify deployment without turning the
  application into a general-purpose network filesystem manager;
- an explicit, one-time conversion workflow for an unsupported Video File using
  ffmpeg. This is not live transcoding: the user starts a durable background
  conversion, reviews the successful playable result, and separately authorises
  deletion of the original. The original is never deleted after a failed or
  unverified conversion.

None of these turns runtime playback into a transcoding pipeline or the
installation into a hosted service. A future one-time conversion is library
maintenance performed before playback, not part of serving a Video.

## Principles

**Unknown is a valid state.** A useful local item is better than a false match
or a video hidden because no remote record exists.

**Evidence stays visible.** Confirmed prdb data, local inference and user input
carry different authority. The product records and communicates that
difference.

**The library remains local.** Remote metadata enriches local files; it does not
become a condition for playing them every time.

**Personalisation is personal.** A shared library does not imply shared watch
history, preferences or recommendations.

**Direct play is the scope, not a missing feature.** Unsupported files are
catalogued honestly, not converted behind the scenes and not silently omitted
from the database.

**Source files are irreplaceable.** Viewing the library must not reorganise or
delete it. Generated data goes elsewhere and should be rebuildable. A future
conversion workflow may replace an original only after it has produced and
verified a playable result and the user explicitly approves deletion.

**Authentication is mandatory for the application.** There is no default
credential and no anonymous catalogue browsing. Registration grants no access
until an administrator approves it. Video and preview delivery remain outside
that boundary by design.

**Local network means local network.** Security is appropriate for an
authenticated home application, not marketed as protection for a public video
service.

**The public API is the contract.** `prdb-viewer` depends on prdb.net, not on
another prdb application, deployment or private implementation detail.

**Setup should stay small.** Docker Compose supplies storage, identity and a
port; guided onboarding handles product configuration, and a single portable
backup file protects the state that matters.

**Long work explains itself.** Scanning and enrichment continue in the
background, survive restarts and expose useful progress and failures without
making the user read container logs.

## What it is not

- Not a replacement for Jellyfin, Plex or another transcoding media server.
- Not a downloader, indexer, library filer or companion process required by
  `prdb-fab`.
- Not an online platform, public tube site, hosted service or multi-tenant
  product.
- Not a content access-control system. Video and preview URLs are deliberately
  not protected by the application's login.
- Not a parental-control or per-user library-visibility system. Every approved
  user sees the same library.
- Not a native mobile or television application, and not a promise of support
  for television browsers.
- Not a mirror of the prdb catalogue or hash database.
- Not limited to files already known by prdb.
- Not a global metadata editor; local corrections organise this installation,
  while changes to prdb data belong upstream.

## Prerequisites

- An active prdb.net subscription and API key.
- A host capable of running the application and retaining its local database
  and generated previews.
- Docker with Docker Compose.
- One or more video directories mounted into the container, read-only wherever
  practical. In the MVP, network storage is mounted on the Docker host first.
- Modern browser clients on a trusted local network.
- Video files encoded in a directly playable format for playback; other video
  files can still be indexed and shown on request.

## Open questions

- Which exact container and codec combinations form the supported direct-play
  baseline across current Chrome, Firefox and Safari, and how client-specific
  capability checks refine it.
- Which local evidence is strong enough to assign a site automatically and
  which results must wait for review.
- Which roles may create or change the local assignments that are shared across
  the installation.
- Which personal organisation features make the smallest coherent set after the
  MVP.
- How much viewing-event detail is necessary for useful recommendations without
  collecting events that do not improve the result.
- Which NAS platforms and Compose volume patterns need first-class examples
  beyond the host-mounted SMB and NFS approach supported by the MVP.
- How the backup file protects secrets while keeping restore simple and
  portable.
