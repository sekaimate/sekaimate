using Basis.Network.Core;
using Xunit;

namespace BasisNetworkCore.Tests;

public sealed class ClientConnectionTargetSelectorTests
{
    [Fact]
    public void Select_ReturnsNativeTargetUnchangedOutsideWebGl()
    {
        ConnectionTarget nativeTarget = new(BasisNetworkStackRegistry.LiteNetLibId, "server.example:4296");

        ConnectionTarget selected = ClientConnectionTargetSelector.Select(nativeTarget, string.Empty, false);

        Assert.Same(nativeTarget, selected);
    }

    [Fact]
    public void Select_UsesExplicitDirectoryWebSocketUriInWebGl()
    {
        ConnectionTarget nativeTarget = new(BasisNetworkStackRegistry.LiteNetLibId, "server.example:4296");

        ConnectionTarget selected = ClientConnectionTargetSelector.Select(
            nativeTarget,
            "wss://server.example:8443/basis?room=main",
            true);

        Assert.Equal(BasisNetworkStackRegistry.WebSocketId, selected.StackId);
        Assert.Equal("wss://server.example:8443/basis?room=main", selected.Raw);
        Assert.Equal("server.example", selected.Get(ConnectionTarget.Keys.Address));
        Assert.Equal("8443", selected.Get(ConnectionTarget.Keys.Port));
    }

    [Fact]
    public void Select_RejectsMissingWebSocketUriInWebGl()
    {
        ConnectionTarget nativeTarget = new(BasisNetworkStackRegistry.LiteNetLibId, "server.example:4296");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => ClientConnectionTargetSelector.Select(nativeTarget, string.Empty, true));

        Assert.Contains("WebSocket URI", exception.Message);
    }

    [Theory]
    [InlineData("ws://server.example/basis")]
    [InlineData("http://localhost/basis")]
    [InlineData("server.example:4296")]
    public void Select_RejectsUnsafeOrNonWebSocketUriInWebGl(string uri)
    {
        ConnectionTarget nativeTarget = new(BasisNetworkStackRegistry.LiteNetLibId, "server.example:4296");

        Assert.Throws<FormatException>(
            () => ClientConnectionTargetSelector.Select(nativeTarget, uri, true));
    }

    [Theory]
    [InlineData("ws://localhost/basis")]
    [InlineData("ws://127.0.0.1:8080/basis")]
    [InlineData("ws://[::1]:8080/basis")]
    public void Select_AllowsInsecureWebSocketOnlyForLoopbackDevelopment(string uri)
    {
        ConnectionTarget nativeTarget = new(BasisNetworkStackRegistry.LiteNetLibId, "localhost:4296");

        ConnectionTarget selected = ClientConnectionTargetSelector.Select(nativeTarget, uri, true);

        Assert.Equal(uri, selected.Raw);
    }
}
