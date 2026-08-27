using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Basis.Network.WebSocketServer;

public static class ServerInfoHttpEndpoint
{
    public static void Map(
        WebApplication application,
        ServerInfoHttpEndpointOptions options,
        Func<ServerInfoSnapshot> snapshotProvider)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(snapshotProvider);
        options.Validate();

        application.MapGet(options.Path, context => HandleRequestAsync(context, options, snapshotProvider));
    }

    internal static async Task HandleRequestAsync(
        HttpContext context,
        ServerInfoHttpEndpointOptions options,
        Func<ServerInfoSnapshot> snapshotProvider)
    {
        options.Validate();
        string origin = context.Request.Headers.Origin.ToString();
        if (!options.IsOriginAllowed(origin))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        context.Response.Headers.AccessControlAllowOrigin = origin;
        context.Response.Headers.Vary = "Origin";
        await context.Response.WriteAsJsonAsync(snapshotProvider(), context.RequestAborted).ConfigureAwait(false);
    }
}
