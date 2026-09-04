namespace Prdb.Viewer.Infrastructure.Persistence;

/// <summary>
/// One Account's private reference to an Actor.
/// </summary>
/// <remarks>
/// It is Personal State, and alone among the things this installation keeps about Actors it
/// belongs in a Backup Archive (ADR 0020): the profile beside it is regenerable from prdb, and
/// this is not. It references prdb's identifier rather than a local Actor row, which is what lets
/// it be restored before the profile it names has been fetched again.
/// </remarks>
public sealed class PersonalActorStateRow
{
    public Guid AccountId { get; set; }

    public AccountRow Account { get; set; } = null!;

    public required string PrdbActorId { get; set; }

    public DateTime FavouriteAddedAt { get; set; }
}
