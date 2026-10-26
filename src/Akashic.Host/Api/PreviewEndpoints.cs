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
        [".svg"] = "image/svg+xml"
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
                context.Response.Headers["Cache-Control"] = "no-store";
                return Results.Bytes(frame.Jpeg, "image/jpeg");
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
                var (target, contentType) = textures.Resolve(full);
                return Results.File(target, contentType, enableRangeProcessing: true);
            }
            catch (Exception)
            {
                return Results.NotFound();
            }
        });

        app.MapGet("/preview/{token}/material", async Task<IResult> (string token, HttpContext context,
            IPreviewService preview, HlslPreviewService hlsl, CancellationToken ct) =>
        {
            var resource = Query(context, "k");
            var relative = Query(context, "p");
            if (resource.Length == 0 || relative.Length == 0) return Results.NotFound();

            var size = Math.Clamp(ParseInt(Query(context, "size"), 512), 128, 1024);

            try
            {
                var material = await preview.MaterialAsync(resource, Query(context, "entry"), relative, ct);
                var frame = await hlsl.RenderMaterialAsync(material, new RenderOptions
                {
                    SessionId = "material",
                    Width = size,
                    Height = size,
                    View = "iso",
                    Mode = "textured",
                    Outline = false
                }, ct);

                context.Response.Headers["Cache-Control"] = "public, max-age=600";
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

    private static int ParseInt(string text, int fallback) =>
        int.TryParse(text, out var value) ? value : fallback;

    private static string ContentTypeOf(string path) =>
        ContentTypes.TryGetValue(Path.GetExtension(path), out var type) ? type : "application/octet-stream";

    [GeneratedRegex("^[0-9a-f]{20}$")]
    private static partial Regex SessionIdRegex();
}
