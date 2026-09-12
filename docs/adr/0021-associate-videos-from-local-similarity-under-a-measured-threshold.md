# Associate Videos from local perceptual similarity, under a measured threshold

Two Video Files of this installation whose Perceptual Hashes lie within a
Hamming distance of **6 of 64 bits**, and whose durations agree to within
**0.25 % of the longer running time**, may be associated without review: their
Videos are merged, and an identification established for one then covers the
other. Anything that does not clear both conditions becomes an Identification
Candidate, which is where a local similarity ordinarily ends.

An association is not an Identification Claim. It names no work and no site —
it says only that two Videos carry the same content, so a Video that exists
because an association merged two of them is still an Unknown Video until
something identifies it. Its source is `LocalInference`, and its provenance
carries the measured distance and both durations, so a person reading the
library can see what the installation concluded and from what.

Identification decisions stay Administrator-only. Nothing here changes who may
accept or reject a candidate, revoke an Administrative Override or split a
Video.

## Why

`VISION.md` asks for richer matching for files prdb cannot identify, and the
Perceptual Hash is already computed and retained per Video File as a rung the
remote catalogue is asked on. Comparing two of this installation's own files
against each other is the one use of that value that needs nothing from the
network — it works for exactly the files prdb had no answer about.

**Why a distance alone is not enough.** The hash describes not a picture but a
5×5 montage of 25 frames sampled at `0.05·d + i·(0.9·d/25)`, so the entire
sample grid is a function of the duration. Two files of one work with the same
running time sample the same moments; two files whose durations differ by *p* %
sample moments that drift apart by up to *p* % of the running time, and so
describe different material. Duration is therefore not a second opinion bolted
on beside the distance — it is the condition under which a small distance means
what it appears to mean. A small distance between files whose durations
disagree cannot have come from them showing the same moments, because at
different durations they do not; it comes from the picture carrying too little
for 64 bits to describe, which is precisely the case an automatic merge must
not act on.

That is also the answer to the obvious alternative. A second sampling pass
costs another 25 decodes per file and asks the question the first pass already
answered, while the duration is known already, costs nothing, and is the
variable the hash's own sample grid is built on.

## The numbers, and what they were measured on

Both were measured rather than assumed, with `Prdb.Hashing` 0.1.0 — the library
the product hashes with — against ffmpeg 8.0.1. Frame extraction is the one
step of the method that depends on the ffmpeg build, so the version is part of
the result. Three corpora:

- **Real film.** Twelve non-overlapping excerpts of four freely licensed
  films (*Sintel*, *Big Buck Bunny*, *Elephants Dream*, *Sita Sings the
  Blues*), each in six encodes: H.264 at 720p and 480p, H.265 and VP9 at 360p,
  a regrade, and a letterboxed 2.35:1 version. Two excerpts of one film are
  different works that share a camera, a palette and a grade — the hardest
  honest false-positive case.
- **Synthetic.** Twelve generated works in the same six encodes, chosen for the
  material 64 bits struggle with: near-black grain, a static vignette,
  low-contrast grey, title cards, heavy noise.
- **Collisions.** Thirty unrelated single-encode files, all dark, static,
  low-contrast or near-empty, with running times drawn from a three-second band
  so that many pairs agree on duration by accident.

That gives 180 same-work pairs and 2 376 different-work pairs per encode
corpus, and 435 further different-work pairs of deliberately degenerate
material.

| | same work | different works |
|---|---|---|
| real film | median 0, **max 6** (116 of 180 bit-identical) | **min 22**, median 30 |
| synthetic | median 2, max 34 | **min 18**, median 32 |
| collisions | — | **min 0**, median 32 |

**The distance is 6 because 6 is where the same-work distances end.** On real
film no pair of encodes of one work sat further than 6 apart, and no pair of
different works came closer than 22. The synthetic corpus does not move that
upper bound even though its same-work distances run to 34: every one of those
outliers is degenerate material the hash cannot describe at all (below), so
raising the threshold to catch them would buy nothing and spend the margin.
Its closest unrelated pair is 18.

**8 was tried and is wrong here**, which is worth recording because 8 of 64 is
the value Stash uses and `PerceptualHashDistance.DefaultThreshold` offers, and
it is what a future reader will propose. Across all 5 187 different-work pairs,
the combined rule admits:

| | dur ≤ 0.10 % | dur ≤ 0.25 % | dur ≤ 0.50 % |
|---|---|---|---|
| d ≤ 4 | 0 false merges, 5 true pairs missed | 0, 5 missed | 2, 5 missed |
| **d ≤ 6** | **0, none missed** | **0, none missed** | 3, none missed |
| d ≤ 8 | 1, none missed | 1, none missed | 5, none missed |

At 8 one pair of unrelated near-empty files — two different dark pictures whose
durations happened to be identical — clears the rule and would be merged. At 6
nothing does, and nothing true is lost: 6 still catches all 180 same-work pairs
of the film corpus.

**The tolerance is 0.25 % because that is the largest duration difference the
distance budget survives.** Shortening real film at the end by a fraction of
its running time, and changing nothing else, moves the hash by:

| shortened by | 0.10 % | 0.25 % | 0.50 % | 0.75 % | 1.00 % | 1.50 % |
|---|---|---|---|---|---|---|
| worst distance | 6 | 6 | 8 | 12 | 14 | 22 |

At 0.25 % the shift alone costs at most 6 bits — the whole threshold, and no
more. At 0.5 % it costs 8, so a tolerance there would admit pairs whose
similarity the shift has already made meaningless. The two numbers are
therefore one number twice: the tolerance is the point at which a duration
difference stops fitting inside the distance it is allowed to consume.

There is no absolute floor beside the percentage. 0.25 % of a ten-minute work
is a second and a half, and of a one-minute work about four frames at 25 fps,
which is more than container and frame rounding need.

## What this costs, and what it cannot do

**One common case is refused on purpose:** two encodes of one work where one is
a few seconds shorter — a missing end card, a trimmed intro. On real film a 1 %
trim already moves the hash by 4 to 14 bits, so the distance rejects most of
them anyway and the tolerance rejects the rest. They are not lost; they become
Identification Candidates and reach an Administrator, like every local
similarity that does not clear the narrow path.

**The hash carries no usable information about visually poor material, and the
measurements show it in both directions.** On near-black, static and heavily
grained works, two encodes of *one* work sat up to 34 bits apart — further than
most unrelated pairs — while three unrelated near-black files hashed
*identically*. Such material is beyond what any 64-bit picture hash can
separate: what is not visually distinguishable does not become distinguishable
by being hashed. The rule survives it in this measurement, but by a margin of
two bits, and the duration condition is doing that work. This is the limit of
the mechanism, not a calibration that a third number would fix — which is why
the path is narrow and review remains the default.

**Why the narrow path has to be narrow.** A merge is not a label that can be
peeled off. It writes Shared Library Knowledge every User sees, and it combines
Personal State for all of them at once: play counts are added, viewing
completion is ORed, the earlier Favourite and Watch Later timestamps win, and
the more recently active side's Personal Rating takes over. A Split separates
the Video Files again and derives their activity again, but ambiguous
Video-level state — organisation, ratings, dismissals — stays with the
continuing Video. A wrong automatic merge therefore costs Personal State that
the corrective action cannot give back. That asymmetry, rather than the
false-positive rate, sets the width of this path.

**Why identification decisions stay Administrator-only.** The argument for
letting ordinary Users accept and reject candidates is real: a large library
can produce thousands of pending cases, and one Administrator is then the
bottleneck. It is declined because the bottleneck is the number of cases rather
than the number of people. An installation serves one person or a small trusted
group, so delegation buys little, while grouped review attacks the backlog
directly. Every identification decision is Shared Library Knowledge, and one
wrong yes changes the library for everybody.

## Consequences

- A Video File with no Perceptual Hash can never be associated. A container
  ffmpeg cannot sample produces no hash at all, and that silence is the same
  absence the remote ladder already lives with rather than an issue to report.
- A similarity found on visually poor material must not be presented as a weak
  signal either. Where the hash cannot separate the material it is not evidence
  of low confidence; it is no evidence.
- The two conditions are evidence about a pair rather than a property of one
  file, so an association is not a new `IdentificationEvidenceClass` and not a
  third `IdentificationDimension`. Work Identification and Site Recognition
  both name a target in prdb's catalogue; an association names nothing.
- A merge must not erase file-level facts. Each Video File keeps its own
  hashes, duration and path, because those are what a later Split and any later
  identification have to work from.
- The numbers rest on a measurement, not on a property of the method. They were
  measured on two encode corpora of twelve works each and one corpus of
  deliberately degenerate material. `tools/Prdb.PerceptualStudy` builds those
  corpora, hashes them and prints every table above, so a reader who doubts a
  number can produce it again rather than argue about it.
