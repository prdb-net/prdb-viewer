# Persist Background Work and run bounded resource lanes

Every bounded Background Work run, its current state, progress checkpoint, and
Work Issues are durable in SQLite. Durable state is the only authority for what
is `Queued`, `Running`, `Waiting`, `Paused`, `Completed`, `Completed with
Issues`, `Cancelled`, or due; process memory may accelerate notification but
never becomes a second queue or schedule that can disagree after restart.

Workers pull bounded slices of eligible work through named resource lanes.
The initial lanes separate Library Scan traversal, local derived work such as
technical inspection, content hashing and preview generation, and remote prdb
work. A long traversal, hash, preview, or delayed request therefore cannot hold
unrelated work behind it. Each lane has bounded concurrency fixed by the
application and yields between slices; resource profiles and user-adjustable
worker counts are not part of the MVP. Regeneration uses the lane for the
resource it consumes rather than creating another scheduling mechanism.

Playback and ordinary requests do not enter these lanes. Lane admission and
slice bounds must leave them responsive, and heavy local work must be
throttleable while direct playback is active.

## Consequences

- Starting, pausing, resuming, cancelling, retrying, or coalescing work changes
  durable state. A worker observes the same state that the administrative
  status surface reports.
- Periodic work records when it is next due. Work caused by durable facts is
  derived from those facts, so no separate per-item queue duplicates the truth
  unless accepting that item itself is a durable product promise.
- A restart recovers an unfinished run from its last safe boundary. An
  interrupted slice is neither completion nor a Work Issue and does not consume
  a failure retry; a deliberate cancellation remains `Cancelled`.
- Automatic retries, waiting conditions, and fairness are evaluated between
  bounded slices. No worker sleeps through backoff or a remote rate limit while
  retaining a lane.
- At most one Library Scan runs for a Library Directory, and another periodic
  or manual request coalesces into one durable follow-up as specified by the
  Library Scan lifecycle.
- Work from a superseded Library Directory configuration generation may finish
  reading but cannot commit results into the current generation.
- Logs remain diagnostic evidence. They are never the authority for Background
  Work State, Work Issues, retries, or remediation.
