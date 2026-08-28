using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Recovery;

/// <summary>
/// Everything a Backup Archive carries: every durable fact that cannot be reconstructed without
/// loss. Source Video Files, generated previews, cached artwork, active sessions, Bootstrap
/// Authorizations, Recovery Codes, and executable Background Work checkpoints are deliberately
/// absent — they are either externally authoritative or regenerable.
/// </summary>
public sealed class BackupDocument
{
    public required InstallationConfigurationRow InstallationConfiguration { get; init; }

    public required IReadOnlyList<AccountRow> Accounts { get; init; }

    public required IReadOnlyList<LibraryDirectoryRow> LibraryDirectories { get; init; }

    public required IReadOnlyList<VideoRow> Videos { get; init; }

    public required IReadOnlyList<VideoFileRow> VideoFiles { get; init; }

    public required IReadOnlyList<VideoMetadataRow> VideoMetadata { get; init; }

    public required IReadOnlyList<IdentificationClaimRow> IdentificationClaims { get; init; }

    public required IReadOnlyList<IdentificationCandidateRow> IdentificationCandidates { get; init; }

    public required IReadOnlyList<IdentificationDecisionRow> IdentificationDecisions { get; init; }

    public required IReadOnlyList<PersonalVideoStateRow> PersonalVideoStates { get; init; }

    public required IReadOnlyList<PlaybackAttemptRow> PlaybackAttempts { get; init; }

    public required IReadOnlyList<PlaybackReportRow> PlaybackReports { get; init; }

    public required IReadOnlyList<PlaybackAttemptVideoFileRow> PlaybackAttemptVideoFiles
    {
        get;
        init;
    }
}

/// <summary>
/// Serialises the durable rows themselves rather than a parallel set of transport records, so no
/// precious column can be forgotten when the model grows. Only value-typed columns travel;
/// navigation properties are dropped because their targets are already carried as their own
/// sections. Reading refuses unknown members, because a field this version cannot read might hold
/// state that must not be silently omitted.
/// </summary>
public static class BackupDocumentSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { DropNavigationProperties },
        },
    };

    public static byte[] Serialize(BackupDocument document) =>
        JsonSerializer.SerializeToUtf8Bytes(document, Options);

    public static BackupDocument? Deserialize(byte[] payload) =>
        JsonSerializer.Deserialize<BackupDocument>(payload, Options);

    private static void DropNavigationProperties(JsonTypeInfo type)
    {
        if (type.Type.Namespace != typeof(AccountRow).Namespace)
        {
            return;
        }

        for (var index = type.Properties.Count - 1; index >= 0; index--)
        {
            if (IsNavigation(type.Properties[index].PropertyType))
            {
                type.Properties.RemoveAt(index);
            }
        }
    }

    private static bool IsNavigation(Type property) =>
        property.Namespace == typeof(AccountRow).Namespace ||
        (property.IsGenericType &&
         property.GetGenericArguments()
             .Any(argument => argument.Namespace == typeof(AccountRow).Namespace));
}
