using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Prdb.PerceptualStudy;

public static class Formats
{
    public static CultureInfo Invariant => CultureInfo.InvariantCulture;
}

/// <summary>
/// The two external programs this study needs. Failures are thrown here rather than returned,
/// because unlike the product a measurement run has nothing useful to do with a corpus it could
/// not build.
/// </summary>
public static class Ffmpeg
{
    public static async Task RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellation,
        string? workingDirectory = null)
    {
        var start = new ProcessStartInfo("ffmpeg")
        {
            RedirectStandardError = true,
            ArgumentList = { "-nostdin", "-loglevel", "error", "-y" },
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("ffmpeg could not be started.");
        var errors = await process.StandardError.ReadToEndAsync(cancellation);
        await process.WaitForExitAsync(cancellation);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"ffmpeg failed ({process.ExitCode}): {errors.Trim()}");
        }
    }

    public static async Task<double?> ProbeDurationAsync(string path, CancellationToken cancellation)
    {
        var start = new ProcessStartInfo("ffprobe")
        {
            RedirectStandardOutput = true,
            ArgumentList =
            {
                "-v", "quiet", "-print_format", "json", "-show_entries", "format=duration", path,
            },
        };

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("ffprobe could not be started.");
        var output = await process.StandardOutput.ReadToEndAsync(cancellation);
        await process.WaitForExitAsync(cancellation);

        using var document = JsonDocument.Parse(output);

        if (!document.RootElement.TryGetProperty("format", out var format) ||
            !format.TryGetProperty("duration", out var value))
        {
            return null;
        }

        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();

        return double.TryParse(text, NumberStyles.Float, Formats.Invariant, out var seconds) &&
               seconds > 0
            ? seconds
            : null;
    }
}
