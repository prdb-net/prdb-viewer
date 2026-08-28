using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

using Prdb.Viewer.Core.Library;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// What inspection established about one file: the media configuration the direct-play contract
/// reasons about, and how long it runs.
/// </summary>
public sealed record MediaProbeFacts(MediaConfiguration Media, long DurationMilliseconds);

public interface IMediaProbe
{
    Task<MediaProbeFacts?> InspectAsync(string path, CancellationToken cancellationToken = default);
}

public sealed class FfprobeMediaProbe : IMediaProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public async Task<MediaProbeFacts?> InspectAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffprobe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add("-v");
        process.StartInfo.ArgumentList.Add("error");
        process.StartInfo.ArgumentList.Add("-print_format");
        process.StartInfo.ArgumentList.Add("json");
        process.StartInfo.ArgumentList.Add("-show_format");
        process.StartInfo.ArgumentList.Add("-show_streams");
        process.StartInfo.ArgumentList.Add("--");
        process.StartInfo.ArgumentList.Add(path);

        try
        {
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            _ = await error;
            return process.ExitCode == 0
                ? FfprobeOutputParser.Parse(await output)
                : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryTerminate(process);
            return null;
        }
        catch (OperationCanceledException)
        {
            TryTerminate(process);
            throw;
        }
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}

public static class FfprobeOutputParser
{
    public static MediaProbeFacts? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var streams = root.GetProperty("streams").EnumerateArray().ToArray();
            var video = streams.FirstOrDefault(stream => Type(stream) == "video");

            if (video.ValueKind == JsonValueKind.Undefined)
            {
                return null;
            }

            var audio = streams.FirstOrDefault(stream => Type(stream) == "audio");
            var format = root.TryGetProperty("format", out var formatElement)
                ? formatElement
                : default;
            var duration = Decimal(format, "duration") ?? Decimal(video, "duration") ?? 0;
            var media = new MediaConfiguration(
                Text(format, "format_name") ?? "unknown",
                Text(video, "codec_name") ?? "unknown",
                Text(audio, "codec_name"))
            {
                VideoProfile = Text(video, "profile"),
                VideoLevel = PositiveInteger(video, "level"),
                BitDepth = BitDepthOf(video),
                Width = Integer(video, "width"),
                Height = Integer(video, "height"),
                FrameRate = Rate(Text(video, "avg_frame_rate") ?? Text(video, "r_frame_rate")),
                VideoBitrate = Bitrate(video),
                AudioChannels = Integer(audio, "channels"),
                AudioSampleRate = Integer(audio, "sample_rate"),
                AudioBitrate = Bitrate(audio),
            };

            return new MediaProbeFacts(
                media,
                Math.Max(0, (long)Math.Round(duration * 1000, MidpointRounding.AwayFromZero)));
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? Type(JsonElement stream) => Text(stream, "codec_type");

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// A whole number the inspector may report either as a number or as a string: `width` is a
    /// number, `sample_rate` and `bits_per_raw_sample` are strings, and asking a string element for
    /// an integer throws rather than answering.
    /// </summary>
    private static int? Integer(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt32(out var number) ? number : null,
            JsonValueKind.String => ParsedInteger(value.GetString()),
            _ => null,
        };
    }

    /// <summary>
    /// A level the inspector could not determine is reported as a negative sentinel, which must
    /// stay unknown rather than become a number in a codec string.
    /// </summary>
    private static int? PositiveInteger(JsonElement element, string name) =>
        Integer(element, name) is > 0 and var value ? value : null;

    /// <summary>
    /// Bits per sample, taken from the pixel format the inspector names. Anything the name does
    /// not state stays unknown.
    /// </summary>
    private static int? BitDepthOf(JsonElement video)
    {
        if (Integer(video, "bits_per_raw_sample") is > 0 and var stated)
        {
            return stated;
        }

        var format = Text(video, "pix_fmt");

        if (format is null)
        {
            return null;
        }

        foreach (var depth in new[] { 16, 14, 12, 10 })
        {
            if (format.Contains(depth.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                return depth;
            }
        }

        return format.StartsWith("yuv", StringComparison.Ordinal) ||
               format.StartsWith("gbr", StringComparison.Ordinal) ||
               format.StartsWith("rgb", StringComparison.Ordinal)
            ? 8
            : null;
    }

    /// <summary>The `numerator/denominator` frame rate the inspector reports.</summary>
    private static double? Rate(string? value)
    {
        var parts = (value ?? string.Empty).Split('/');

        if (parts.Length != 2 ||
            !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) ||
            denominator <= 0 ||
            numerator <= 0)
        {
            return null;
        }

        return Math.Round(numerator / denominator, 3);
    }

    private static long? Bitrate(JsonElement element) =>
        long.TryParse(
            Text(element, "bit_rate"),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value) && value > 0
            ? value
            : null;

    private static int? ParsedInteger(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
        parsed > 0
            ? parsed
            : null;


    private static decimal? Decimal(JsonElement element, string name) =>
        decimal.TryParse(Text(element, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
