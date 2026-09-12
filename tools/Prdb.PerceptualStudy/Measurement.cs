using System.Text.Json;
using System.Text.Json.Serialization;

using Prdb.Hashing;

namespace Prdb.PerceptualStudy;

/// <summary>
/// One measured file. The work and encode are read off the file name, which is
/// <c>&lt;work&gt;__&lt;encode&gt;.&lt;extension&gt;</c>, so a corpus directory carries its own
/// structure and two files of one work need no index to be recognised as such.
/// </summary>
public sealed record Measurement(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("work")] string Work,
    [property: JsonPropertyName("encode")] string Encode,
    [property: JsonPropertyName("duration")] double? Duration,
    [property: JsonPropertyName("osHash")] string? OsHash,
    [property: JsonPropertyName("pHash")] string? PerceptualHash,
    [property: JsonPropertyName("outcome")] string Outcome);

public static class Measurements
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>
    /// Hashes every file of a directory with the library the product hashes with, several at a
    /// time because each perceptual hash is 25 sequential ffmpeg calls and one file at a time
    /// leaves a machine idle. The values do not depend on how many run at once.
    /// </summary>
    public static async Task<IReadOnlyList<Measurement>> HashAsync(
        string directory,
        int parallelism,
        CancellationToken cancellation)
    {
        var files = Directory
            .EnumerateFiles(directory)
            .Where(file => !System.IO.Path.GetFileName(file).StartsWith('.'))
            .Order()
            .ToArray();

        var results = new Measurement?[files.Length];
        var gate = new SemaphoreSlim(parallelism);
        var done = 0;

        await Task.WhenAll(files.Select(async (file, index) =>
        {
            await gate.WaitAsync(cancellation);

            try
            {
                var name = System.IO.Path.GetFileNameWithoutExtension(file);
                var parts = name.Split("__", 2);
                var result = await new VideoPerceptualHasher().ComputeAsync(file, cancellation);

                results[index] = new Measurement(
                    file,
                    parts[0],
                    parts.Length > 1 ? parts[1] : "master",
                    await Ffmpeg.ProbeDurationAsync(file, cancellation),
                    OsHash.TryCompute(file, out var osHash) ? osHash : null,
                    result.IsComputed ? result.Hash : null,
                    result.Outcome.ToString());

                Console.Error.WriteLine(
                    $"[{Interlocked.Increment(ref done)}/{files.Length}] {name} -> " +
                    (result.IsComputed ? result.Hash : result.Outcome.ToString()));
            }
            finally
            {
                gate.Release();
            }
        }));

        return [.. results.Where(measurement => measurement is not null).Select(m => m!)];
    }

    public static async Task WriteAsync(
        string path,
        IReadOnlyList<Measurement> measurements,
        CancellationToken cancellation) =>
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(measurements, Json),
            cancellation);

    public static async Task<IReadOnlyList<Measurement>> ReadAsync(
        string path,
        CancellationToken cancellation) =>
        JsonSerializer.Deserialize<List<Measurement>>(
            await File.ReadAllTextAsync(path, cancellation)) ?? [];
}
