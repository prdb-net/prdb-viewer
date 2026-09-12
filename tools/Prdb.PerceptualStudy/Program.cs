using Prdb.PerceptualStudy;

// The measurement behind ADR 0021, in the three steps it was actually done in: build a corpus,
// hash it, read the tables off it. Each step leaves its result on disk, so the expensive ones do
// not have to be repeated to re-read the cheap one.

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

if (args.Length == 0)
{
    Usage();
    return 2;
}

try
{
    switch (args[0])
    {
        case "corpus" when args.Length >= 3:
            await BuildAsync(args[1], args[2], args.Length > 3 ? args[3] : null);
            return 0;

        case "hash" when args.Length >= 3:
            await HashAsync(args[1], args[2], args.Length > 3 ? int.Parse(args[3]) : 4);
            return 0;

        case "report" when args.Length >= 2:
            await ReportAsync(args[1..]);
            return 0;

        case "trims" when args.Length >= 2:
            Report.PrintTrims(await Measurements.ReadAsync(args[1], cancellation.Token));
            return 0;

        default:
            Usage();
            return 2;
    }
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("cancelled");
    return 130;
}

async Task BuildAsync(string kind, string directory, string? source)
{
    switch (kind)
    {
        case "synthetic":
            await Corpus.BuildSyntheticAsync(directory, cancellation.Token);
            break;

        case "film":
            await Corpus.BuildFilmAsync(directory, cancellation.Token);
            break;

        case "collisions":
            await Corpus.BuildCollisionsAsync(directory, cancellation.Token);
            break;

        case "trims" when source is not null:
            await Corpus.BuildTrimsAsync(source, directory, cancellation.Token);
            break;

        default:
            throw new ArgumentException(
                "corpus takes synthetic, film, collisions, or trims <dir> <built-corpus>.");
    }
}

async Task HashAsync(string directory, string destination, int parallelism)
{
    var measurements = await Measurements.HashAsync(directory, parallelism, cancellation.Token);
    await Measurements.WriteAsync(destination, measurements, cancellation.Token);
    Console.WriteLine($"{measurements.Count} files written to {destination}");
}

async Task ReportAsync(IReadOnlyList<string> paths)
{
    var corpora = new Dictionary<string, IReadOnlyList<Measurement>>();

    foreach (var path in paths)
    {
        corpora[Path.GetFileNameWithoutExtension(path)] =
            await Measurements.ReadAsync(path, cancellation.Token);
    }

    Report.Print(corpora);
}

static void Usage()
{
    Console.Error.WriteLine("""
        perceptual-study — the measurement behind ADR 0021.

          corpus synthetic  <dir>              twelve generated works in six encodes
          corpus film       <dir>              twelve film excerpts in six encodes (needs the network once)
          corpus collisions <dir>              thirty unrelated dark and static files
          corpus trims      <dir> <corpus>     each master of <corpus> shortened by a little

          hash   <dir> <out.json> [parallel]   hash a corpus with Prdb.Hashing
          report <json> [<json> ...]           the distance and rule tables
          trims  <json>                        what a duration difference alone costs

        Needs ffmpeg and ffprobe on PATH. Name a film corpus's json "film" for the
        report to count its same-work pairs as the ones a rule must not miss.
        """);
}
