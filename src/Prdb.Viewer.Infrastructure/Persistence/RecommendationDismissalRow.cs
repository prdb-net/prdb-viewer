namespace Prdb.Viewer.Infrastructure.Persistence;

/// <summary>
/// One Account's "not today" about one Video: a Temporary Dismissal that keeps the Video out of
/// every Recommendation Section for twenty-four hours from the moment it was made.
/// </summary>
/// <remarks>
/// It is not a preference and says nothing about the Video. It belongs to the Account rather than
/// to a client, so dismissing something on a phone also removes it from the television; and it is
/// stored as the moment rather than as an expiry so that undoing it is deleting a row rather than
/// reasoning about a deadline somebody has already passed.
/// </remarks>
public sealed class RecommendationDismissalRow
{
    public Guid AccountId { get; set; }

    public AccountRow Account { get; set; } = null!;

    public Guid VideoId { get; set; }

    public VideoRow Video { get; set; } = null!;

    public DateTime DismissedAt { get; set; }
}
