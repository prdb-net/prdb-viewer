using System.Diagnostics;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

/// <summary>
/// Builds the small synthetic clips the playback tests need. They are generated from ffmpeg's own
/// test sources rather than committed to the repository, so the fixtures stay freely
/// redistributable and can never carry identifying content from anyone's library.
/// </summary>
internal static class BrowserPlaybackFixtures
{
    public static bool FfmpegIsAvailable =>
        Which("ffmpeg") && Which("ffprobe");

    /// <summary>
    /// A baseline H.264 and AAC clip in MP4 — the shape every current browser plays directly.
    /// </summary>
    public static Task<string> BaselineMp4Async(string directory, string name = "baseline.mp4") =>
        GenerateAsync(directory, name,
        [
            "-c:v", "libx264", "-profile:v", "baseline", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "64k", "-movflags", "+faststart",
        ]);

    /// <summary>A VP9 and Opus clip in WebM, which only some browsers play.</summary>
    public static Task<string> ClientDependentWebmAsync(string directory) =>
        GenerateAsync(directory, "client-dependent.webm",
        [
            "-c:v", "libvpx-vp9", "-b:v", "200k", "-c:a", "libopus", "-b:a", "48k",
        ]);

    /// <summary>An MPEG-2 clip, which no browser plays directly.</summary>
    public static Task<string> UnsupportedMpegAsync(string directory) =>
        GenerateAsync(directory, "unsupported.mpg",
        [
            "-c:v", "mpeg2video", "-b:v", "600k", "-c:a", "mp2", "-b:a", "64k",
        ]);

    private static async Task<string> GenerateAsync(
        string directory,
        string name,
        string[] encoding)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);

        if (File.Exists(path))
        {
            return path;
        }

        string[] arguments =
        [
            "-nostdin", "-hide_banner", "-v", "error", "-y",
            "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=15:duration=4",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=4",
            .. encoding,
            path,
        ];
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

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"The fixture {name} could not be generated. {error}");
        }

        return path;
    }

    private static bool Which(string tool)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = tool,
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            process!.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
