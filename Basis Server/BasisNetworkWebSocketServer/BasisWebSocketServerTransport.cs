using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Basis.Network.WebSocketServer;

public sealed class BasisWebSocketServerTransport : IAsyncDisposable
{
    private readonly WebApplication _application;
    private readonly WebSocketPeerIdAllocator _peerIdAllocator;
    private int _started;

    public BasisWebSocketServerTransport(
        WebSocketServerTransportOptions options,
        IWebSocketServerConnectionHandler connectionHandler,
        ServerInfoHttpEndpointOptions serverInfoOptions,
        Func<ServerInfoSnapshot> serverInfoSnapshotProvider,
        WebSocketPeerIdAllocator? peerIdAllocator = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(connectionHandler);
        ArgumentNullException.ThrowIfNull(serverInfoOptions);
        ArgumentNullException.ThrowIfNull(serverInfoSnapshotProvider);
        options.Validate();
        serverInfoOptions.Validate();
        _peerIdAllocator = peerIdAllocator ?? new WebSocketPeerIdAllocator();

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        // .NET 10 requires this opt-in before Kestrel may load HTTPS endpoint
        // and certificate settings supplied through IConfiguration. Without
        // it, a TLS-enabled browser transport fails during host startup even
        // when both certificate files are present and readable.
        if (options.UseTls)
        {
            builder.WebHost.UseKestrelHttpsConfiguration();
        }
        string endpoint = "Kestrel:Endpoints:WebSocket";
        builder.Configuration[$"{endpoint}:Url"] = $"{(options.UseTls ? "https" : "http")}://*:{options.Port}";
        if (options.UseTls)
        {
            builder.Configuration[$"{endpoint}:Certificate:Path"] = options.CertificatePath;
            builder.Configuration[$"{endpoint}:Certificate:KeyPath"] = options.CertificateKeyPath;
        }
        builder.WebHost.ConfigureKestrel((context, serverOptions) =>
            serverOptions.Configure(context.Configuration.GetSection("Kestrel")));
        WebApplication application = builder.Build();
        application.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = options.KeepAliveInterval,
        });
        ServerInfoHttpEndpoint.Map(application, serverInfoOptions, serverInfoSnapshotProvider);
        application.Map(options.Path, branch => branch.Run(context => HandleRequestAsync(
            context,
            options,
            connectionHandler,
            _peerIdAllocator,
            context.RequestAborted)));
        _application = application;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("The WebSocket server transport is already started.");
        }
        return _application.StartAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 0) == 0)
        {
            return Task.CompletedTask;
        }
        return _application.StopAsync(cancellationToken);
    }

    private static async Task HandleRequestAsync(
        HttpContext context,
        WebSocketServerTransportOptions options,
        IWebSocketServerConnectionHandler connectionHandler,
        WebSocketPeerIdAllocator peerIdAllocator,
        CancellationToken cancellationToken)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        if (!options.IsOriginAllowed(context.Request.Headers.Origin.ToString()))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        using System.Net.WebSockets.WebSocket socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        IPAddress remoteAddress = context.Connection.RemoteIpAddress
            ?? throw new InvalidOperationException("Kestrel did not provide a remote IP address.");
        IPEndPoint remoteEndPoint = new(remoteAddress, context.Connection.RemotePort);
        int peerId = peerIdAllocator.Allocate();
        try
        {
            await using WebSocketServerSession session = new(
                socket,
                connectionHandler,
                options.MaximumPayloadLength,
                remoteEndPoint,
                peerId,
                options.PendingSendCapacity);
            await session.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            peerIdAllocator.Release(peerId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        await _application.DisposeAsync().ConfigureAwait(false);
    }
}
