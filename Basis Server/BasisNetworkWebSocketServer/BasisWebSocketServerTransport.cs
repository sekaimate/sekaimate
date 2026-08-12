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
    private int _started;

    public BasisWebSocketServerTransport(
        WebSocketServerTransportOptions options,
        IWebSocketServerConnectionHandler connectionHandler)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(connectionHandler);
        options.Validate();

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(serverOptions => serverOptions.ListenAnyIP(options.Port));
        WebApplication application = builder.Build();
        application.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = options.KeepAliveInterval,
        });
        application.Map(options.Path, branch => branch.Run(context => HandleRequestAsync(
            context,
            options,
            connectionHandler,
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
        await using WebSocketServerSession session = new(
            socket,
            connectionHandler,
            options.MaximumPayloadLength,
            remoteEndPoint);
        await session.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        await _application.DisposeAsync().ConfigureAwait(false);
    }
}
