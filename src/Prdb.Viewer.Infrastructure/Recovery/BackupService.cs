using System.Reflection;

using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Access;
using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Core.Recovery;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Recovery;

public sealed record BackupCreationResult(
    bool Created,
    string? DestinationPath = null,
    DateTimeOffset? CreatedAt = null,
    int? FormatVersion = null,
    string? ProductVersion = null,
    string? Reason = null);

public sealed record BackupValidationResult(
    bool Valid,
    BackupArchiveHeader? Header = null,
    BackupValidationFailure? Failure = null,
    string? Reason = null);

public enum RestoreVerdict
{
    Restored,
    TargetNotEmpty,
    ArchiveRejected,
    ArchiveUnreadable,
}

public sealed record RestoreResult(
    RestoreVerdict Verdict,
    BackupArchiveHeader? Header = null,
    string? Reason = null,
    int Accounts = 0,
    int Videos = 0,
    int VideoFiles = 0,
    bool BackgroundWorkPaused = false);

/// <summary>
/// Creates, validates, and restores the Backup Archive. It never reads, copies, or mutates Source
/// Video Files, never writes an archive that has not validated, and only ever restores into an
/// empty, unclaimed application state.
/// </summary>
public sealed class BackupService(
    ViewerDbContext database,
    LibraryWorkScheduler scheduler,
    TimeProvider timeProvider)
{
    public static string ProductVersion =>
        typeof(BackupService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";

    public async Task<BackupCreationResult> CreateAsync(
        string destinationPath,
        string passphrase,
        CancellationToken cancellationToken = default)
    {
        var destination = Path.GetFullPath(destinationPath);

        if (File.Exists(destination) || Directory.Exists(destination))
        {
            return new BackupCreationResult(
                false,
                Reason: $"{destination} already exists. Backup never overwrites a file.");
        }

        var createdAt = timeProvider.GetUtcNow();
        var document = await ReadConsistentSnapshotAsync(cancellationToken);
        var archive = BackupArchive.Write(document, passphrase, ProductVersion, createdAt);

        // The archive is opened and revalidated before it is published, so a file that could be
        // mistaken for a successful Backup Archive is never left behind.
        var opened = BackupArchive.Read(archive, passphrase);
        var validation = Validate(opened);

        if (!validation.Valid)
        {
            return new BackupCreationResult(false, Reason: validation.Reason);
        }

        var staged = $"{destination}.partial";

        try
        {
            await WriteOwnerOnlyAsync(staged, archive, cancellationToken);
            File.Move(staged, destination);
        }
        catch (PlatformNotSupportedException)
        {
            Delete(staged);
            return new BackupCreationResult(
                false,
                Reason: "The destination cannot be given owner-only permissions on this platform.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Delete(staged);
            return new BackupCreationResult(
                false,
                Reason: $"The archive could not be written to {destination}.");
        }

        return new BackupCreationResult(
            true,
            destination,
            createdAt,
            BackupArchiveFormat.CurrentVersion,
            ProductVersion);
    }

    public async Task<BackupValidationResult> ValidateAsync(
        string archivePath,
        string passphrase,
        CancellationToken cancellationToken = default)
    {
        byte[] archive;

        try
        {
            archive = await File.ReadAllBytesAsync(archivePath, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new BackupValidationResult(
                false,
                Failure: BackupValidationFailure.NotAnArchive,
                Reason: $"{archivePath} could not be read.");
        }

        return Validate(BackupArchive.Read(archive, passphrase));
    }

    /// <summary>
    /// Activates an archive into an empty target. Validation is repeated here rather than trusting
    /// an earlier result, and every failure happens before activation, so the original archive and
    /// the empty target both remain reusable.
    /// </summary>
    public async Task<RestoreResult> RestoreAsync(
        string archivePath,
        string passphrase,
        CancellationToken cancellationToken = default)
    {
        if (!await IsEmptyAsync(cancellationToken))
        {
            return new RestoreResult(
                RestoreVerdict.TargetNotEmpty,
                Reason: "Restore requires an empty, unclaimed application state. Stop the " +
                        "installation, move its application data aside as a fallback, and point " +
                        "Restore at a fresh data directory.");
        }

        byte[] archive;

        try
        {
            archive = await File.ReadAllBytesAsync(archivePath, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new RestoreResult(
                RestoreVerdict.ArchiveUnreadable,
                Reason: $"{archivePath} could not be read.");
        }

        var opened = BackupArchive.Read(archive, passphrase);
        var validation = Validate(opened);

        if (!validation.Valid)
        {
            return new RestoreResult(
                RestoreVerdict.ArchiveRejected,
                opened.Header,
                validation.Reason);
        }

        var document = opened.Document!;
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        Activate(document);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new RestoreResult(
            RestoreVerdict.Restored,
            opened.Header,
            Accounts: document.Accounts.Count,
            Videos: document.Videos.Count,
            VideoFiles: document.VideoFiles.Count,
            BackgroundWorkPaused: document.InstallationConfiguration.BackgroundWorkPaused);
    }

    /// <summary>
    /// Reads one logically consistent committed point. A single read transaction means concurrent
    /// mutations fall wholly before or after it, so the archive can never carry a torn state.
    /// </summary>
    private async Task<BackupDocument> ReadConsistentSnapshotAsync(
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var document = new BackupDocument
        {
            InstallationConfiguration = await database.InstallationConfigurations
                .AsNoTracking()
                .SingleAsync(cancellationToken),
            Accounts = await database.Accounts.AsNoTracking().ToListAsync(cancellationToken),
            LibraryDirectories = await database.LibraryDirectories
                .AsNoTracking()
                .ToListAsync(cancellationToken),
            Videos = await database.Videos.AsNoTracking().ToListAsync(cancellationToken),
            VideoFiles = await database.VideoFiles.AsNoTracking().ToListAsync(cancellationToken),
            VideoMetadata = await database.VideoMetadata
                .AsNoTracking()
                .ToListAsync(cancellationToken),
            IdentificationClaims = await database.IdentificationClaims
                .AsNoTracking()
                .ToListAsync(cancellationToken),
            IdentificationCandidates = await database.IdentificationCandidates
                .AsNoTracking()
                .ToListAsync(cancellationToken),
            IdentificationDecisions = await database.IdentificationDecisions
                .AsNoTracking()
                .ToListAsync(cancellationToken),
            PersonalVideoStates = await database.PersonalVideoStates
                .AsNoTracking()
                .ToListAsync(cancellationToken),
            PlaybackAttempts = await database.PlaybackAttempts
                .AsNoTracking()
                .ToListAsync(cancellationToken),
            PlaybackReports = await database.PlaybackReports
                .AsNoTracking()
                .ToListAsync(cancellationToken),
            PlaybackAttemptVideoFiles = await database.PlaybackAttemptVideoFiles
                .AsNoTracking()
                .ToListAsync(cancellationToken),
        };
        await transaction.CommitAsync(cancellationToken);
        return document;
    }

    /// <summary>
    /// The domain invariants an archive must satisfy before it can be activated without loss. A
    /// successful result establishes that the archive is internally restorable by this product
    /// version; it claims nothing about mounts, prdb.net, or any other current dependency.
    /// </summary>
    private static BackupValidationResult Validate(BackupArchiveOpenResult opened)
    {
        if (!opened.Opened)
        {
            return new BackupValidationResult(
                false,
                opened.Header,
                opened.Failure,
                opened.Reason);
        }

        var document = opened.Document!;

        if (!document.Accounts.Any(account =>
                account.Authority == AccountAuthority.Administrator &&
                account.State == AccountState.Approved))
        {
            return Rejected(
                opened,
                BackupValidationFailure.NoActiveAdministrator,
                "The archive contains no active Administrator, so activating it would leave the " +
                "installation unreachable.");
        }

        var accounts = document.Accounts.Select(account => account.Id).ToHashSet();
        var videos = document.Videos.Select(video => video.Id).ToHashSet();
        var directories = document.LibraryDirectories.Select(directory => directory.Id).ToHashSet();
        var videoFiles = document.VideoFiles.Select(file => file.Id).ToHashSet();
        var attempts = document.PlaybackAttempts.Select(attempt => attempt.Id).ToHashSet();

        var broken =
            document.VideoFiles.Any(file =>
                !videos.Contains(file.VideoId) ||
                !directories.Contains(file.LibraryDirectoryId)) ||
            document.VideoMetadata.Any(metadata => !videos.Contains(metadata.VideoId)) ||
            document.IdentificationClaims.Any(claim => !videos.Contains(claim.VideoId)) ||
            document.IdentificationCandidates.Any(candidate => !videos.Contains(candidate.VideoId)) ||
            document.PersonalVideoStates.Any(state =>
                !accounts.Contains(state.AccountId) || !videos.Contains(state.VideoId)) ||
            document.PlaybackAttempts.Any(attempt =>
                !accounts.Contains(attempt.AccountId) || !videos.Contains(attempt.VideoId)) ||
            document.PlaybackReports.Any(report => !attempts.Contains(report.PlaybackAttemptId)) ||
            document.PlaybackAttemptVideoFiles.Any(participation =>
                !attempts.Contains(participation.PlaybackAttemptId) ||
                !videoFiles.Contains(participation.VideoFileId)) ||
            document.Videos.Any(video =>
                video.SurvivingVideoId is { } surviving && !videos.Contains(surviving));

        return broken
            ? Rejected(
                opened,
                BackupValidationFailure.BrokenReference,
                "The archive refers to state it does not contain, so activating it would lose " +
                "identity, provenance, or Personal State.")
            : new BackupValidationResult(true, opened.Header);
    }

    private static BackupValidationResult Rejected(
        BackupArchiveOpenResult opened,
        BackupValidationFailure failure,
        string reason) =>
        new(false, opened.Header, failure, reason);

    private Task<bool> IsEmptyAsync(CancellationToken cancellationToken) =>
        AllEmptyAsync(cancellationToken);

    private async Task<bool> AllEmptyAsync(CancellationToken cancellationToken) =>
        !await database.Accounts.AnyAsync(cancellationToken) &&
        !await database.LibraryDirectories.AnyAsync(cancellationToken) &&
        !await database.Videos.AnyAsync(cancellationToken) &&
        !await database.VideoFiles.AnyAsync(cancellationToken) &&
        !await database.PersonalVideoStates.AnyAsync(cancellationToken) &&
        !await database.BackgroundWork.AnyAsync(cancellationToken);

    /// <summary>
    /// Stages every restored row and re-establishes the facts that are current observations rather
    /// than history: availability becomes Unreachable until a scan proves otherwise, previews are
    /// dropped and rebuilt, and a restored credential is reverified before it may claim Verified.
    /// </summary>
    private void Activate(BackupDocument document)
    {
        var configuration = document.InstallationConfiguration;
        configuration.Id = InstallationConfigurationRow.TheOnlyRow;
        configuration.PendingPrdbCredential = null;
        configuration.PendingCredentialRevision = null;
        configuration.PrdbConnectionStatus = configuration.ActivePrdbCredential is null
            ? PrdbConnectionStatus.Missing
            : PrdbConnectionStatus.VerificationPending;
        configuration.LastConnectionIssue = null;
        database.InstallationConfigurations
            .Where(row => row.Id == InstallationConfigurationRow.TheOnlyRow)
            .ExecuteDelete();
        database.InstallationConfigurations.Add(configuration);
        database.Accounts.AddRange(document.Accounts);
        database.LibraryDirectories.AddRange(document.LibraryDirectories);
        database.Videos.AddRange(document.Videos);

        foreach (var file in document.VideoFiles)
        {
            if (file.Availability is not (VideoFileAvailability.Removed
                or VideoFileAvailability.Replaced))
            {
                file.Availability = VideoFileAvailability.Unreachable;
                file.ConsecutiveCompleteAbsences = 0;
            }

            file.PreviewState = VideoFilePreviewState.Pending;
            file.PreviewRelativePath = null;
            file.PreviewSha256 = null;
            file.PublicPreviewId = null;
            file.PreviewGeneratedAt = null;
        }

        database.VideoFiles.AddRange(document.VideoFiles);
        database.VideoMetadata.AddRange(document.VideoMetadata);
        database.IdentificationClaims.AddRange(document.IdentificationClaims);
        database.IdentificationCandidates.AddRange(document.IdentificationCandidates);
        database.IdentificationDecisions.AddRange(document.IdentificationDecisions);
        database.PersonalVideoStates.AddRange(document.PersonalVideoStates);
        database.PlaybackAttempts.AddRange(document.PlaybackAttempts);
        database.PlaybackReports.AddRange(document.PlaybackReports);
        database.PlaybackAttemptVideoFiles.AddRange(document.PlaybackAttemptVideoFiles);

        // No old run is resumed across installations. The work that is needed is derived again
        // from the restored durable state, and an installation-wide pause keeps it parked.
        var now = timeProvider.GetUtcNow().UtcDateTime;

        foreach (var directory in document.LibraryDirectories
                     .Where(directory => directory.State == LibraryDirectoryState.Active))
        {
            directory.Health = LibraryDirectoryHealth.Unreachable;
            scheduler.QueueInitialScan(directory, now);
        }
    }

    private static async Task WriteOwnerOnlyAsync(
        string path,
        byte[] archive,
        CancellationToken cancellationToken)
    {
        Delete(path);
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }
        else
        {
            throw new PlatformNotSupportedException(
                "Owner-only archive permissions are only available on Unix-like hosts.");
        }

        await using var stream = new FileStream(path, options);
        await stream.WriteAsync(archive, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
