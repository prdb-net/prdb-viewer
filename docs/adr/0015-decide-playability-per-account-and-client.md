# Decide playability per Account and client

A Video is admitted to Ordinary Discovery by its Client Video Playability, which
is derived for one Account on one client from three levels of evidence:

1. the installation-wide **Direct-Play Classification** of each Available Video
   File, from its inspected configuration alone;
2. the **Client Playback Assessment** that client made of that configuration; and
3. the **Observed Playback Outcome** of actually playing that file there.

Each later level overrides an optimistic earlier one within its narrower scope.
A file is ready when this client already played it, or when it has not been
ruled out and is either the conservative baseline or a Client-Dependent file
this client assessed positively.

Inspection retains the exact configuration a client can be asked about — profile,
level, bit depth, dimensions, frame rate, bitrate, audio layout — and derives a
**Profile Key** from it: the question the file puts to a browser. Files that
share the question share one answer, so a library of thousands asks a few dozen
questions. The client answers them with Media Capabilities where the inspected
facts determine a full RFC 6381 codec string, and with the coarser type support
test where they do not.

Both client-level facts are Personal State, scoped to the Account and the client
context that produced them, and both expire structurally rather than on a timer:
an assessment is keyed by the Profile Key, so a re-inspected file asks a new
question; an outcome is bound to the content hash it was observed about; and both
are keyed by a client context the client names for itself, so a different browser
or device is a different context.

Only a media failure is remembered about a file. Availability, delivery and
network failures are the library's or the installation's problem, they stop
variant fallback rather than driving it, and they never become "this browser
cannot play it".

## Why

The direct-play contract has always specified these three levels. The
implementation had one, and ticket 09 admitted the gap in the code that
approximated the other two: a Client-Dependent Video looked identical to every
browser, so the library both offered Videos a browser cannot play and hid Videos
it can.

Client evidence cannot be a projected column, because it is not a fact about the
Video. It belongs to one Account on one client, and two Accounts sharing a
browser must not see each other's. So the projection keeps what is installation-
wide and the query joins what is not.

The alternative — asking the client to qualify only what it can already see —
does not work: a configuration the client cannot play is precisely what keeps a
Video out of its results, so it would never be asked about it. The client is
therefore asked about the library's configurations rather than about its page.

## Consequences

- Deciding admission costs one indexed question per Video rather than reading a
  column: 69 ms for a page of a 20,000-Video library against the 6 ms of the
  approximation, most of it in the exact match count. The measurement and its
  breakdown are in [performance.md](../performance.md). Materialising the
  decision per Account and client would buy that back and cost a cache to
  invalidate on every assessment, outcome and re-inspection; it is not worth it
  at this size.
- The count of matches kept out is derived by subtraction rather than by a
  second full decision, which is what keeps the request at one pass.
- Baseline Candidate now means what the contract says: a conforming WebM with
  VP8 and Vorbis or no audio, at ordinary dimensions and frame rate. Ordinary
  H.264/AAC in MP4 became Client-Dependent, which is what it always was. An
  upgrade keeps its existing classifications and queues one Library Scan per
  Active Library Directory to inspect the facts the new rules need, so nothing
  vanishes from the library while that runs.
- A client that names no context gets the unqualified context, where nothing has
  been assessed and only Baseline Candidates are ready. That is honest rather
  than generous: without client evidence there is nothing to be generous with.
- The rule lives in the Core and the discovery query is its translation into
  something the database can answer. Two expressions of one rule can drift, so
  the discovery tests exercise the query against the same cases the rule tests
  cover.
