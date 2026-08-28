using System.Globalization;
using System.Text;

namespace Prdb.Viewer.Core.Library;

/// <summary>
/// The facts an Installation Operator needs to change a deployment, mount, permission, storage, or
/// host condition. Everything here is safe to copy out of the application: no credential, no
/// Personal State, no stack trace, and no request to read container logs.
/// </summary>
public sealed record OperatorHandoffFacts(
    string Reference,
    WorkIssueSeverity Severity,
    WorkIssueCause Cause,
    BackgroundWorkCategory Category,
    string Phase,
    string AffectedScope,
    string? ContainerPath,
    string SafeCause,
    int OccurrenceCount,
    int AttemptedRetries,
    DateTimeOffset FirstOccurredAt,
    DateTimeOffset LastOccurredAt,
    string RequestedOperatorAction,
    string ExpectedResolutionEvidence);

/// <summary>
/// Composes the copyable Operator Handoff. It tells the operator which condition must change and
/// what the application must observe afterwards, rather than prescribing platform-specific shell
/// commands that belong in the deployment documentation.
/// </summary>
public static class OperatorHandoff
{
    public static string Compose(OperatorHandoffFacts facts)
    {
        var text = new StringBuilder();
        text.AppendLine("prdb-viewer operator handoff");
        text.AppendLine($"Reference: {facts.Reference}");
        text.AppendLine($"Severity: {Readable(facts.Severity.ToString())}");
        text.AppendLine($"Cause: {Readable(facts.Cause.ToString())}");
        text.AppendLine($"Work: {Readable(facts.Category.ToString())} · {facts.Phase}");
        text.AppendLine($"Affected scope: {facts.AffectedScope}");

        if (!string.IsNullOrEmpty(facts.ContainerPath))
        {
            text.AppendLine($"Container path: {facts.ContainerPath}");
        }

        text.AppendLine($"Observed cause: {facts.SafeCause}");
        text.AppendLine($"First occurrence: {Moment(facts.FirstOccurredAt)}");
        text.AppendLine($"Latest occurrence: {Moment(facts.LastOccurredAt)}");
        text.AppendLine(
            $"Occurrences: {facts.OccurrenceCount} · automatic retries: {facts.AttemptedRetries}");
        text.AppendLine($"Requested operator action: {facts.RequestedOperatorAction}");
        text.AppendLine($"Expected resolution evidence: {facts.ExpectedResolutionEvidence}");
        text.Append(
            "The application never guesses a host path. After the condition changes, an " +
            "Administrator selects Check again and the application verifies it.");

        return text.ToString();
    }

    private static string Moment(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

    /// <summary>Turns a PascalCase domain value into the words an operator reads.</summary>
    public static string Readable(string value)
    {
        var text = new StringBuilder(value.Length + 8);

        for (var index = 0; index < value.Length; index++)
        {
            if (index > 0 && char.IsUpper(value[index]))
            {
                text.Append(' ');
                text.Append(char.ToLowerInvariant(value[index]));
                continue;
            }

            text.Append(value[index]);
        }

        return text.ToString();
    }
}
