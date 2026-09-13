# Recommend from return interest rather than completion

Recommendations answer "what do I feel like watching?", and in this library the
honest answer is often something the User has already watched. **Return
Interest** — how much confirmed Active Watching a Video has drawn from one
Account, over how many separate Viewing Sessions, and what that Account said
about it explicitly — is the preference signal. Viewing Completion and the
fraction of a Video watched are not preference at all, and nothing is pushed
down for having been finished.

The first surface is a Recommendations page with three sections: **For you to
watch again**, **Long unseen** and **Not yet discovered**. Every section is
computed locally from the current Account's Personal State, states a short
factual reason, and offers the same four **Personal Reactions** the rest of the
application offers.

## Why

The obvious recommender treats a watched Video as spent and a completed one as
finished business. That is a video-shop model, and it is wrong here: a private
collection is largely rewatched, and a favourite returned to every month is the
clearest success this feature can have. A recommender that pushed such a Video
down would be systematically hiding the best answers it has.

Absolute Active Watching, not percentage, is what the evidence supports. A
minute of a four-minute clip and a minute of a two-hour film are the same
minute of interest, and the fraction they represent says more about the
Video's runtime than about the User. Duration is already available for other
purposes and is deliberately not consulted here.

The five-star Personal Rating goes. It asked for a judgement of quality when
what a recommendation needs is a direction, it was never usable as an input
without inventing a mapping from stars to intent, and a scale of five invites
the question of what a three means. Four reactions — dislike, shrug, like,
love — each say something a recommender can act on, and **Shrug** in particular
says "I saw it and I have no opinion", which is a different fact from having
said nothing.

## The preference rules

These are the agreed product semantics. The numbers that implement them are
initial tuning and live in the implementation tickets and in the Core policy
they produce, not here.

- **Confirmed Active Watching counts, including rewatched portions.** Seeking,
  pauses, buffering and an open but inactive player add no time.
- **Sixty seconds of Active Watching in one Viewing Session is mildly
  positive**, whether it arrived as one continuous minute or as six ten-second
  runs separated by seeks. Beyond two minutes the positive strength keeps
  growing with diminishing returns. A long **Uninterrupted Run** adds a little
  on top; seeking itself is neither rewarded nor punished.
- **Repeated meaningful visits are the strong signal.** Somebody who comes back
  three times has said more than somebody who watched once for an hour. The
  repeat bonus is capped, so a Video cannot climb without bound by being opened
  often.
- **A very short visit followed by a Deliberate Departure to another Video is
  weakly negative**, and only then. A technical failure, buffering, closing the
  application or simply losing the evidence is not a dislike. The penalty is
  bounded and can never erase established positive history or an explicit
  positive reaction.
- **Playlist membership is a moderate positive**, weaker than a like, because a
  playlist may be exploratory. It contributes once however many playlists hold
  the Video.
- **Like is strongly positive and Love is stronger.** Shrug contributes zero and
  suppresses nothing: later behaviour can still speak.
- **Dislike excludes.** A disliked Video appears in no recommendation section
  until the reaction is changed or cleared, whatever else its evidence says. It
  is not hidden from the Library, from search, or from the Account's own lists,
  and it says nothing about the Video's Actors or its Site.

## Variety and control

There is no fixed rewatch cooldown. Within one **Browsing Visit** a Video the
Account has just watched moves down somewhat so a page does not simply mirror
the last hour; a later visit removes that adjustment and a favourite may lead
again.

Discovery uses shared Actors and Sites and explicit Favourite Actors
cautiously and with normalisation, so that a prolific Actor or Site cannot
dominate by count alone. A reserved part of discovery is selected independently
of any inferred taste and deliberately includes Videos with sparse metadata,
because a collection's overlooked corners are exactly what a taste model will
never propose.

Every card carries the four reactions, and the page offers **Other
suggestions** and per-Video **Not today**. Not today is a Temporary Dismissal:
it excludes the Video from every recommendation section for twenty-four hours
without touching preference, and it can be undone. No Video appears twice on
one page. With little history the page shows discovery and says so, rather than
inventing a preference it cannot support.

## The playback evidence contract

The recommender needs six facts per Account and Video. Five of them the
application already holds; the sixth and the per-session detail are what
implementation must add. Nothing here is a general clickstream, and nothing
about a User's viewing leaves the installation.

| Fact the rules need | Where it already is | What must be added |
| --- | --- | --- |
| Active Watching per Viewing Session | `PlaybackAttemptRow.ActiveWatchDurationMilliseconds`, excluding seeks, pauses and stale reports already | — |
| Lifetime Active Watching for a Video | `PersonalVideoStateRow.AccumulatedWatchDurationMilliseconds`, a union of report intervals so concurrent reports count once | — |
| Number of meaningful visits | `PersonalVideoStateRow.PlayCount`, the count of qualifying Viewing Sessions | Qualification for Play Count keeps its own threshold; the recommender counts sessions by its own rule and never substitutes Play Count for it |
| Last confirmed watch | derivable from `PlaybackAttemptRow.LastActivityAt` | A summarised last-confirmed-watch moment on the Personal Video State, so Long unseen does not scan attempts |
| Longest Uninterrupted Run in a session | — | A per-attempt maximum, extended while consecutive reports remain contiguous and reset on seek, pause, buffering, file switch or stale evidence |
| Deliberate Departure after a short visit | — | An explicitly reported departure reason on ending a Playback Attempt, distinguishing navigation to another Video from technical failure, ordinary closure and unknown |

An **Uninterrupted Run** ends on a seek, a pause, buffering, a Video File
switch, or evidence too old to be contiguous. None of those breaks is negative:
the Viewing Session keeps its total across them, and only the run measurement
starts again.

Historical evidence is honest about being historical. An Account that watched a
Video before this contract existed has an Accumulated Watch Duration and a Play
Count, and those establish that prior watching happened — enough for Long
unseen and enough to keep a Video out of Not yet discovered. They are never
converted into invented per-session durations, uninterrupted runs or departure
reasons, and a Video whose only evidence is ambiguous is not claimed as never
watched.

## The Browsing Visit

A Browsing Visit is one Account and client context browsing continuously,
ending after thirty minutes without activity. It exists only to down-rank
within a visit and holds nothing but which Videos this visit has seen watched.
Two clients of the same Account browse in separate visits and do not affect one
another. It is not a navigation history, and it is deliberately a different
thing from a Viewing Session, which is per Video and made of confirmed Active
Watching.

## Consequences

- **Every legacy one-to-five Personal Rating is discarded**, with no mapping and
  no preservation. Upgrade drops the column, and a supported restore of an older
  Backup Archive drops the values it carries, so an old rating cannot reappear.
  This data loss is deliberate and approved: there are no production
  installations holding meaningful ratings, and inventing an equivalence between
  three stars and a shrug would be worse than the loss. All other Personal State
  survives untouched.
- Preference is Video-level, like the rest of Personal State, so the same Video
  watched across several encodes has one history. A merge combines the two
  Accounts' evidence the way the existing reconciliation does, a split
  redistributes what the participating Video Files can account for, and the
  Video-level facts no file can claim — reactions, playlist membership,
  dismissals — follow the Video an Administrator chose to carry them.
- Play Count, Viewing Completion, Personal Play State and Continue Watching keep
  their current definitions and thresholds. The recommender's thresholds are its
  own, and the two are not made to agree.
- Recommendations are Personal State. No other Account and no Administrator can
  read an Account's recommendation evidence, its reactions, its playlists or its
  dismissals, and computing a recommendation sends nothing outbound.
- Candidates satisfy Ordinary Discovery for the current Account and client, so a
  recommendation is something the reader can actually press play on.

## Worked examples

Each row is one Account and one Video. "Positive" and "weakly negative" are the
direction the rules give, not a score.

| Evidence | Session total | Longest run | Direction |
| --- | --- | --- | --- |
| Six ten-second runs separated by seeks | 60 s | 10 s | Positive, mild — the seeks cost nothing and add nothing |
| One continuous minute | 60 s | 60 s | Positive, mild, plus the uninterrupted-run addition |
| Twenty minutes with several seeks | 20 min | varies | Positive, well above a minute, with diminishing returns |
| Three separate visits of two minutes each | 3 × 120 s | — | Strongly positive: repeat visits, capped |
| Eight seconds, then navigation to another Video | 8 s | 8 s | Weakly negative, bounded |
| Eight seconds, then a decode failure | 8 s | 8 s | Neutral — a failure is not a dislike |
| Eight seconds after an earlier Love | 8 s | 8 s | Still positive: a short visit cannot erase an explicit reaction |
| Legacy state only: Play Count 4, accumulated 50 min, no retained sessions | unknown | unknown | Prior watching established; eligible for Long unseen, excluded from Not yet discovered, no invented sessions |
| Dislike, with hours of watching and a playlist membership | any | any | Excluded from every section |
| Completed twice, last watched three weeks ago | — | — | Positive; completion neither helps nor hurts, and age only orders Long unseen |
