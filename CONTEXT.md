# prdb-viewer

`prdb-viewer` turns mounted home video directories into a shared, locally hosted library while keeping each person's viewing and organisational state private.

## Language

### People and access

**Account**:
A local identity secured by a username and password and subject to registration approval.
_Avoid_: prdb account, profile

**Registration Request**:
A request to create an Account that grants no application access until an Administrator approves it.
_Avoid_: invitation, registration

**Applicant**:
The person associated with a Registration Request that has not yet been approved to access the installation.
_Avoid_: guest, pending User

**User**:
An approved Account that may access the shared library and its own Personal State.
_Avoid_: viewer, member

**Administrator**:
A User with additional authority over installation-wide configuration, Accounts, and Shared Library Knowledge.
_Avoid_: owner, superuser

**Installation Operator**:
The person who operates the deployment, storage mounts, backup, and recovery outside the application; this is not an application role and need not be an Administrator.
_Avoid_: owner, Administrator

**Bootstrap Authorization**:
A single-use authority obtained by the Installation Operator that permits creation of the first Administrator on a fresh installation. It prevents an unauthorised first browser from claiming the installation and becomes invalid when the first Administrator is created.
_Avoid_: default administrator, first-browser claim

**prdb Connection Status**:
The Administrator-visible state of the installation credential and its most recent verification: Missing, Verification Pending, Verified, Rejected, or Degraded. A temporary service failure may degrade a previously verified connection but does not retroactively reject its credential.
_Avoid_: API availability, User authentication

### Information authority

**Shared Library Knowledge**:
Installation-wide facts about Videos and Video Files, including their metadata, provenance, local identifications, assignments, and corrections, that every User sees consistently.
_Avoid_: personal metadata, user metadata

**Installation State**:
Administrator-only information about Accounts, library configuration, background work, and external-service connectivity; stored secrets belong to this area but are never readable through the application.
_Avoid_: shared library data, Personal State

**Library Directory**:
A durable configured source of Video Files, selected by an Administrator from within the installation's documented video-mount area. Its container path may be changed explicitly without replacing the Library Directory, while its host location and mount configuration remain the Installation Operator's responsibility.
_Avoid_: host path, application data directory, library

**Library Directory State**:
The durable configuration state of a Library Directory: Active or Removed. A proposed addition or path change is staged until validation and explicit activation succeed.
_Avoid_: availability, scan status, health

**Library Directory Health**:
The latest reachability assessment of an Active Library Directory: Healthy, Partially Unreachable, or Unreachable. It never removes or disables the configuration by itself.
_Avoid_: Library Directory State, Video File Availability, scan result

**Library Scan**:
A bounded, durable reconciliation pass over one Library Directory. It commits trustworthy observations incrementally and provides eventual consistency rather than claiming a point-in-time filesystem snapshot.
_Avoid_: import, filesystem snapshot, indexing job

**Background Work**:
Durable, Administrator-visible processing that advances library discovery, technical understanding, derived artefacts, identification, enrichment, or regeneration without blocking ordinary use of the application.
_Avoid_: request handling, foreground task, undifferentiated job queue

**Background Work State**:
The observable lifecycle state of a bounded Background Work run: Queued, Running, Waiting, Paused, Completed, Completed with Issues, or Cancelled. Waiting always carries the condition needed to continue, while issues remain explicit rather than being collapsed into an unexplained failed state.
_Avoid_: Installation Configuration Status, Library Directory Health, Degraded

**Work Issue**:
An explicit outcome or obstacle attached to Background Work that records its affected scope, cause, impact, retry disposition, and required Administrator or Installation Operator action without rolling back unrelated successful work.
_Avoid_: generic error, container-log entry, Background Work State

**Work Issue Severity**:
The operational impact of a Work Issue: Scoped Issue when independent work may continue, Operational Blocker when a meaningful work area or systematic scope cannot advance, or Safety Stop when further writes could endanger durable state. Operational Blocker and Safety Stop establish Operational Attention.
_Avoid_: log level, Attention Required, content support classification

**Work Issue Cause**:
The stable cause class shared across Background Work categories: Source Access, Changing Source, Invalid Content, Capacity, External Availability, External Authority, Configuration, or Internal Consistency.
_Avoid_: work category, exception type, error code

**Remediation Owner**:
The single current party expected to advance a Work Issue: Automatic Recovery while an applicable retry remains scheduled, an Administrator for an application action, or the Installation Operator for deployment, mount, permission, storage, or host action.
_Avoid_: issue reporter, shared responsibility, Account owner

**Resolution Evidence**:
A new trustworthy observation that disproves a Work Issue's cause and is followed by successful continuation of the blocked work or proof that the work is no longer applicable.
_Avoid_: acknowledgement, dismissal, successful prerequisite check alone

**Operator Handoff**:
A copyable, secret-free diagnostic record that tells the Installation Operator which deployment, mount, permission, storage, or host condition must change and what Resolution Evidence the application expects afterward.
_Avoid_: container-log request, support bundle with secrets, Administrator action

**Operational Attention**:
The Administrator-visible condition that human action is required, a meaningful work area is blocked, or a systematic Work Issue exists. An isolated content-specific issue does not establish Operational Attention by itself.
_Avoid_: Installation Configuration Status Attention Required, Degraded, any work error

**Video File Candidate**:
A regular file admitted to technical inspection by the installation's recognised video-extension policy. It becomes a Video File only when inspection establishes that it is audiovisual content.
_Avoid_: Video File, arbitrary file, media file

**Configured Installation**:
An installation that has established an Administrator, a verified prdb connection, and at least one validated Library Directory whose initial processing has begun. This durable onboarding milestone describes completed configuration rather than current service or storage health.
_Avoid_: healthy installation, fully processed library

**Configuration Required**:
The current Installation State in which an Administrator must supply or replace required product configuration, such as a missing prdb credential or the absence of any Library Directory. It does not erase earlier onboarding milestones or reopen first-administrator bootstrap.
_Avoid_: unclaimed installation, degraded service

**Installation Configuration Status**:
The current setup state presented with this precedence: Unclaimed, Configuration Required, Configuration Pending, Attention Required, or Configured. Degraded is a separate current-health condition that may overlay an installation whose configuration milestone remains complete.
_Avoid_: background-work status, First Playable Video Milestone

**First Playable Video Milestone**:
The durable onboarding milestone reached when a Video first appears in the default library as Ready for Direct Play with the normal Play action. It does not require an Observed Playback Outcome and is not revoked by a later outage or availability change.
_Avoid_: completed scan, successful playback

**Personal State**:
A User's private viewing activity, progress, play counts, organisation, and preferences, accessible only through that User's Account.
_Avoid_: shared activity, administrator-visible history

**Recovery Code**:
A single-use credential valid for thirty minutes that lets its intended User replace a forgotten password without exposing the old or new password. An Administrator may issue one for a User, while the Installation Operator CLI may issue one only for an existing Administrator.
_Avoid_: temporary password, administrator password

**Backup Archive**:
A single portable, passphrase-protected file containing the installation's precious durable state while excluding source Video Files, ephemeral authorizations, and large regenerable artefacts. Its authenticated format and product version determine how Restore may accept or migrate it.
_Avoid_: application data directory copy, Video backup, unencrypted export

**Restore**:
The Installation Operator action that validates and migrates a Backup Archive into an empty unclaimed application state before atomically activating its Accounts, configuration, Shared Library Knowledge, and Personal State.
_Avoid_: import, merge, in-place rollback

### Library identity

**Video**:
A durable shared-library identity for one audiovisual work, independent of the technical representations or copies that currently carry it. Its identity persists when its identification is established or corrected.
_Avoid_: title, file, media item

**Video File**:
A single durably tracked occurrence of a Video on storage; its current path is a location rather than its identity.
_Avoid_: Video, path, variant

**Video File Availability**:
The observed storage state of a Video File: Available, Unreachable, Missing, Replaced, or Removed. Unreachable includes uncertain access or a first trustworthy absence; Missing requires two separate complete observations, Replaced requires stable evidence of different bytes at its former path, and Removed is deliberate.
_Avoid_: scan state, playability

**Video Availability**:
The state derived from a Video's Video Files: Available when any is available, Unavailable when none is available but the Video remains active, or Removed when every association has been deliberately removed from the active library.
_Avoid_: Video File Availability, playability

**Video File Quality**:
The resolution band one Video File would be named by — SD, 720p, 1080p, 1440p, 4K, 8K, or Unknown where inspection established no dimensions. A band names a picture the way a release is named rather than by its height alone, so a film with its bars cut off and a recording held upright are both the band they would be called.
_Avoid_: resolution, Playback Profile Key, bitrate

**Video Quality**:
The state derived from a Video's Video Files: the highest Video File Quality among those that are Available, and Unknown when none of them establishes one. It is installation-wide and is what the Library filters and orders by, unlike the occurrence a play action would reach for on one client, whose quality is what a screen shows.
_Avoid_: Video File Quality, Direct-Play Classification

### Identification and provenance

**Identification Claim**:
A provenance-bearing assertion about either a Video's work identity or its Site Recognition; its subject, source, evidence, and history remain explicit rather than being collapsed into one overall identification status.
_Avoid_: identification state, metadata

**Work Identification**:
An Identification Claim that associates a Video with a particular known audiovisual work. It is independent of Site Recognition.
_Avoid_: Site Recognition, Video identity

**Site Recognition**:
An Identification Claim that associates a Video with a particular originating site without necessarily identifying the audiovisual work. It is independent of Work Identification.
_Avoid_: Work Identification, site metadata

**Site Directory**:
The locally retained list of sites the installation knows, obtained from prdb and joined with every Site the installation has already established. It is the vocabulary local Site Recognition reads a Video File's path against, and it is regenerable rather than authoritative.
_Avoid_: site list, Site Recognition

**Identification Evidence**:
Evidence supporting an Identification Claim, classified as Conclusive, Suggestive, or Insufficient. Conclusive evidence may establish a claim automatically; Suggestive evidence may only produce a reviewable candidate; Insufficient evidence produces neither.
_Avoid_: confidence score, Identification Claim

**Identification Candidate**:
A reviewable proposed Identification Claim supported by Suggestive Identification Evidence. Its history is retained as Pending, Rejected, or Superseded without presenting it as established knowledge.
_Avoid_: match, established identification

**Identification Review Status**:
The state, Clear or Review Needed, that records whether an Identification Claim requires administrative attention because of a pending candidate or conflicting evidence. It remains independent of whether the current claim is Unknown or Established.
_Avoid_: Identification Claim, confidence

**Administrative Override**:
An Administrator's durable decision that establishes an Identification Claim and prevents conflicting automation from silently replacing it until the decision is explicitly revoked.
_Avoid_: automatic correction, permanent identification

**Unknown Video**:
A Video with no Established Work Identification. It may still have an Established Site Recognition, local file facts, Pending Identification Candidates, and an Identification Review Status of Review Needed.
_Avoid_: unidentified file, unprocessed Video, Video without metadata

### Actors

**Actor**:
A durable shared-library identity for one person prdb names as appearing in a Video, carried by prdb's own identifier and independent of the Videos that currently carry them. It is established only through an Established Work Identification; this installation never mints one of its own.
_Avoid_: performer, cast member, actor name

**Actor Credit**:
One Video's naming of one person: the name as that Video's retained metadata spells it, together with the Actor it resolves to where prdb sent an identity. It is what the Library facets and counts by, so a credit whose name resolves to nobody still narrows the Library and simply leads nowhere.
_Avoid_: Actor, cast, appearance

**Actor Profile**:
The last known prdb facts about an Actor — how they are described, their aliases, their external links, their bios and their pictures — retained so that an Actor's page reads through an outage or a rejected credential. Like the Site Directory it is regenerable rather than authoritative, and it never establishes, corrects, or disputes an Identification Claim.
_Avoid_: Actor, biography, Shared Library Knowledge

**Actor Image**:
One picture belonging to an Actor Profile, held in application storage and served under this installation's own address by a random identifier. A browser is never sent to prdb for one.
_Avoid_: preview, artwork, Actor Portrait

**Actor Portrait**:
The one Actor Image that stands for an Actor wherever they are named in a list. It is chosen from the Actor Profile's images rather than fetched separately, and an Actor whose pictures have not arrived has none.
_Avoid_: Actor Image, thumbnail, preview

### Direct playback

**Direct-Play Classification**:
The installation-wide assessment of a Video File as a Baseline Candidate, Client-Dependent, Unsupported, or Undetermined from its inspected media properties. It describes suitability for original-file browser playback, not availability, delivery health, or a guarantee of playback on a particular client.
_Avoid_: playable, compatibility

**Client Playback Assessment**:
The current client and device's assessment of whether a Video File is suitable for direct playback. It does not generalise to other clients and remains provisional until playback is attempted.
_Avoid_: Direct-Play Classification, universal support

**Playback Profile Key**:
The identity of one inspected media configuration, derived from the container, codec, profile, level, bit depth, resolution band, frame-rate band and audio layout. It is the question a Video File puts to a client: files that share it share one Client Playback Assessment, and a re-inspected file that no longer shares it asks a new question.
_Avoid_: codec string, Direct-Play Classification

**Client Context**:
The browser and device an Account is currently using, as that client names itself. Client Playback Assessments and Observed Playback Outcomes belong to one Client Context and stop applying when it materially changes.
_Avoid_: session, device identifier, Account

**Observed Playback Outcome**:
The success or failure actually observed when an Account in a particular client context attempts to play a Video File. It is account-and-client-scoped Personal State and is stronger evidence there than either the Direct-Play Classification or Client Playback Assessment.
_Avoid_: compatibility, support guarantee

### Playback activity

**Playback Attempt**:
One User-initiated effort to start a Video. Automatic fallback and manual switching among its Video Files remain part of the same Playback Attempt, while each Video File's technical outcome remains separately observable.
_Avoid_: Video File load, Viewing Session, play count

**Viewing Session**:
One coherent period of Active Watching for a User's Video. It begins only when Active Watching is confirmed; brief pauses, seeking, and Video File changes do not split it. Deliberate departure or a terminal failure ends it immediately, while thirty minutes without confirmed Active Watching ends it through inactivity. Its activity remains attributable to the participating Video Files even though Personal State is aggregated for the Video.
_Avoid_: Playback Attempt, browser session, uninterrupted Video File playback

**Active Watching**:
Confirmed time during which playback normally advances for a User. Pausing, buffering, seeking, an open but inactive player, and time without sufficiently recent playback evidence do not count.
_Avoid_: player-open time, elapsed session time, timeline coverage

**Playback Progress**:
The latest confirmed meaningful resume position for a User's Video, even when it is earlier than a position reached previously. Seeking alone does not establish it; playback must advance at the destination. A position transfers automatically between Video Files only when their timelines are known to be equivalent.
_Avoid_: furthest position reached, accumulated watch duration, completion

**Accumulated Watch Duration**:
The sum of confirmed Active Watching for a User's Video, including time spent rewatching the same portion. Concurrent Active Watching of the same Video by the same User counts only once.
_Avoid_: Video duration, elapsed session time, unique coverage

**Play Count**:
The number of a User's qualifying Viewing Sessions for a Video. A Viewing Session qualifies once after either sixty seconds or ten percent of the Video's duration of Active Watching, whichever comes first, with a ten-second minimum; a shorter Video qualifies when its end is confirmed. A Playback Attempt alone does not increment it, and completion is not required.
_Avoid_: Playback Attempt count, completion count

**Viewing Completion**:
The durable Personal State fact that Active Watching reached the natural end or the Completion End Zone of a Video. It does not require unique timeline coverage, seeking alone cannot establish it, and later replay does not erase its history.
_Avoid_: full timeline coverage, current play state, Playback Progress

**Completion End Zone**:
The final ten percent of a Video's established duration, capped at five minutes. Confirmed Active Watching in this zone establishes Viewing Completion; seeking into it without subsequent Active Watching does not.
_Avoid_: credits, fixed percentage, Playback Progress threshold

**Personal Play State**:
The current User-specific state of a Video: Unplayed, In Progress, or Completed. A qualifying incomplete Viewing Session establishes In Progress, Viewing Completion establishes Completed, and a later qualifying replay may establish In Progress again without erasing completion history.
_Avoid_: Viewing Completion history, Client Video Playability, Video Availability

**Timeline Equivalence**:
Established evidence that two Video Files carry the same sequence and timing closely enough to transfer Playback Progress at the same elapsed position. Similar duration or association with the same Video alone does not establish it.
_Avoid_: same Video, proportional progress guess, similar duration

**Client Video Playability**:
The state derived for a Video on a particular client: Ready for Direct Play, Compatibility Uncertain, or Not Directly Playable. It is derived from the Video's Available Video Files and remains separate from Video Availability.
_Avoid_: Video Availability, universal playability

**Unsupported Video**:
A Video whose Video Files are all statically classified as Unsupported. Rejection by one client does not make a Video an Unsupported Video.
_Avoid_: unavailable Video, not playable on this device

### Personal library

**Continue Watching**:
The User-owned, automatically derived surface of Videos whose current viewing cycle has a qualifying incomplete Viewing Session and a meaningful resume position outside the Completion End Zone. Viewing Completion removes a Video, an explicit dismissal suppresses it without deleting playback state, and later qualifying viewing makes it eligible again.
_Avoid_: viewing history, Watch Later, manually curated list

**Favourite**:
A User-owned, explicitly maintained reference to a Video that remains independent of playback activity, completion, availability, and playability.
_Avoid_: shared favourite, recommendation, rating

**Favourite Actor**:
A User-owned, explicitly maintained reference to an Actor. It is independent of the Videos that Actor has here, of playback activity, and of availability, and it is Personal State rather than Shared Library Knowledge. It is not a Personal Shelf, because it narrows nothing: it names a person rather than a set of Videos.
_Avoid_: Favourite, Personal Shelf, recommendation

**Watch Later**:
A User-owned, explicitly maintained queue of Video references ordered from oldest to newest addition. Playback and completion do not alter its membership.
_Avoid_: Continue Watching, playlist, viewing history

**Personal Rating**:
A User-owned optional score from one to five for a Video. Setting the same score is idempotent, changing it replaces the previous score, and clearing it removes only the score; it is independent of Favourite, Watch Later, and playback activity.
_Avoid_: Favourite, vote, shared rating

**Personal Shelf**:
One of the User-owned lists — Continue Watching, Favourites, Watch Later — taken as a way of narrowing the Library rather than a library of its own: a shelf admits the same search, facets, order and paging the Library does, keeps an order of its own, and shows what the User put there whether or not the current client can play it.
_Avoid_: personal library, playlist, saved filter

### Discovery

**Ordinary Discovery**:
The default Home, Library, and search presentation of Videos that are Available and Ready for Direct Play for the current Account and client. Personal references and explicit preferences or filters may expose exceptions without redefining this set.
_Avoid_: all indexed Videos, active library, default library

**Discovery Date**:
The installation-wide time at which technical inspection first admits a Video File Candidate as a Video, or at which a split creates a genuinely new Video identity. It is independent of playability and Personal State, and later enrichment, temporary loss, and recovery do not reset it.
_Avoid_: Ordinary Discovery eligibility time, first playback time, metadata update time, file modification time

**Direct Address**:
Reaching one Video by its own address rather than by finding it in a presentation. It does not apply the admission rule of Ordinary Discovery, because following a link is the User's own decision to look at a Video rather than the Library's decision to offer it; only what has left the active Library is refused, and an identity absorbed by a merge answers as the Video that survived it.
_Avoid_: Ordinary Discovery, search result, deep link to a Video File
