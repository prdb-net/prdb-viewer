using System.Diagnostics;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Access;
using Prdb.Viewer.Infrastructure.Configuration;
using Prdb.Viewer.Infrastructure.Library;

namespace Prdb.Viewer.Host.Development;

/// <summary>
/// Fills an empty installation with the state a real one reaches after a few days, so that looking
/// at a screen is cheap enough to be done before a release rather than after one.
///
/// Three releases running, the defects were found by opening the deployed product rather than by a
/// failing check, and every one of them needed an installation that had actually done some work to
/// be visible at all. Reaching that state by hand costs a prdb key, a library of files, and the
/// patience to wait for six lanes; reaching it here costs one command.
///
/// The states it goes out of its way to produce are the dull ones. A lane asked to do work it has
/// already done, a Video prdb does not recognise, an Account that was turned off — these are what
/// an installation is mostly made of, and what no fixture had. The defect that made a lane read
/// `0/0` lived in exactly that gap.
/// </summary>
public static class SeedCommand
{
    private const string Seed = "seed";

    /// <summary>The password every seeded Account is given. It is printed, and it is not a secret.</summary>
    private const string Password = "seed-password-2026";

    private static readonly (string Path, string Video, string Audio, string Container)[] Files =
    [
        // Ordinary H.264 in MP4: the broadest case, and one the browser has to be asked about.
        ("films/first-film.mp4", "libx264", "aac", "mp4"),
        // VP8 in WebM: the conservative baseline any client plays.
        ("films/second-film.webm", "libvpx", "libvorbis", "webm"),
        // Nested deeper, so the traversal has more than one level to walk and a path worth reading.
        ("films/series/third-film.mp4", "libx264", "aac", "mp4"),
    ];

    public static bool Matches(string[] arguments) => arguments is [Seed];

    public static async Task<int> RunAsync(
        IServiceProvider services,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var access = scope.ServiceProvider.GetRequiredService<AccessService>();

        // The same guard Restore uses, and for the same reason: this writes an installation from
        // nothing, and it must never be able to do that over one that someone is using.
        if (await access.IsClaimedAsync(cancellationToken))
        {
            await error.WriteLineAsync(
                "This installation is already claimed. Seeding only ever writes into an empty, " +
                "unclaimed data directory, so that it cannot overwrite an installation in use.");
            return 1;
        }

        var mountRoot = scope.ServiceProvider.GetRequiredService<LibraryMountRoot>().Path;

        if (!await WriteLibraryAsync(mountRoot, output, error, cancellationToken))
        {
            return 1;
        }

        var administrator = await ClaimAsync(access, scope.ServiceProvider, output, error, cancellationToken);

        if (administrator is null)
        {
            return 1;
        }

        await RegisterAccountsAsync(access, output, cancellationToken);

        if (!await ActivateLibraryAsync(scope.ServiceProvider, mountRoot, output, error, cancellationToken))
        {
            return 1;
        }

        await output.WriteLineAsync("Running the lanes over the new library.");
        await DrainAsync(services, cancellationToken);

        // The second Scan is the point of this, not a flourish. It is what leaves each derived lane
        // with a run that had nothing to do, which is the state an installation sits in almost all
        // of the time and the one the screens were never looked at in.
        await output.WriteLineAsync("Scanning a second time, so the lanes have a run with nothing to do.");
        await RescanAsync(services, cancellationToken);
        await DrainAsync(services, cancellationToken);

        await ReportAsync(services, administrator, output, cancellationToken);
        return 0;
    }

    /// <summary>
    /// Writes real video files, because every lane past the traversal reads them. Files of the
    /// right name and the wrong content produce an installation full of invalid-content issues,
    /// which is a state worth seeing but not the one this is for.
    /// </summary>
    private static async Task<bool> WriteLibraryAsync(
        string mountRoot,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        await output.WriteLineAsync($"Writing {Files.Length} video files beneath {mountRoot}.");

        foreach (var (relativePath, video, audio, container) in Files)
        {
            var path = Path.Combine(mountRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            if (File.Exists(path))
            {
                continue;
            }

            var arguments = new[]
            {
                "-nostdin", "-loglevel", "error", "-y",
                "-f", "lavfi", "-i", "testsrc=duration=2:size=320x240:rate=10",
                "-f", "lavfi", "-i", "sine=frequency=440:duration=2",
                "-c:v", video, "-pix_fmt", "yuv420p", "-c:a", audio, "-shortest",
                "-f", container, path,
            };

            if (!await FfmpegAsync(arguments, error, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<bool> FfmpegAsync(
        string[] arguments,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(start);

            if (process is null)
            {
                await error.WriteLineAsync("ffmpeg could not be started.");
                return false;
            }

            var diagnostics = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                await error.WriteLineAsync($"ffmpeg failed: {diagnostics.Trim()}");
                return false;
            }

            return true;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            await error.WriteLineAsync(
                "ffmpeg is not on the path. Seeding writes real video files, because every lane " +
                "past the traversal reads them.");
            return false;
        }
    }

    private static async Task<string?> ClaimAsync(
        AccessService access,
        IServiceProvider scoped,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var authorization = await access.CreateBootstrapAuthorizationAsync(cancellationToken);

        if (!authorization.Created || authorization.DeliveryPath is null)
        {
            await error.WriteLineAsync($"The Bootstrap Authorization was refused: {authorization.Reason}");
            return null;
        }

        // The credential is written to a file and never returned, so that no caller can log it by
        // accident. Seeding is the one caller entitled to read it back.
        var credential = (await File.ReadAllTextAsync(authorization.DeliveryPath, cancellationToken))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();

        if (credential is null)
        {
            await error.WriteLineAsync("The Bootstrap Authorization file held no credential.");
            return null;
        }

        const string username = "admin";
        var claim = await access.ClaimAsync(
            credential,
            username,
            Password,
            "admin@example.invalid",
            cancellationToken);

        if (claim.Verdict != BootstrapClaimVerdict.Created)
        {
            await error.WriteLineAsync($"The installation could not be claimed: {claim.Verdict}");
            return null;
        }

        await output.WriteLineAsync($"Claimed the installation as “{username}”.");
        return username;
    }

    /// <summary>
    /// One Account in each state the Accounts screen has a row for, so that screen has something
    /// to be wrong about. A disabled Account is here because reinstating one was a one-way door
    /// until 0.5.0, which is the kind of gap an empty screen hides.
    /// </summary>
    private static async Task RegisterAccountsAsync(
        AccessService access,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        foreach (var username in new[] { "viewer", "waiting", "retired" })
        {
            await access.SubmitRegistrationRequestAsync(
                username,
                Password,
                $"{username}@example.invalid",
                cancellationToken);
        }

        var accounts = await access.ListAccountsAsync(cancellationToken);

        foreach (var account in accounts.Where(row => row.Username is "viewer" or "retired"))
        {
            await access.ApproveAsync(account.Id, cancellationToken);
        }

        foreach (var account in accounts.Where(row => row.Username == "retired"))
        {
            await access.DisableAsync(account.Id, cancellationToken);
        }

        await output.WriteLineAsync(
            "Registered three Accounts: one approved, one still waiting, one disabled.");
    }

    private static async Task<bool> ActivateLibraryAsync(
        IServiceProvider scoped,
        string mountRoot,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var configuration = scoped.GetRequiredService<InstallationConfigurationService>();
        var staged = await configuration.StageLibraryDirectoryAsync(
            "Films",
            Path.Combine(mountRoot, "films"),
            cancellationToken);

        if (staged.Verdict != LibraryDirectoryStageVerdict.Staged || staged.StageId is null)
        {
            await error.WriteLineAsync($"The Library Directory could not be staged: {staged.Verdict}");
            return false;
        }

        var activated = await configuration.ActivateLibraryDirectoryAsync(
            staged.StageId.Value,
            cancellationToken);

        if (activated.Verdict != LibraryDirectoryActivationVerdict.Activated)
        {
            await error.WriteLineAsync($"The Library Directory could not be activated: {activated.Verdict}");
            return false;
        }

        await output.WriteLineAsync("Activated “Films” as a Library Directory.");
        return true;
    }

    private static async Task RescanAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<Prdb.Viewer.Infrastructure.Persistence.ViewerDbContext>();
        var scheduler = scope.ServiceProvider.GetRequiredService<LibraryWorkScheduler>();

        foreach (var directory in database.LibraryDirectories.Select(row => row.Id).ToList())
        {
            await scheduler.QueueScanAsync(
                directory,
                BackgroundWorkTrigger.Administrator,
                cancellationToken);
        }
    }

    /// <summary>
    /// Drives the lanes the way the hosted workers do. Seeding runs them to a standstill rather
    /// than starting the application, so the installation is already settled when it is opened.
    /// </summary>
    private static async Task DrainAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        for (var pass = 0; pass < 200; pass++)
        {
            var advanced =
                await SliceAsync<LibraryScanRunner>(services, (runner, token) => runner.RunNextSliceAsync(token), cancellationToken) |
                await SliceAsync<TechnicalInspectionRunner>(services, (runner, token) => runner.RunNextSliceAsync(token), cancellationToken) |
                await SliceAsync<HashingRunner>(services, (runner, token) => runner.RunNextSliceAsync(token), cancellationToken) |
                await SliceAsync<PreviewGenerationRunner>(services, (runner, token) => runner.RunNextSliceAsync(token), cancellationToken) |
                await SliceAsync<IdentificationRunner>(services, (runner, token) => runner.RunNextSliceAsync(token), cancellationToken) |
                await SliceAsync<SiteRecognitionRunner>(services, (runner, token) => runner.RunNextSliceAsync(token), cancellationToken);

            if (!advanced)
            {
                return;
            }
        }
    }

    private static async Task<bool> SliceAsync<TRunner>(
        IServiceProvider services,
        Func<TRunner, CancellationToken, Task<bool>> slice,
        CancellationToken cancellationToken)
        where TRunner : notnull
    {
        var advanced = false;

        while (true)
        {
            await using var scope = services.CreateAsyncScope();

            if (!await slice(scope.ServiceProvider.GetRequiredService<TRunner>(), cancellationToken))
            {
                return advanced;
            }

            advanced = true;
        }
    }

    private static async Task ReportAsync(
        IServiceProvider services,
        string administrator,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var status = await scope.ServiceProvider
            .GetRequiredService<BackgroundWorkQuery>()
            .GetAsync(cancellationToken);
        var lanes = status.Work
            .GroupBy(work => work.Category)
            .Select(lane => lane.OrderByDescending(work => work.RequestedAt).First())
            .OrderBy(work => work.Category.ToString());

        await output.WriteLineAsync();
        await output.WriteLineAsync("Lanes, at the run each one would show:");

        foreach (var lane in lanes)
        {
            await output.WriteLineAsync(
                $"  {lane.Category,-20} {lane.State,-10} " +
                $"{lane.CompletedItemCount}/{lane.DiscoveredCandidateCount}");
        }

        if (status.Issues.Count > 0)
        {
            await output.WriteLineAsync($"Work Issues awaiting someone: {status.Issues.Count}.");
        }

        await output.WriteLineAsync();
        await output.WriteLineAsync($"Sign in as “{administrator}” with the password {Password}.");
        await output.WriteLineAsync(
            "The Videos are not identified: that needs a prdb credential, which this command " +
            "deliberately does not invent. Add one on the Installation screen and the " +
            "identification lanes will have something to do.");
    }
}
