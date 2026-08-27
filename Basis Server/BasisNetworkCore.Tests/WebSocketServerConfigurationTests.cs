using Xunit;

public sealed class WebSocketServerConfigurationTests
{
    [Fact]
    public void Defaults_EnableWebSocketAlongsideUdp()
    {
        Configuration configuration = new();

        Assert.True(configuration.WebSocketEnabled);
        Assert.Equal((ushort)4297, configuration.WebSocketPort);
        Assert.Equal("/basis", configuration.WebSocketPath);
        Assert.Equal("/server-info", configuration.WebSocketServerInfoPath);
        Assert.Equal(1024 * 1024, configuration.WebSocketMaximumPayloadLength);
        Assert.Equal(64, configuration.WebSocketPendingSendCapacity);
        Assert.False(configuration.WebSocketUseTls);
        Assert.Equal(string.Empty, configuration.WebSocketCertificatePath);
        Assert.Equal(string.Empty, configuration.WebSocketCertificateKeyPath);
        Assert.Equal(
            new[] { "http://127.0.0.1:4173", "http://localhost:4173" },
            configuration.WebSocketAllowedOrigins);
    }

    [Fact]
    public void EnvironmentalOverrides_ParseAllowedOrigins()
    {
        const string variableName = nameof(Configuration.WebSocketAllowedOrigins);
        string? originalValue = Environment.GetEnvironmentVariable(variableName);
        try
        {
            Environment.SetEnvironmentVariable(
                variableName,
                "http://127.0.0.1:4173, http://localhost:4173");
            Configuration configuration = new();

            configuration.ProcessEnvironmentalOverrides();

            Assert.Equal(
                new[] { "http://127.0.0.1:4173", "http://localhost:4173" },
                configuration.WebSocketAllowedOrigins);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, originalValue);
        }
    }
}
