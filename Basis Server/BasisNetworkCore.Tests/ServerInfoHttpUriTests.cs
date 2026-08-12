using Basis.Network.Core;
using Xunit;

namespace BasisNetworkCore.Tests;

public sealed class ServerInfoHttpUriTests
{
    [Theory]
    [InlineData("https://server.example/basis/server-info")]
    [InlineData("http://localhost:4297/server-info")]
    [InlineData("http://127.0.0.1:4297/server-info")]
    [InlineData("http://[::1]:4297/server-info")]
    public void Parse_AcceptsSecureOrLoopbackEndpoint(string value)
    {
        Uri uri = ServerInfoHttpUri.Parse(value);

        Assert.Equal(value, uri.AbsoluteUri.TrimEnd('/'));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/server-info")]
    [InlineData("http://server.example/server-info")]
    [InlineData("ws://localhost:4297/server-info")]
    [InlineData("https://user@server.example/server-info")]
    [InlineData("https://server.example/server-info#fragment")]
    public void Parse_RejectsInvalidOrInsecureEndpoint(string value)
    {
        Assert.Throws<FormatException>(() => ServerInfoHttpUri.Parse(value));
    }
}
