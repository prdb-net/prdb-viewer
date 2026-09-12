# Prdb.PerceptualStudy

The measurement behind [ADR 0021](../../docs/adr/0021-associate-videos-from-local-similarity-under-a-measured-threshold.md).

That ADR lets a local perceptual similarity merge two Videos without anyone
looking, which is a decision with a number in it — a Hamming distance of 6 of 64
bits, and durations agreeing to within 0.25 %. A number in a decision like that
should be reproducible rather than remembered, so this is what produced it.

It is not part of the product. Nothing in `src/` references it, the Dockerfile
does not build it, and it needs `ffmpeg` and `ffprobe` on `PATH`.

## Running it

```sh
cd tools/Prdb.PerceptualStudy

# build the corpora — the film one fetches four freely licensed films once
dotnet run -- corpus film        /tmp/study/film
dotnet run -- corpus synthetic   /tmp/study/synthetic
dotnet run -- corpus collisions  /tmp/study/collisions
dotnet run -- corpus trims       /tmp/study/trims /tmp/study/film

# hash them with the same Prdb.Hashing the product uses
dotnet run -- hash /tmp/study/film/enc       /tmp/study/film.json
dotnet run -- hash /tmp/study/synthetic/enc  /tmp/study/synthetic.json
dotnet run -- hash /tmp/study/collisions     /tmp/study/collisions.json
dotnet run -- hash /tmp/study/trims          /tmp/study/trims.json

# read the tables the ADR states
dotnet run -- report /tmp/study/film.json /tmp/study/synthetic.json /tmp/study/collisions.json
dotnet run -- trims  /tmp/study/trims.json
```

Building the corpora is the expensive part and takes the better part of an hour
on twelve cores; each step skips what it already produced, so an interrupted run
resumes. The corpora are about 20 GB and can be deleted once the JSON exists —
every table is computed from the hashes alone.

## What the corpora are

A *work* is one visual content. Each is rendered once as a master and then
re-encoded six ways — H.264 at 720p and 480p, H.265 and VP9 at 360p, a regrade,
and a letterboxed 2.35:1 framing — which is the shape one work takes when a
library holds it as several files. Works carry deliberately different durations
because real works do, and the few that share one are there to make duration
agreement happen by accident.

- **film** — twelve non-overlapping excerpts of *Sintel*, *Big Buck Bunny*,
  *Elephants Dream* and *Sita Sings the Blues*. Two excerpts of one film are
  different works sharing a camera, a palette and a grade, which is the hardest
  honest false-positive case. This corpus is what the threshold is read off,
  because it is the one made of real pictures.
- **synthetic** — twelve generated works chosen for what 64 bits struggle with:
  near-black grain, a static vignette, low-contrast grey, title cards, noise.
- **collisions** — thirty unrelated single-encode files of that same difficult
  material, with running times drawn from a three-second band.
- **trims** — each film master shortened at the end by 0.10 % to 1.50 %, with
  nothing else changed. The duration tolerance is derived from this table: it is
  the largest shift whose cost still fits inside the distance the rule may
  spend.

## Two things that would quietly invalidate a repeat

**The ffmpeg build is part of the result.** Frame extraction is the one step of
the hashing method that is not pure arithmetic — which single frame comes back
depends on the argument list and on the build. The published numbers were
measured against ffmpeg 8.0.1. A different build may move individual bits, so a
repeat that disagrees should compare versions before it concludes anything.

**Hash values do not depend on how many files are hashed at once.** The `hash`
command works on several files in parallel because each perceptual hash is 25
sequential ffmpeg calls; the values are identical either way, and the
parallelism is a last optional argument if a machine wants less of it.
