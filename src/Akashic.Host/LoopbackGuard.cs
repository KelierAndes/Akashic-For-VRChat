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
            var isApi = context.Request.Path.StartsWithSegments("/api");
            var isPreview = context.Request.Path.StartsWithSegments("/preview");

            if (!isApi && !isPreview)
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

            var provided = isApi
                ? context.Request.Headers[SidecarOptions.TokenHeader].ToString()
                : PreviewTokenFromPath(context.Request.Path.Value);

            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(provided), token))
            {
                response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next();
        });

        return app;
    }

    private static string PreviewTokenFromPath(string? path)
    {
        var segments = (path ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 1 ? segments[1] : string.Empty;
    }
}
