using Prdb.Viewer.Core.Library;

using Xunit;

namespace Prdb.Viewer.Core.Tests.Library;

/// <summary>
/// prdb sends its enumerations with a label rather than as a number, which is why nothing in this
/// application translates one. The catch is the value it uses for "no answer": the label is the
/// word "Unknown", so a field prdb has nothing for arrives looking exactly like one it does.
/// </summary>
public sealed class ActorFactsTests
{
    [Theory]
    [InlineData("Unknown")]
    [InlineData("unknown")]
    [InlineData("  Unknown  ")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_label_that_says_nothing_is_nothing(string? label)
    {
        // "Nationality — Unknown" is a row that takes the place of one that would say something.
        Assert.Null(ActorFacts.Stated(label));
    }

    [Theory]
    [InlineData("Female", "Female")]
    [InlineData("  Brown  ", "Brown")]
    [InlineData("Not Applicable", "Not Applicable")]
    [InlineData("Unknown Origin", "Unknown Origin")]
    public void Anything_prdb_actually_said_is_kept(string label, string expected)
    {
        // Only the word on its own is the absence. A label that merely contains it is a label.
        Assert.Equal(expected, ActorFacts.Stated(label));
    }
}
