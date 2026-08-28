using System.Diagnostics;
using System.Globalization;

namespace Prdb.Viewer.Infrastructure.Library;

public interface IPreviewImageGenerator
{
    Task<bool> TryGenerateAsync(
        string sourcePath,
        double sampleSeconds,
        int width,
        string destinationPath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Writes one still frame per Video File with ffmpeg. It only ever reads the source and only ever
/// writes beneath the application's own data directory.
/// </summary>
public sealed class FfmpegPreviewImageGenerator : IPreviewImageGenerator
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    public async Task<bool> TryGenerateAsync(
        string sourcePath,
        double sampleSeconds,
        int width,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        foreach (var argument in new[]
        {
            "-nostdin",
            "-hide_banner",
            "-v", "error",
            "-ss", sampleSeconds.ToString("0.###", CultureInfo.InvariantCulture),
            "-i", sourcePath,
            "-map", "0:v:0",
            "-frames:v", "1",
            "-vf", $"scale={width.ToString(CultureInfo.InvariantCulture)}:-2",
            "-q:v", "4",
            "-f", "image2",
            "-y",
            destinationPath,
        })
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            _ = await output;
            _ = await error;

            return process.ExitCode == 0 &&
                   File.Exists(destinationPath) &&
                   new FileInfo(destinationPath).Length > 0;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryTerminate(process);
            return false;
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
