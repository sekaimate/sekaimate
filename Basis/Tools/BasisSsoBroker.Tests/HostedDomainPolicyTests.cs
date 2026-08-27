using Xunit;

public sealed class HostedDomainPolicyTests
{
    [Fact]
    public void WildcardRequiresGoogleHostedDomainClaim()
    {
        Assert.True(HostedDomainPolicy.Matches("workspace.example", ["*"]));
        Assert.False(HostedDomainPolicy.Matches(null, ["*"]));
    }

    [Fact]
    public void SpecificDomainMatchesWithoutCaseSensitivity()
    {
        Assert.True(HostedDomainPolicy.Matches("Workspace.Example", ["workspace.example"]));
        Assert.False(HostedDomainPolicy.Matches("outside.example", ["workspace.example"]));
    }

    [Fact]
    public void AuthorizationHintUsesSpecificDomainOrWorkspaceWildcard()
    {
        Assert.Equal("workspace.example", HostedDomainPolicy.AuthorizationParameters(["workspace.example"])["hd"]);
        Assert.Equal("*", HostedDomainPolicy.AuthorizationParameters(["first.example", "second.example"])["hd"]);
        Assert.False(HostedDomainPolicy.AuthorizationParameters([]).ContainsKey("hd"));
    }
}
