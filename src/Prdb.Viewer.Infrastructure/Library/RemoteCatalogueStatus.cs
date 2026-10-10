using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

public sealed record RemoteCatalogueStatus(RemoteCatalogueState State, string PrdbVideoId,
    DateTimeOffset? UpdatedAt, DateTimeOffset? DeletedAt, string? MergedIntoId)
{
    internal static RemoteCatalogueStatus? Of(VideoMetadataRow? row) => row is null ? null : new(
        row.RemoteState, row.PrdbVideoId, VideoPresentation.AsOffset(row.RemoteUpdatedAt),
        VideoPresentation.AsOffset(row.RemoteDeletedAt), row.RemoteMergedIntoId);

    internal static RemoteCatalogueStatus Of(ProposedWorkRow row) => new(
        row.RemoteState, row.PrdbVideoId, VideoPresentation.AsOffset(row.RemoteUpdatedAt),
        VideoPresentation.AsOffset(row.RemoteDeletedAt), row.RemoteMergedIntoId);

    internal static bool Withdrawn(ProposedWorkRow? row) => row?.RemoteState is RemoteCatalogueState.Deleted or RemoteCatalogueState.Merged;
    internal const string WithdrawalReason = "prdb has withdrawn this proposed work. Review its retained facts or assign a current work explicitly.";
}
