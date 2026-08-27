using Prdb.Viewer.Core.Access;

using Xunit;

namespace Prdb.Viewer.Core.Tests.Access;

public sealed class AccessRuleTests
{
    [Theory]
    [InlineData("viewer")]
    [InlineData("prdb.user")]
    [InlineData("user_name-2")]
    public void Accepted_usernames_are_stable_identifiers(string username)
    {
        Assert.True(UsernameRule.IsValid(username));
    }

    [Theory]
    [InlineData("ab")]
    [InlineData(" leading")]
    [InlineData("trailing-")]
    [InlineData("contains space")]
    public void Ambiguous_usernames_are_rejected(string username)
    {
        Assert.False(UsernameRule.IsValid(username));
    }

    [Fact]
    public void Username_uniqueness_is_case_insensitive()
    {
        Assert.Equal(UsernameRule.Normalize("Viewer"), UsernameRule.Normalize("viewer"));
    }

    [Theory]
    [InlineData("correct horse battery staple")]
    [InlineData("twelve-chars")]
    public void Long_passwords_are_accepted_without_composition_rules(string password)
    {
        Assert.True(PasswordRule.IsValid(password));
    }

    [Theory]
    [InlineData("too-short")]
    [InlineData("contains\ncontrol")]
    public void Weak_or_control_bearing_passwords_are_rejected(string password)
    {
        Assert.False(PasswordRule.IsValid(password));
    }
}
