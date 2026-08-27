using Prdb.Viewer.Infrastructure.Access;

namespace Prdb.Viewer.Host.Access;

public static class OperatorCommands
{
    private const string Bootstrap = "bootstrap-authorize";
    private const string RecoverAdministrator = "recover-administrator";

    public static bool Matches(string[] arguments) =>
        arguments.Length > 0 && arguments[0] is Bootstrap or RecoverAdministrator;

    public static async Task<int> RunAsync(
        string[] arguments,
        AccessService access,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        if (arguments is [Bootstrap])
        {
            var result = await access.CreateBootstrapAuthorizationAsync(cancellationToken);
            return Report(result, output, error);
        }

        if (arguments is [RecoverAdministrator, var username])
        {
            var result = await access.IssueAdministratorRecoveryCodeAsync(username, cancellationToken);
            return Report(result, output, error);
        }

        await error.WriteLineAsync(
            "Usage: bootstrap-authorize | recover-administrator <username>");
        return 64;
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
