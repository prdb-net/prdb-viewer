using Prdb.Viewer.Infrastructure.Library;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

/// <summary>
/// The backlog flattened back into the cases it holds, for tests that are about one case rather
/// than about the shape a large library is reviewed from.
/// </summary>
internal static class ReviewQueue
{
    public static async Task<IReadOnlyList<IdentificationQueueItem>> QueueAsync(
        this IdentificationReviewService review,
        CancellationToken cancellationToken) =>
        (await review.GetQueueAsync(
                new IdentificationQueueRequest { Take = 50 },
                cancellationToken))
            .Groups
            .SelectMany(group => group.Cases)
            .ToArray();
}
