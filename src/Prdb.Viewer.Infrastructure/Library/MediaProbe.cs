using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Prdb.Viewer.Infrastructure.Library;

public sealed record MediaProbeFacts(
    string ContainerFormat,
    string VideoCodec,
    string? AudioCodec,
    long DurationMilliseconds,
    int? Width,
    int? Height);

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

            return new MediaProbeFacts(
                Text(format, "format_name") ?? "unknown",
                Text(video, "codec_name") ?? "unknown",
                Text(audio, "codec_name"),
                Math.Max(0, (long)Math.Round(duration * 1000, MidpointRounding.AwayFromZero)),
                Integer(video, "width"),
                Integer(video, "height"));
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

    private static int? Integer(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.TryGetInt32(out var result)
            ? result
            : null;

    private static decimal? Decimal(JsonElement element, string name) =>
        decimal.TryParse(Text(element, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
