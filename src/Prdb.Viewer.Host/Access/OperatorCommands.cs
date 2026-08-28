using Prdb.Viewer.Infrastructure.Access;
using Prdb.Viewer.Infrastructure.Recovery;

namespace Prdb.Viewer.Host.Access;

public static class OperatorCommands
{
    private const string Bootstrap = "bootstrap-authorize";
    private const string RecoverAdministrator = "recover-administrator";
    private const string Backup = "backup";
    private const string ValidateBackup = "validate-backup";
    private const string Restore = "restore";

    private const string Usage =
        "Usage: bootstrap-authorize | recover-administrator <username> | backup <destination> | " +
        "validate-backup <archive> | restore <archive>";

    public static bool Matches(string[] arguments) =>
        arguments.Length > 0 &&
        arguments[0] is Bootstrap or RecoverAdministrator or Backup or ValidateBackup or Restore;

    public static async Task<int> RunAsync(
        string[] arguments,
        AccessService access,
        BackupService backup,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        switch (arguments)
        {
            case [Bootstrap]:
                return Report(
                    await access.CreateBootstrapAuthorizationAsync(cancellationToken),
                    output,
                    error);

            case [RecoverAdministrator, var username]:
                return Report(
                    await access.IssueAdministratorRecoveryCodeAsync(username, cancellationToken),
                    output,
                    error);

            case [Backup, var destination]:
                return await CreateAsync(backup, destination, output, error, cancellationToken);

            case [ValidateBackup, var archive]:
                return await ValidateAsync(backup, archive, output, error, cancellationToken);

            case [Restore, var archive]:
                return await RestoreAsync(backup, archive, output, error, cancellationToken);

            default:
                await error.WriteLineAsync(Usage);
                return 64;
        }
    }

    private static async Task<int> CreateAsync(
        BackupService backup,
        string destination,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var passphrase = PassphraseConsole.Read(
            "Choose a passphrase for the Backup Archive: ",
            error,
            confirm: true);

        if (passphrase is null)
        {
            return 1;
        }

        if (passphrase.Length < PassphraseConsole.MinimumLength)
        {
            await error.WriteLineAsync(
                $"The passphrase must be at least {PassphraseConsole.MinimumLength} characters. " +
                "It cannot be recovered by the product, so keep it somewhere safe.");
            return 1;
        }

        var result = await backup.CreateAsync(destination, passphrase, cancellationToken);

        if (!result.Created)
        {
            await error.WriteLineAsync(result.Reason);
            return 1;
        }

        await output.WriteLineAsync($"Backup Archive written to {result.DestinationPath}.");
        await output.WriteLineAsync(
            $"Created {result.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC · " +
            $"format {result.FormatVersion} · product {result.ProductVersion} · validated.");
        await output.WriteLineAsync(
            "Source Video Files were not read, copied, or changed. The passphrase is not stored " +
            "anywhere and cannot be recovered.");
        return 0;
    }

    private static async Task<int> ValidateAsync(
        BackupService backup,
        string archive,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var passphrase = PassphraseConsole.Read("Backup Archive passphrase: ", error);

        if (passphrase is null)
        {
            return 1;
        }

        var result = await backup.ValidateAsync(archive, passphrase, cancellationToken);

        if (!result.Valid)
        {
            await error.WriteLineAsync(result.Reason);
            return 1;
        }

        await output.WriteLineAsync(
            $"{archive} is a valid Backup Archive · format {result.Header!.FormatVersion} · " +
            $"product {result.Header.ProductVersion} · " +
            $"created {result.Header.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC.");
        await output.WriteLineAsync(
            "It is internally restorable by this version. That says nothing about whether video " +
            "mounts or prdb.net are currently available.");
        return 0;
    }

    private static async Task<int> RestoreAsync(
        BackupService backup,
        string archive,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var passphrase = PassphraseConsole.Read("Backup Archive passphrase: ", error);

        if (passphrase is null)
        {
            return 1;
        }

        var result = await backup.RestoreAsync(archive, passphrase, cancellationToken);

        if (result.Verdict != RestoreVerdict.Restored)
        {
            await error.WriteLineAsync(result.Reason);
            await error.WriteLineAsync(
                "Neither the archive nor the target was changed, so both remain usable once the " +
                "cause is corrected.");
            return 1;
        }

        await output.WriteLineAsync(
            $"Restored {result.Accounts} Accounts, {result.Videos} Videos, and " +
            $"{result.VideoFiles} Video Files from a format {result.Header!.FormatVersion} archive.");
        await output.WriteLineAsync(
            "Every earlier session, Bootstrap Authorization, and Recovery Code is invalid, so " +
            "Users sign in again. Video Files stay Unreachable until a Library Scan observes " +
            "them, previews are generated again, and the prdb credential is reverified.");

        if (result.BackgroundWorkPaused)
        {
            await output.WriteLineAsync(
                "Background work was paused when the archive was taken and remains paused until " +
                "an Administrator resumes it.");
        }

        return 0;
    }

    private static int Report(
        OperatorCredentialResult result,
        TextWriter output,
        TextWriter error)
    {
        if (!result.Created)
        {
            error.WriteLine(result.Reason);
            return 1;
        }

        output.WriteLine($"The single-use credential was written to {result.DeliveryPath}.");
        output.WriteLine("Its value is never written to logs or command output.");
        return 0;
    }
}
