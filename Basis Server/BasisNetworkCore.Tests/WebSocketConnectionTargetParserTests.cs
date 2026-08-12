using Basis.Network.Core;
using Xunit;

namespace BasisNetworkCore.Tests;

public sealed class WebSocketConnectionTargetParserTests
{
    private readonly WebSocketConnectionTargetParser _parser = new();

    [Theory]
    [InlineData("ws://localhost/basis", "ws", "localhost", "80", "/basis", "false")]
    [InlineData("wss://example.com:8443/basis?room=one", "wss", "example.com", "8443", "/basis?room=one", "true")]
    [InlineData("wss://[2001:db8::1]/basis", "wss", "2001:db8::1", "443", "/basis", "true")]
    public void Parse_StoresExplicitConnectionProperties(
        string raw, string scheme, string address, string port, string path, string secure)
    {
        ConnectionTarget target = new("websocket", raw);

        _parser.Parse(target);

        Assert.Equal(scheme, target.Get(ConnectionTarget.Keys.Scheme));
        Assert.Equal(address, target.Get(ConnectionTarget.Keys.Address));
        Assert.Equal(port, target.Get(ConnectionTarget.Keys.Port));
        Assert.Equal(path, target.Get(ConnectionTarget.Keys.Path));
        Assert.Equal(secure, target.Get(ConnectionTarget.Keys.Secure));
    }

    [Theory]
    [InlineData("")]
    [InlineData("example.com:4296")]
    [InlineData("http://example.com/basis")]
    [InlineData("https://example.com/basis")]
    [InlineData("ftp://example.com/basis")]
    [InlineData("wss:///basis")]
    [InlineData("wss://user@example.com/basis")]
    public void Parse_RejectsInvalidOrUnsupportedTarget(string raw)
    {
        ConnectionTarget target = new("websocket", raw);

        Assert.Throws<FormatException>(() => _parser.Parse(target));
    }

    [Fact]
    public void Parse_RequiresSecureWebSocketOutsideLoopback()
    {
        ConnectionTarget target = new("websocket", "ws://example.com/basis");

        FormatException exception = Assert.Throws<FormatException>(() => _parser.Parse(target));

        Assert.Contains("wss", exception.Message);
        Assert.Contains("loopback", exception.Message);
    }

    [Theory]
    [InlineData("wss://example.com/basis#password")]
    [InlineData("wss://example.com/basis#")]
    public void Parse_RejectsUriFragment(string raw)
    {
        ConnectionTarget target = new("websocket", raw);

        Assert.Throws<FormatException>(() => _parser.Parse(target));
    }

    [Theory]
    [InlineData("ws://localhost/basis")]
    [InlineData("wss://example.com:8443/basis?room=one")]
    [InlineData("wss://[2001:db8::1]/basis")]
    public void Format_RoundTripsParsedTarget(string raw)
    {
        ConnectionTarget target = new("websocket", raw);
        _parser.Parse(target);

        Assert.Equal(raw, _parser.Format(target));
    }
}
