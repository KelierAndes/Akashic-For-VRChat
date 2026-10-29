using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Akashic.Core.Models;
using Akashic.Core.Services.Preview;
using Akashic.Host.Preview;
using Akashic.Host.Preview.Hlsl;

namespace Akashic.Host.Api;

public static partial class PreviewEndpoints
{
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".glb"] = "model/gltf-binary",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".svg"] = "image/svg+xml",
        [".pdf"] = "application/pdf"
    };

    private static readonly HashSet<string> BuildFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "scene.json", "model.glb"
    };

    public static RouteGroupBuilder MapPreview(this RouteGroupBuilder group)
    {
        group.MapGet("/preview/list", async Task<IResult> (string resourceKey, string? entry,
            IPreviewService preview, CancellationToken ct) =>
        {
            try
            {
                return Results.Json(await preview.ListAsync(resourceKey, entry, ct));
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message, statusCode: 500, title: "preview-list");
            }
        });

        group.MapPost("/preview/build", async Task<IResult> (PreviewRequest request, IPreviewService preview,
            SidecarOptions options, CancellationToken ct) =>
        {
            try
            {
                var result = await preview.BuildAsync(request, ct);
                var basePath = $"/preview/{options.Token}";
                result.ViewerUrl = $"{basePath}/viewer.html?build={result.SessionId}&embed=1";
                result.ModelUrl = $"{basePath}/builds/{result.SessionId}/model.glb";
                return Results.Json(result);
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message, statusCode: 500, title: "preview-build");
            }
        });

        group.MapPost("/preview/render", async Task<IResult> (RenderOptions options, HlslPreviewService hlsl,
            HttpContext context, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            try
            {
                var frame = await hlsl.RenderAsync(options, ct);
                context.Response.Headers["X-Akashic-Draws"] = frame.DrawCalls.ToString();
                context.Response.Headers["X-Akashic-Fallbacks"] = frame.Fallbacks.ToString();
                foreach (var (kind, count) in frame.FailureKinds)
                    context.Response.Headers["X-Akashic-Reject-" + kind] = count.ToString();
                context.Response.Headers["X-Akashic-Render-Ms"] =
                    frame.RenderMs.ToString("F1", CultureInfo.InvariantCulture);
                context.Response.Headers["X-Akashic-Queue-Ms"] =
                    frame.QueueMs.ToString("F1", CultureInfo.InvariantCulture);
                context.Response.Headers["X-Akashic-Frame-Width"] = frame.Width.ToString();
                context.Response.Headers["X-Akashic-Frame-Height"] = frame.Height.ToString();
                context.Response.Headers["X-Akashic-Textures"] =
                    $"{frame.Textures}|{frame.TextureBytes / (1024 * 1024)}|{frame.TextureDecodeFailures}";
                context.Response.Headers["X-Akashic-Msaa"] = frame.SampleCount.ToString();
                if (frame.Camera is { } camera)
                {
                    context.Response.Headers["X-Akashic-Camera"] = string.Join(";", new[]
                    {
                        camera.Yaw, camera.Pitch, camera.Distance,
                        camera.TargetX, camera.TargetY, camera.TargetZ
                    }.Select(value => value.ToString("F5", CultureInfo.InvariantCulture))
                        .Append(camera.Fov.ToString("F3", CultureInfo.InvariantCulture)));
                }

                if (frame.FallbackNotes.Count > 0)
                {
                    context.Response.Headers["X-Akashic-Fallback-Detail"] =
                        Uri.EscapeDataString(string.Join(" | ", frame.FallbackNotes.Take(8)));
                }

                context.Response.Headers["Cache-Control"] = "no-store";
                return options.Raw
                    ? Results.Bytes(frame.Pixels, "application/octet-stream")
                    : Results.Bytes(frame.Jpeg, "image/jpeg");
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger("hlsl").LogError(ex, "D3D11 渲染失败 {session}", options.SessionId);
                return Results.Problem(ex.Message, statusCode: 422, title: "preview-render");
            }
        });

        group.MapGet("/preview/hlsl/status", (HlslPreviewService hlsl) =>
        {
            var (available, error, adapter) = hlsl.Status();
            return Results.Json(new { available, error, adapter });
        });

        return group;
    }

    public static WebApplication MapPreviewRoutes(this WebApplication app, SidecarOptions options)
    {
        var staticRoot = Path.Combine(AppContext.BaseDirectory, "preview");

        app.MapGet("/preview/{token}/viewer.html", (string token) =>
        {
            var path = Path.Combine(staticRoot, "viewer.html");
            if (!File.Exists(path)) return Results.NotFound("预览页面未安装。");
            return Results.File(path, "text/html; charset=utf-8");
        });

        app.MapGet("/preview/{token}/static/{**path}", (string token, string path) =>
        {
            var full = Path.GetFullPath(Path.Combine(staticRoot, path.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(staticRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(full))
                return Results.NotFound();

            return Results.File(full, ContentTypeOf(full));
        });

        app.MapGet("/preview/{token}/builds/{id}/{**file}", (string token, string id, string file,
            IPreviewService preview) =>
        {
            var buildRoot = preview.BuildsRoot;
            if (!SessionIdRegex().IsMatch(id)) return Results.NotFound();

            var relative = file.Replace('\\', '/');
            var allowed = BuildFiles.Contains(relative) ||
                          (relative.StartsWith("textures/", StringComparison.OrdinalIgnoreCase) &&
                           !relative.Contains("..", StringComparison.Ordinal));

            if (!allowed) return Results.NotFound();

            var directory = Path.Combine(buildRoot, id);
            var full = Path.GetFullPath(Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(full))
                return Results.NotFound();

            return Results.File(full, ContentTypeOf(full), enableRangeProcessing: true);
        });

        app.MapGet("/preview/{token}/file", async Task<IResult> (string token, HttpContext context,
            IPreviewService preview, TexturePreviewService textures, CancellationToken ct) =>
        {
            var resource = Query(context, "k");
            var relative = Query(context, "p");
            if (resource.Length == 0 || relative.Length == 0) return Results.NotFound();

            try
            {
                var full = await preview.AssetAsync(resource, Query(context, "entry"), relative, ct);

                if (Path.GetExtension(full).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.Headers["Cache-Control"] = "no-store";
                    return Results.File(full, "application/pdf", enableRangeProcessing: true);
                }

                var (target, contentType) = textures.Resolve(full);
                return Results.File(target, contentType, enableRangeProcessing: true);
            }
            catch (Exception)
            {
                return Results.NotFound();
            }
        });

        app.MapGet("/preview/{token}/text", async Task<IResult> (string token, HttpContext context,
            IPreviewService preview, CancellationToken ct) =>
        {
            var resource = Query(context, "k");
            var relative = Query(context, "p");
            if (resource.Length == 0 || relative.Length == 0) return Results.NotFound();

            var limit = Math.Clamp(ParseInt(Query(context, "limit"), 200_000), 1024, 2_000_000);

            try
            {
                var full = await preview.AssetAsync(resource, Query(context, "entry"), relative, ct);
                var info = new FileInfo(full);
                if (!info.Exists) return Results.NotFound();

                if (info.Length > 32L * 1024 * 1024)
                    return Results.Problem("这个文件超过 32 MB，不适合按文本预览。", statusCode: 413,
                        title: "preview-text");

                var bytes = await File.ReadAllBytesAsync(full, ct);
                var decoded = DecodeText(bytes, limit);

                if (decoded is null)
                    return Results.Problem("这是二进制文件，没有可显示的文本内容。", statusCode: 415, title: "preview-text");

                context.Response.Headers["Cache-Control"] = "no-store";
                return Results.Json(new
                {
                    text = decoded.Value.Text,
                    encoding = decoded.Value.Encoding,
                    size = info.Length,
                    truncated = decoded.Value.Truncated
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message, statusCode: 422, title: "preview-text");
            }
        });

        app.MapGet("/preview/{token}/material", async Task<IResult> (string token, HttpContext context,
            IPreviewService preview, HlslPreviewService hlsl, CancellationToken ct) =>
        {
            var resource = Query(context, "k");
            var relative = Query(context, "p");
            if (resource.Length == 0 || relative.Length == 0) return Results.NotFound();

            var size = Math.Clamp(ParseInt(Query(context, "size"), 512), 128, 1536);
            var light = Query(context, "light") is "1" or "true";
            var yaw = ParseFloat(Query(context, "yaw"), float.NaN);
            var pitch = ParseFloat(Query(context, "pitch"), float.NaN);
            var distance = ParseFloat(Query(context, "distance"), 0f);
            var orbit = !float.IsNaN(yaw) && !float.IsNaN(pitch);

            try
            {
                var material = await preview.MaterialAsync(resource, Query(context, "entry"), relative, ct);
                var frame = await hlsl.RenderMaterialAsync(material, new RenderOptions
                {
                    SessionId = "material",
                    Width = size,
                    Height = size,
                    View = orbit ? "orbit" : "iso",
                    Yaw = orbit ? yaw : 0,
                    Pitch = orbit ? pitch : 0,
                    Distance = distance,
                    Mode = "textured",
                    Outline = false,
                    LightBackground = light
                }, ct);

                context.Response.Headers["Cache-Control"] = orbit ? "no-store" : "public, max-age=600";
                return Results.Bytes(frame.Jpeg, "image/jpeg");
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message, statusCode: 422, title: "preview-material");
            }
        });

        return app;
    }

    private static string Query(HttpContext context, string key) =>
        context.Request.Query.TryGetValue(key, out var values) ? values.ToString() : string.Empty;

    private static (string Text, string Encoding, bool Truncated)? DecodeText(byte[] bytes, int limit)
    {
        string raw;
        string label;

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            raw = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            label = "UTF-16 LE";
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            raw = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            label = "UTF-16 BE";
        }
        else if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            raw = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            label = "UTF-8";
        }
        else
        {
            if (LooksBinary(bytes)) return null;

            string? decoded = null;
            label = "UTF-8";

            try
            {
                decoded = new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                label = "Shift-JIS";

                try
                {
                    decoded = Encoding.GetEncoding(932).GetString(bytes);
                }
                catch (ArgumentException)
                {
                    decoded = null;
                }
            }

            if (decoded is null)
            {
                decoded = Encoding.Latin1.GetString(bytes);
                label = "Latin-1";
            }

            raw = decoded;
        }

        var truncated = raw.Length > limit;
        var text = (truncated ? raw[..limit] : raw).Replace("\r\n", "\n").Replace('\r', '\n');
        var builder = new StringBuilder(text.Length);

        foreach (var character in text)
            if (character >= ' ' || character is '\n' or '\t') builder.Append(character);

        return (builder.ToString(), label, truncated);
    }

    private static bool LooksBinary(byte[] bytes)
    {
        var probe = Math.Min(bytes.Length, 4096);
        for (var i = 0; i < probe; i++)
            if (bytes[i] == 0) return true;

        return false;
    }

    private static int ParseInt(string text, int fallback) =>
        int.TryParse(text, out var value) ? value : fallback;

    private static float ParseFloat(string text, float fallback) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static string ContentTypeOf(string path) =>
        ContentTypes.TryGetValue(Path.GetExtension(path), out var type) ? type : "application/octet-stream";

    [GeneratedRegex("^[0-9a-f]{20}$")]
    private static partial Regex SessionIdRegex();
}
