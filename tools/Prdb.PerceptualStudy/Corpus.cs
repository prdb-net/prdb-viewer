namespace Prdb.PerceptualStudy;

/// <summary>
/// One visual content, rendered once as a master and then re-encoded several ways — the shape one
/// work takes when a library holds it as several files. Works carry deliberately different
/// durations, because real works do; where two share one it is to make duration agreement happen
/// by accident, which is the case the rule in ADR 0021 has to survive.
/// </summary>
public sealed record Work(string Name, int DurationSeconds, string Source);

/// <summary>
/// Builds the three corpora ADR 0021 was measured on. Sources are synthesised at 640x360 and
/// scaled up, because the expensive lavfi generators cost by the pixel while the hash sees a
/// 64x64 montage either way.
/// </summary>
public static class Corpus
{
    private const string Upscale = "scale=1280:720:flags=bicubic,format=yuv420p";
    private const int Fps = 25;

    /// <summary>
    /// The six encodes every work is delivered in: two H.264 sizes, H.265, VP9, a different
    /// grade, and a letterboxed 2.35:1 framing.
    /// </summary>
    public static readonly (string Name, string Extension, string[] Arguments)[] Encodes =
    [
        ("h264-720-crf20", "mp4",
            ["-vf", "scale=1280:720", "-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-an"]),
        ("h264-480-crf30", "mp4",
            ["-vf", "scale=854:480", "-c:v", "libx264", "-preset", "veryfast", "-crf", "30", "-an"]),
        ("h265-360-crf30", "mkv",
            ["-vf", "scale=640:360", "-c:v", "libx265", "-preset", "ultrafast", "-crf", "30", "-an"]),
        ("vp9-360", "webm",
            ["-vf", "scale=640:360", "-c:v", "libvpx-vp9", "-deadline", "realtime", "-cpu-used", "8",
             "-crf", "40", "-b:v", "0", "-an"]),
        ("regrade-720", "mp4",
            ["-vf", "scale=1280:720,eq=brightness=0.06:contrast=1.12:saturation=1.15,unsharp=5:5:0.8",
             "-c:v", "libx264", "-preset", "veryfast", "-crf", "22", "-an"]),
        ("letterbox-720", "mp4",
            ["-vf", "scale=1280:536,pad=1280:720:0:92:black",
             "-c:v", "libx264", "-preset", "veryfast", "-crf", "22", "-an"]),
    ];

    /// <summary>
    /// Twelve generated works chosen for the material 64 bits struggle with. The two static dark
    /// works share a duration on purpose.
    /// </summary>
    public static readonly Work[] Synthetic =
    [
        new("bright-motion", 197, $"testsrc2=size=640x360:rate={Fps},eq=saturation=1.3"),
        new("scenes", 233, "SCENES"),
        new("fractal", 181, $"mandelbrot=size=640x360:rate={Fps}:maxiter=120"),
        new("life-bw", 179,
            $"life=size=640x360:rate={Fps}:mold=10:ratio=0.1:death_color=#101010:life_color=#e0e0e0"),
        new("noise-grain", 211, $"color=c=gray:size=640x360:rate={Fps},noise=alls=60:allf=t+u"),
        new("dark-motion", 223, $"testsrc2=size=640x360:rate={Fps},eq=brightness=-0.34:contrast=0.22"),
        new("dark-drift", 199,
            $"mandelbrot=size=640x360:rate={Fps}:maxiter=60,eq=brightness=-0.36:contrast=0.20"),
        new("dark-static-a", 203,
            $"color=c=#0a0c10:size=640x360:rate={Fps},noise=alls=6:allf=t,vignette"),
        new("dark-static-b", 203,
            $"color=c=#0d0a0c:size=640x360:rate={Fps},noise=alls=5:allf=t,vignette=PI/5"),
        new("grey-lowcontrast", 241,
            $"gradients=size=640x360:rate={Fps}:c0=#6a6a6a:c1=#7c7c7c:speed=0.01"),
        new("titlecards", 187,
            $"color=c=#101014:size=640x360:rate={Fps}," +
            @"drawtext=text='%{eif\:t\:d}':fontsize=72:fontcolor=#c8c8c8:x=(w-tw)/2:y=(h-th)/2"),
        new("slow-pan", 227,
            $"smptebars=size=640x360:rate={Fps},scale=2*iw:-1," +
            "crop=640:360:'(iw-ow)*abs(sin(t/40))':'(ih-oh)/2'"),
    ];

    private static readonly string[] SceneSources =
    [
        $"smptebars=size=640x360:rate={Fps}",
        $"testsrc=size=640x360:rate={Fps}",
        $"mandelbrot=size=640x360:rate={Fps}:maxiter=100",
        $"rgbtestsrc=size=640x360:rate={Fps}",
        $"color=c=#141414:size=640x360:rate={Fps},noise=alls=14:allf=t",
        $"life=size=640x360:rate={Fps}:mold=8",
    ];

    /// <summary>
    /// The films the excerpts are cut from. Freely licensed, and named with the address they were
    /// fetched from so the corpus can be rebuilt.
    /// </summary>
    public static readonly (string File, string Url)[] Films =
    [
        ("sintel.mp4", "https://archive.org/download/Sintel/sintel-2048-surround_512kb.mp4"),
        ("bbb.mp4", "https://archive.org/download/BigBuckBunny_124/Content/big_buck_bunny_720p_surround.mp4"),
        ("ed.mp4", "https://archive.org/download/ElephantsDream/ed_hd_512kb.mp4"),
        ("sita.mp4", "https://archive.org/download/Sita_Sings_the_Blues/Sita_Sings_the_Blues_small.mp4"),
    ];

    /// <summary>
    /// Twelve non-overlapping excerpts. Two excerpts of one film are different works that share a
    /// camera, a palette and a grade, which is the hardest honest false-positive case; sita-a and
    /// sita-b additionally share a duration.
    /// </summary>
    public static readonly (string Name, string Film, int Start, int Duration)[] Excerpts =
    [
        ("sintel-a", "sintel.mp4", 70, 197),
        ("sintel-b", "sintel.mp4", 300, 233),
        ("sintel-c", "sintel.mp4", 560, 181),
        ("bbb-a", "bbb.mp4", 40, 179),
        ("bbb-b", "bbb.mp4", 300, 211),
        ("ed-a", "ed.mp4", 40, 223),
        ("ed-b", "ed.mp4", 330, 199),
        ("sita-a", "sita.mp4", 200, 203),
        ("sita-b", "sita.mp4", 900, 203),
        ("sita-c", "sita.mp4", 1800, 241),
        ("sita-d", "sita.mp4", 2700, 187),
        ("sita-e", "sita.mp4", 3600, 227),
    ];

    /// <summary>
    /// Thirty unrelated files of the material 64 bits collide on, with running times drawn from a
    /// three-second band so that many pairs agree on duration by accident.
    /// </summary>
    public static IEnumerable<Work> Collisions()
    {
        for (var n = 1; n <= 8; n++)
        {
            yield return new Work(
                $"black-grain-{n}",
                200 + (n % 3),
                $"color=c=0x0{n}0{n}0{(n % 9) + 1}:size=640x360:rate={Fps}," +
                $"noise=alls={4 + n}:allf=t,vignette=PI/{4 + n}");
        }

        for (var n = 1; n <= 6; n++)
        {
            yield return new Work(
                $"dim-drift-{n}",
                200 + (n % 3),
                $"color=c=#14141a:size=640x360:rate={Fps}," +
                $"drawbox=x='{n * 40}+60*sin(t/{6 + n})':y='120+40*cos(t/{5 + n})':" +
                $"w={60 + (n * 10)}:h={40 + (n * 8)}:color=#3a3a44@0.9:t=fill,noise=alls=5:allf=t");
        }

        for (var n = 1; n <= 6; n++)
        {
            yield return new Work(
                $"grey-{n}",
                200 + (n % 3),
                $"gradients=size=640x360:rate={Fps}:c0=0x6{n}6{n}6{n}:c1=0x7{n}7{n}7{n}:" +
                $"speed=0.0{n}:x0={n * 30}:y0={n * 20}");
        }

        for (var n = 1; n <= 6; n++)
        {
            yield return new Work(
                $"card-{n}",
                200 + (n % 3),
                $"color=c=#0e0e12:size=640x360:rate={Fps}," +
                $"drawtext=text='{new string('A', n)}':fontsize={40 + (n * 12)}:" +
                "fontcolor=#b0b0b8:x=(w-tw)/2:y=(h-th)/2");
        }

        for (var n = 1; n <= 4; n++)
        {
            yield return new Work(
                $"still-{n}",
                200 + (n % 3),
                $"smptebars=size=640x360:rate={Fps}," +
                $"eq=brightness=-0.{25 + n}:contrast=0.1{n},noise=alls=3:allf=t");
        }
    }

    /// <summary>
    /// The fractions a trim corpus shortens a work by. They bracket the tolerance so that the
    /// point where a duration difference exhausts the distance budget is visible rather than
    /// assumed.
    /// </summary>
    public static readonly double[] TrimPercentages = [0.10, 0.25, 0.50, 0.75, 1.00, 1.50];

    public static async Task BuildSyntheticAsync(string directory, CancellationToken cancellation)
    {
        var masters = Path.Combine(directory, "master");
        Directory.CreateDirectory(masters);

        foreach (var work in Synthetic)
        {
            var master = Path.Combine(masters, $"{work.Name}.mp4");

            if (File.Exists(master))
            {
                continue;
            }

            Console.WriteLine($"  {work.Name} ({work.DurationSeconds}s)");

            if (work.Source == "SCENES")
            {
                await BuildScenesAsync(master, work.DurationSeconds, cancellation);
                continue;
            }

            await Ffmpeg.RunAsync(
                ["-f", "lavfi", "-i", work.Source, "-t", work.DurationSeconds.ToString(),
                 "-vf", Upscale, "-c:v", "libx264", "-preset", "ultrafast", "-crf", "20", master],
                cancellation);
        }

        await EncodeAllAsync(directory, cancellation);
    }

    public static async Task BuildFilmAsync(string directory, CancellationToken cancellation)
    {
        var sources = Path.Combine(directory, "src");
        var masters = Path.Combine(directory, "master");
        Directory.CreateDirectory(sources);
        Directory.CreateDirectory(masters);

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };

        foreach (var (file, url) in Films)
        {
            var path = Path.Combine(sources, file);

            if (File.Exists(path))
            {
                continue;
            }

            Console.WriteLine($"  fetching {file}");
            await using var stream = await client.GetStreamAsync(url, cancellation);
            await using var target = File.Create(path);
            await stream.CopyToAsync(target, cancellation);
        }

        foreach (var (name, film, start, duration) in Excerpts)
        {
            var master = Path.Combine(masters, $"{name}.mp4");
            var source = Path.Combine(sources, film);

            if (File.Exists(master) || !File.Exists(source))
            {
                continue;
            }

            Console.WriteLine($"  {name} <- {film} @{start}s +{duration}s");
            await Ffmpeg.RunAsync(
                ["-ss", start.ToString(), "-i", source, "-t", duration.ToString(),
                 "-vf", "scale=1280:720:force_original_aspect_ratio=decrease," +
                        "pad=1280:720:(ow-iw)/2:(oh-ih)/2,format=yuv420p",
                 "-c:v", "libx264", "-preset", "ultrafast", "-crf", "18", "-an", master],
                cancellation);
        }

        await EncodeAllAsync(directory, cancellation);
    }

    public static async Task BuildCollisionsAsync(string directory, CancellationToken cancellation)
    {
        Directory.CreateDirectory(directory);

        foreach (var work in Collisions())
        {
            var path = Path.Combine(directory, $"{work.Name}.mp4");

            if (File.Exists(path))
            {
                continue;
            }

            Console.WriteLine($"  {work.Name} ({work.DurationSeconds}s)");
            await Ffmpeg.RunAsync(
                ["-f", "lavfi", "-i", work.Source, "-t", work.DurationSeconds.ToString(),
                 "-vf", Upscale, "-c:v", "libx264", "-preset", "ultrafast", "-crf", "22", path],
                cancellation);
        }
    }

    /// <summary>
    /// Shortens each master at the end by each of <see cref="TrimPercentages"/> and keeps one
    /// untouched encode beside them as the reference. Nothing but the duration changes, so the
    /// distance measured against the reference is what the shift alone costs.
    /// </summary>
    public static async Task BuildTrimsAsync(
        string sourceCorpus,
        string directory,
        CancellationToken cancellation)
    {
        Directory.CreateDirectory(directory);
        var masters = Path.Combine(sourceCorpus, "master");

        foreach (var master in Directory.EnumerateFiles(masters, "*.mp4").Order())
        {
            var name = Path.GetFileNameWithoutExtension(master);
            var reference = Path.Combine(sourceCorpus, "enc", $"{name}__h264-720-crf20.mp4");

            if (!File.Exists(reference))
            {
                continue;
            }

            File.Copy(reference, Path.Combine(directory, $"{name}__ref.mp4"), overwrite: true);
            var duration = await Ffmpeg.ProbeDurationAsync(master, cancellation);

            if (duration is null)
            {
                continue;
            }

            Console.WriteLine($"  {name}");

            foreach (var percentage in TrimPercentages)
            {
                var label = $"trim{percentage:0.00}".Replace(".", "p", StringComparison.Ordinal);
                var path = Path.Combine(directory, $"{name}__{label}.mp4");

                if (File.Exists(path))
                {
                    continue;
                }

                var shortened = duration.Value * (1 - (percentage / 100));
                await Ffmpeg.RunAsync(
                    ["-i", master, "-t", shortened.ToString("0.0000", Formats.Invariant),
                     "-c:v", "libx264", "-preset", "veryfast", "-crf", "22", "-an", path],
                    cancellation);
            }
        }
    }

    private static async Task BuildScenesAsync(
        string master,
        int duration,
        CancellationToken cancellation)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(master)!, ".scenes");
        Directory.CreateDirectory(temporary);

        try
        {
            var segment = duration / SceneSources.Length;

            for (var i = 0; i < SceneSources.Length; i++)
            {
                await Ffmpeg.RunAsync(
                    ["-f", "lavfi", "-i", SceneSources[i], "-t", segment.ToString(),
                     "-vf", Upscale, "-c:v", "libx264", "-preset", "ultrafast", "-crf", "22",
                     Path.Combine(temporary, $"s{i}.mp4")],
                    cancellation);
            }

            var list = Path.Combine(temporary, "list.txt");
            await File.WriteAllLinesAsync(
                list,
                Enumerable.Range(0, SceneSources.Length).Select(i => $"file 's{i}.mp4'"),
                cancellation);

            await Ffmpeg.RunAsync(
                ["-f", "concat", "-safe", "0", "-i", "list.txt", "-c", "copy", master],
                cancellation,
                workingDirectory: temporary);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    private static async Task EncodeAllAsync(string directory, CancellationToken cancellation)
    {
        var encodes = Path.Combine(directory, "enc");
        Directory.CreateDirectory(encodes);

        foreach (var master in Directory.EnumerateFiles(Path.Combine(directory, "master"), "*.mp4").Order())
        {
            var name = Path.GetFileNameWithoutExtension(master);
            Console.WriteLine($"  encoding {name}");

            foreach (var (encode, extension, arguments) in Encodes)
            {
                var path = Path.Combine(encodes, $"{name}__{encode}.{extension}");

                if (File.Exists(path))
                {
                    continue;
                }

                await Ffmpeg.RunAsync([.. new[] { "-i", master }, .. arguments, path], cancellation);
            }
        }
    }
}
