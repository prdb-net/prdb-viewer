# Recognise sites locally from a retained Site Directory

Local Site Recognition reads a Video File's own path against a Site Directory:
the list of sites prdb publishes, retained locally and refreshed at most once a
day, joined with every Site this installation has already established for
itself.

The match is a deterministic mapping, not a similarity. A site's aliases are its
title as words, that title written as one word, and the distinctive label of its
web address, all normalised the way search normalises. An alias matches only as
whole words of the path — its directories and its file name — with the longest
alias at a position winning, so `Harbour Nights` names that site rather than
ambiguously naming it and `Harbour`. Nothing looks inside a word, except that a
single word may carry trailing digits, as a dated release name writes them.

A path that names exactly one site through an alias of at least five characters
is Conclusive evidence and may establish an Unknown Site Recognition. A path
that names several sites, or names one only through a shorter word, is
Suggestive and can only propose an Identification Candidate. Every resulting
claim carries `LocalInference` as its source, and every surface that shows a Site
shows where it came from.

A locally established Site gives way, without review, to the canonical Site of a
work prdb later identifies. Nothing else about it yields: an Administrative
Override is never replaced, and two remote results that disagree still require
review.

## Why

`VISION.md` asks for site recognition for files that receive no full prdb match,
with visible provenance and no guess presented as fact. The identification rules
allow it: deterministic local evidence that maps uniquely to a site is
Conclusive, while names, paths, and heuristics are Suggestive.

The vocabulary has to come from somewhere. Deriving it only from Sites the
library has already established would recognise nothing in a library prdb never
matched, which is the case the feature exists for. `GET /sites` answers that in
one cheap request per day, and using prdb's own site identities means a locally
recognised site and a prdb-established one are the same site rather than two
spellings of one.

Substring matching would be the obvious shortcut and is the wrong one: it makes
`midnightowl` name `Night Owl`. Matching whole words makes "maps uniquely" a
property of the rule rather than a hope.

Without the automatic supersession above, a stale local reading would block the
identified work's canonical Site: the conflicting remote result would wait for a
review decision that the rules do not offer, because a site conflict with an
identified prdb work is corrected upstream rather than locally.

## Consequences

- Recognition runs as its own durable lane after identification, so it applies
  to files the remote ladder has already answered about and keeps working while
  prdb is unreachable or its credential is gone. Only its daily vocabulary
  refresh needs the network.
- The Site Directory is a regenerable copy. It is excluded from the Backup
  Archive, and a Restore clears the last-fetched time so the target installation
  fetches its own.
- A run re-reads the paths of Videos whose Site is still Unknown, so a directory
  that arrives later reaches files that were read before it did. A file whose
  path is unchanged and whose Site is Established is never read twice.
- An installation that has never fetched the directory recognises nothing. That
  is reported once, as a Scoped Issue against the prdb site directory, rather
  than per file — a stale directory that still recognises sites is not an
  obstacle and is not reported as one.
- The five-character threshold is a judgement, not a fact. A site whose name is
  shorter is proposed rather than established, which costs an Administrator a
  decision and never presents a guess as knowledge.
