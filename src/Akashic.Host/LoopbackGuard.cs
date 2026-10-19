using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Akashic.Host;

public static class LoopbackGuard
{
    public static WebApplication UseLoopbackGuard(this WebApplication app, SidecarOptions options)
    {
        var token = Encoding.UTF8.GetBytes(options.Token);

        app.Use(async (context, next) =>
        {
            var response = context.Response;

            if (!context.Request.Path.StartsWithSegments("/api"))
            {
                response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var remote = context.Connection.RemoteIpAddress;
            if (remote is null || !IPAddress.IsLoopback(remote))
            {
                response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            var host = context.Request.Host.Host;
            if (host is not ("127.0.0.1" or "localhost" or "::1"))
            {
                response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            if (!context.Request.Headers.TryGetValue(SidecarOptions.TokenHeader, out var provided)
                || !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(provided.ToString()), token))
            {
                response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next();
        });

        return app;
    }
}
