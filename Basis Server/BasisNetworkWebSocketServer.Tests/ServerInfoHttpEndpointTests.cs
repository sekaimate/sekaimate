using System.Text.Json;
using Basis.Network.WebSocketServer;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace BasisNetworkWebSocketServer.Tests;

public sealed class ServerInfoHttpEndpointTests
{
    [Fact]
    public async Task HandleRequestAsync_ReturnsSnapshotAndFixedCorsOrigin()
    {
        ServerInfoHttpEndpointOptions options = ValidOptions();
        DefaultHttpContext context = new();
        context.Request.Method = HttpMethods.Get;
        context.Request.Headers.Origin = "https://app.example";
        context.Response.Body = new MemoryStream();

        await ServerInfoHttpEndpoint.HandleRequestAsync(
            context,
            options,
            () => new ServerInfoSnapshot(2, 16, 1, "Basis", "Hello"));

        context.Response.Body.Position = 0;
        using JsonDocument json = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("https://app.example", context.Response.Headers.AccessControlAllowOrigin);
        Assert.Equal(2, json.RootElement.GetProperty("online").GetInt32());
        Assert.Equal("Basis", json.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task HandleRequestAsync_RejectsUnknownOrigin()
    {
        ServerInfoHttpEndpointOptions options = ValidOptions();
        DefaultHttpContext context = new();
        context.Request.Method = HttpMethods.Get;
        context.Request.Headers.Origin = "https://unknown.example";

        await ServerInfoHttpEndpoint.HandleRequestAsync(
            context,
            options,
            () => new ServerInfoSnapshot(0, 16, 1, "Basis", string.Empty));

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("Access-Control-Allow-Origin"));
    }

    [Fact]
    public void Validate_RejectsWildcardOrigin()
    {
        ServerInfoHttpEndpointOptions options = ValidOptions();
        options.AllowedOrigins.Clear();
        options.AllowedOrigins.Add("*");

        Assert.Throws<ArgumentException>(options.Validate);
    }

    [Fact]
    public void FromConfiguration_MapsExplicitPathAndOrigins()
    {
        Configuration configuration = new()
        {
            WebSocketServerInfoPath = "/status/server-info",
            WebSocketAllowedOrigins = new[] { "https://app.example" },
        };

        ServerInfoHttpEndpointOptions options = ServerInfoHttpEndpointOptions.FromConfiguration(configuration);

        Assert.Equal("/status/server-info", options.Path);
        Assert.True(options.IsOriginAllowed("https://app.example"));
    }

    [Fact]
    public void Snapshot_UsesCurrentServerConfigurationAndAuthenticatedCount()
    {
        Configuration configuration = new()
        {
            PeerLimit = 16,
            ServerName = "Basis",
            ServerMotd = "Hello",
        };

        ServerInfoSnapshot snapshot = ServerInfoSnapshot.FromConfiguration(configuration, 3);

        Assert.Equal((ushort)3, snapshot.Online);
        Assert.Equal((ushort)16, snapshot.Max);
        Assert.Equal(Basis.Network.Core.BasisNetworkCommons.ServerInfoProtocolVersion, snapshot.ProtocolVersion);
        Assert.Equal("Basis", snapshot.Name);
        Assert.Equal("Hello", snapshot.Motd);
    }

    private static ServerInfoHttpEndpointOptions ValidOptions()
    {
        ServerInfoHttpEndpointOptions options = new() { Path = "/basis/server-info" };
        options.AllowedOrigins.Add("https://app.example");
        return options;
    }
}
