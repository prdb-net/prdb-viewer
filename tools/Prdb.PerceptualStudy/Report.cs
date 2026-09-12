using Prdb.Hashing;

namespace Prdb.PerceptualStudy;

/// <summary>
/// Two measured files and what separates them: the Hamming distance of their Perceptual Hashes,
/// and how far apart their durations are as a percentage of the longer one — the two quantities
/// ADR 0021's rule is written in.
/// </summary>
public sealed record Pair(Measurement Left, Measurement Right, int Distance, double DurationPercent)
{
    public bool SameWork => Left.Work == Right.Work;
}

/// <summary>
/// Prints the tables ADR 0021 states. Every number the ADR gives is produced here, so a reader
/// who doubts one can rebuild the corpus and get it again rather than take it on trust.
/// </summary>
public static class Report
{
    /// <summary>The threshold ADR 0021 decided on, and the two it was chosen against.</summary>
    private static readonly int[] Thresholds = [4, 6, 8];

    /// <summary>The tolerances ADR 0021 decided between.</summary>
    private static readonly double[] Tolerances = [0.10, 0.25, 0.50];

    public static int Distance(Measurement left, Measurement right) =>
        PerceptualHashDistanceOf(left.PerceptualHash!, right.PerceptualHash!);

    private static int PerceptualHashDistanceOf(string left, string right) =>
        PerceptualHashDistance.Between(left, right)
        ?? throw new InvalidOperationException($"Not a comparable pair: {left} / {right}.");

    /// <summary>
    /// A trim is a different cut of a work rather than a different encode of it, so it belongs to
    /// the trim table and not to the distribution the threshold is read off. Leaving it in would
    /// count a deliberately shifted file as evidence about what encoding does.
    /// </summary>
    private static bool IsEncode(Measurement measurement) =>
        !measurement.Encode.StartsWith("trim", StringComparison.Ordinal) &&
        !measurement.Encode.StartsWith("dur-", StringComparison.Ordinal) &&
        measurement.Encode != "ref";

    public static IReadOnlyList<Pair> PairsOf(IReadOnlyList<Measurement> measurements)
    {
        var usable = measurements
            .Where(m => m.PerceptualHash is not null && m.Duration is > 0 && IsEncode(m))
            .ToArray();
        var pairs = new List<Pair>();

        for (var i = 0; i < usable.Length; i++)
        {
            for (var j = i + 1; j < usable.Length; j++)
            {
                var left = usable[i];
                var right = usable[j];
                var longer = Math.Max(left.Duration!.Value, right.Duration!.Value);
                var difference = Math.Abs(left.Duration.Value - right.Duration.Value);

                pairs.Add(new Pair(
                    left,
                    right,
                    Distance(left, right),
                    difference / longer * 100));
            }
        }

        return pairs;
    }

    public static void Print(IReadOnlyDictionary<string, IReadOnlyList<Measurement>> corpora)
    {
        var pairsByCorpus = corpora.ToDictionary(
            entry => entry.Key,
            entry => PairsOf(entry.Value));

        Console.WriteLine("== what the hash does to encodes of one work, and to strangers ==");
        Console.WriteLine();
        Console.WriteLine($"{"corpus",-14} {"same work",-34} different works");

        foreach (var (name, pairs) in pairsByCorpus)
        {
            var same = pairs.Where(pair => pair.SameWork).Select(pair => pair.Distance).Order().ToArray();
            var different = pairs.Where(pair => !pair.SameWork).Select(pair => pair.Distance).Order().ToArray();
            Console.WriteLine($"{name,-14} {Describe(same),-34} {Describe(different)}");
        }

        var allDifferent = pairsByCorpus.Values.SelectMany(pairs => pairs.Where(p => !p.SameWork)).ToArray();
        var filmSame = pairsByCorpus.TryGetValue("film", out var film)
            ? film.Where(pair => pair.SameWork).ToArray()
            : [];

        Console.WriteLine();
        Console.WriteLine(
            $"== what each rule admits, over {allDifferent.Length} different-work pairs ==");
        Console.WriteLine();
        Console.WriteLine($"{"",-8}" + string.Concat(Tolerances.Select(t => $"  dur <= {t,4:0.00} %")));

        foreach (var threshold in Thresholds)
        {
            var cells = Tolerances.Select(tolerance =>
            {
                var admitted = allDifferent.Count(
                    pair => pair.Distance <= threshold && pair.DurationPercent <= tolerance);
                var missed = filmSame.Count(
                    pair => !(pair.Distance <= threshold && pair.DurationPercent <= tolerance));

                return $"  {admitted,5} false, {missed,3} missed";
            });

            Console.WriteLine($"d <= {threshold,-3}" + string.Concat(cells));
        }

        Console.WriteLine();
        Console.WriteLine(
            "\"false\" is a pair of different works the rule would merge without review;");
        Console.WriteLine(
            "\"missed\" is a pair of encodes of one film work the rule would not merge.");
    }

    /// <summary>
    /// What a duration difference alone costs, with the picture held constant. This is the table
    /// the tolerance is derived from: the largest shift whose cost still fits inside the distance
    /// the rule is allowed to spend.
    /// </summary>
    public static void PrintTrims(IReadOnlyList<Measurement> measurements)
    {
        var byWork = measurements
            .Where(m => m.PerceptualHash is not null)
            .GroupBy(m => m.Work)
            .ToDictionary(group => group.Key, group => group.ToDictionary(m => m.Encode));

        var labels = Corpus.TrimPercentages
            .Select(p => ($"trim{p:0.00}".Replace(".", "p", StringComparison.Ordinal), p))
            .ToArray();

        Console.WriteLine("== what a duration difference alone costs ==");
        Console.WriteLine();
        Console.WriteLine("The same picture, shortened at the end. Nothing else changes, so the");
        Console.WriteLine("distance to the untouched encode is what the shift by itself costs.");
        Console.WriteLine();
        Console.WriteLine($"{"work",-14}" + string.Concat(labels.Select(l => $"{l.p,8:0.00} %")));

        var worst = new Dictionary<string, int>();

        foreach (var (work, encodes) in byWork.OrderBy(entry => entry.Key))
        {
            if (!encodes.TryGetValue("ref", out var reference))
            {
                continue;
            }

            var cells = labels.Select(label =>
            {
                if (!encodes.TryGetValue(label.Item1, out var trimmed))
                {
                    return $"{"-",10}";
                }

                var distance = Distance(reference, trimmed);
                worst[label.Item1] = Math.Max(worst.GetValueOrDefault(label.Item1), distance);

                return $"{distance,10}";
            });

            Console.WriteLine($"{work,-14}" + string.Concat(cells));
        }

        Console.WriteLine();
        Console.WriteLine(
            $"{"worst",-14}" +
            string.Concat(labels.Select(l => $"{worst.GetValueOrDefault(l.Item1),10}")));
    }

    private static string Describe(IReadOnlyList<int> distances) =>
        distances.Count == 0
            ? "-"
            : $"n={distances.Count,-5} min={distances[0],-3} " +
              $"median={distances[distances.Count / 2],-3} max={distances[^1]}";
}
