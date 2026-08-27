using Basis.Network.WebSocketServer;
using Xunit;

namespace BasisNetworkWebSocketServer.Tests;

public sealed class WebSocketServerTransportOptionsTests
{
    [Fact]
    public void Validate_AcceptsExplicitConfiguration()
    {
        WebSocketServerTransportOptions options = ValidOptions();

        options.Validate();

        Assert.True(options.IsOriginAllowed("https://basis.example"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("basis")]
    [InlineData("/basis/")]
    [InlineData("/basis?query=true")]
    [InlineData("/basis#fragment")]
    public void Validate_RejectsInvalidPath(string path)
    {
        WebSocketServerTransportOptions options = ValidOptions();
        options.Path = path;

        Assert.Throws<ArgumentException>(options.Validate);
    }

    [Fact]
    public void Validate_RejectsMissingAllowedOrigins()
    {
        WebSocketServerTransportOptions options = ValidOptions();
        options.AllowedOrigins.Clear();

        Assert.Throws<ArgumentException>(options.Validate);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("not-a-uri")]
    [InlineData("https://basis.example/path")]
    [InlineData("https://basis.example#fragment")]
    public void Validate_RejectsWildcardOrInvalidOrigin(string origin)
    {
        WebSocketServerTransportOptions options = ValidOptions();
        options.AllowedOrigins.Clear();
        options.AllowedOrigins.Add(origin);

        Assert.Throws<ArgumentException>(options.Validate);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Validate_RejectsMaximumPayloadShorterThanAcceptPayload(int maximumPayloadLength)
    {
        WebSocketServerTransportOptions options = ValidOptions();
        options.MaximumPayloadLength = maximumPayloadLength;

        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void Validate_RejectsNonPositivePendingSendCapacity(int pendingSendCapacity)
    {
        WebSocketServerTransportOptions options = ValidOptions();
        options.PendingSendCapacity = pendingSendCapacity;

        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }

    [Fact]
    public void Validate_RequiresCertificateForTlsEndpoint()
    {
        WebSocketServerTransportOptions options = ValidOptions();
        options.UseTls = true;

        Assert.Throws<ArgumentException>(options.Validate);
    }

    [Fact]
    public void Validate_RequiresCertificateKeyForTlsEndpoint()
    {
        WebSocketServerTransportOptions options = ValidOptions();
        options.UseTls = true;
        options.CertificatePath = "/run/certs/certificate.pem";

        Assert.Throws<ArgumentException>(options.Validate);
    }

    [Fact]
    public void IsOriginAllowed_RejectsUnknownOrMissingOrigin()
    {
        WebSocketServerTransportOptions options = ValidOptions();
        options.Validate();

        Assert.False(options.IsOriginAllowed(null));
        Assert.False(options.IsOriginAllowed("https://unknown.example"));
    }

    [Fact]
    public void FromConfiguration_MapsExplicitEndpointSettings()
    {
        Configuration configuration = new()
        {
            WebSocketPort = 8443,
            WebSocketPath = "/network",
            WebSocketMaximumPayloadLength = 2048,
            WebSocketPendingSendCapacity = 12,
            WebSocketAllowedOrigins = new[] { "https://basis.example" },
            WebSocketUseTls = false,
        };

        WebSocketServerTransportOptions options = WebSocketServerTransportOptions.FromConfiguration(configuration);

        Assert.Equal(8443, options.Port);
        Assert.Equal("/network", options.Path);
        Assert.Equal(2048, options.MaximumPayloadLength);
        Assert.Equal(12, options.PendingSendCapacity);
        Assert.True(options.IsOriginAllowed("https://basis.example"));
    }

    [Fact]
    public void FromConfiguration_MapsPemCertificateFiles()
    {
        Configuration configuration = new()
        {
            WebSocketPort = 443,
            WebSocketAllowedOrigins = new[] { "https://basis.example" },
            WebSocketUseTls = true,
            WebSocketCertificatePath = "/etc/letsencrypt/live/basis.example/fullchain.pem",
            WebSocketCertificateKeyPath = "/etc/letsencrypt/live/basis.example/privkey.pem",
        };

        WebSocketServerTransportOptions options = WebSocketServerTransportOptions.FromConfiguration(configuration);

        Assert.Equal(configuration.WebSocketCertificatePath, options.CertificatePath);
        Assert.Equal(configuration.WebSocketCertificateKeyPath, options.CertificateKeyPath);
    }

    private static WebSocketServerTransportOptions ValidOptions()
    {
        WebSocketServerTransportOptions options = new()
        {
            Port = 4297,
            Path = "/basis",
            MaximumPayloadLength = 1024,
            PendingSendCapacity = 8,
        };
        options.AllowedOrigins.Add("https://basis.example");
        return options;
    }
}
