using Prdb.Viewer.Infrastructure.Library;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

/// <summary>
/// A retained metadata document survives an upgrade untouched, so the two shapes of its actor
/// list are both current: the plain names written before Actors had an identity, and the credits
/// written since. Reading is where they meet.
/// </summary>
public sealed class RetainedActorsTests
{
    [Fact]
    public void A_document_written_before_actors_had_an_identity_reads_as_names()
    {
        var actors = RetainedActors.Deserialize("""["Alex Doe","Sam Roe"]""");

        Assert.Collection(
            actors,
            first =>
            {
                Assert.Equal("Alex Doe", first.Name);
                Assert.Null(first.ActorId);
            },
            second =>
            {
                Assert.Equal("Sam Roe", second.Name);
                Assert.Null(second.ActorId);
            });
    }

    [Fact]
    public void A_credit_carries_the_identity_prdb_sent_with_it()
    {
        var written = RetainedActors.Serialize(
            [new RetainedActor("Alex Doe", "6f1a2c34-0000-4000-8000-000000000009")]);

        Assert.Equal(
            [new RetainedActor("Alex Doe", "6f1a2c34-0000-4000-8000-000000000009")],
            RetainedActors.Deserialize(written));
    }

    [Fact]
    public void One_document_may_hold_both_shapes()
    {
        var actors = RetainedActors.Deserialize(
            """["Alex Doe",{"name":"Sam Roe","actorId":"6f1a2c34-0000-4000-8000-000000000009"}]""");

        Assert.Equal(
            [
                new RetainedActor("Alex Doe"),
                new RetainedActor("Sam Roe", "6f1a2c34-0000-4000-8000-000000000009"),
            ],
            actors);
    }

    [Fact]
    public void Nothing_worth_naming_is_nothing()
    {
        Assert.Empty(RetainedActors.Deserialize(null));
        Assert.Empty(RetainedActors.Deserialize(""));
        Assert.Empty(RetainedActors.Deserialize("not json"));
        Assert.Empty(RetainedActors.Deserialize("""{"actors":["Alex Doe"]}"""));
        Assert.Empty(RetainedActors.Deserialize("""["",{"actorId":"x"},{"name":" "},null,7]"""));
        Assert.Null(RetainedActors.Serialize([]));
    }

    [Fact]
    public void An_empty_identity_is_no_identity()
    {
        var actors = RetainedActors.Deserialize("""[{"name":"Alex Doe","actorId":""}]""");

        Assert.Null(Assert.Single(actors).ActorId);
    }

    [Fact]
    public void The_names_alone_are_what_a_surface_that_only_shows_names_reads()
    {
        Assert.Equal(
            ["Alex Doe", "Sam Roe"],
            RetainedActors.Names(
                """["Alex Doe",{"name":"Sam Roe","actorId":"6f1a2c34-0000-4000-8000-000000000009"}]"""));
    }
}
