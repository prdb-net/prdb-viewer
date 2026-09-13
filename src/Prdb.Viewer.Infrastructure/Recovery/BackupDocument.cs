using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Recovery;

/// <summary>
/// Everything a Backup Archive carries: every durable fact that cannot be reconstructed without
/// loss. Source Video Files, generated previews, cached artwork, the retained Site Directory,
/// active sessions, Bootstrap Authorizations, Recovery Codes, and executable Background Work
/// checkpoints are deliberately absent — they are either externally authoritative or regenerable.
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

    /// <summary>
    /// Each Account's Favourite Actors. It is the one thing this installation keeps about Actors
    /// that a Backup Archive carries: the profile beside it is regenerable from prdb and this is
    /// not (ADR 0020). Absent from a format 1 archive, which is why it is not required.
    /// </summary>
    public IReadOnlyList<PersonalActorStateRow> PersonalActorStates { get; init; } = [];

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
/// state that must not be silently omitted — which is exactly why a column the product has
/// deliberately retired must be named here as retired rather than simply deleted.
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
            Modifiers = { DropNavigationProperties, DiscardRetiredMembers },
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

    /// <summary>
    /// The columns older archives carry that this product version has deliberately stopped
    /// keeping. Each is read and thrown away, so a supported restore neither fails on a member it
    /// refuses to guess at nor quietly resurrects state a decision removed. Nothing is written
    /// back: an archive this version produces does not carry them at all.
    /// </summary>
    /// <remarks>
    /// `personalRating` is the one-to-five score ADR 0022 discarded in favour of the Personal
    /// Reaction. There is no mapping, by decision: three stars is not a shrug.
    /// </remarks>
    private static void DiscardRetiredMembers(JsonTypeInfo type)
    {
        if (type.Type != typeof(PersonalVideoStateRow))
        {
            return;
        }

        var retired = type.CreateJsonPropertyInfo(typeof(int?), "personalRating");
        retired.Get = _ => null;
        retired.Set = (_, _) => { };
        retired.ShouldSerialize = (_, _) => false;
        type.Properties.Add(retired);
    }

    private static bool IsNavigation(Type property) =>
        property.Namespace == typeof(AccountRow).Namespace ||
        (property.IsGenericType &&
         property.GetGenericArguments()
             .Any(argument => argument.Namespace == typeof(AccountRow).Namespace));
}
