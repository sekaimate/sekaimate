using Xunit;

public sealed class WebSocketServerConfigurationTests
{
    [Fact]
    public void Defaults_KeepWebSocketListenerDisabled()
    {
        Configuration configuration = new();

        Assert.False(configuration.WebSocketEnabled);
        Assert.Equal((ushort)4297, configuration.WebSocketPort);
        Assert.Equal("/basis", configuration.WebSocketPath);
        Assert.Equal("/server-info", configuration.WebSocketServerInfoPath);
        Assert.Equal(1024 * 1024, configuration.WebSocketMaximumPayloadLength);
        Assert.Equal(64, configuration.WebSocketPendingSendCapacity);
        Assert.True(configuration.WebSocketUseTls);
        Assert.Equal(string.Empty, configuration.WebSocketCertificatePath);
        Assert.Empty(configuration.WebSocketAllowedOrigins);
    }
}
