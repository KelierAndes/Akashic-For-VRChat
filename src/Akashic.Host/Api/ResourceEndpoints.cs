using Akashic.Core.Data;
using Akashic.Core.Models;
using Akashic.Core.Services;

namespace Akashic.Host.Api;

public static class ResourceEndpoints
{
    public static RouteGroupBuilder MapResources(this RouteGroupBuilder group)
    {
        group.MapGet("/resources", async (IResourceCatalogService catalog, AkashicDatabase db,
            CancellationToken ct, string? origin = null, string? category = null, string? q = null) =>
        {
            var rows = await catalog.GetRowsAsync(ct);
            if (origin is null && category is null && q is null) return rows;
            return db.LoadResources(origin, category, q);
        });

        group.MapGet("/booth/search", async (string term, IBoothService booth, CancellationToken ct) =>
            await booth.SearchAsync(term, ct));

        group.MapPost("/resources/match", async (ResourceMatchRequest request,
            IResourceCatalogService catalog, AutoSync auto) =>
        {
            var status = await catalog.StartMatchAsync(request.Force);
            _ = Task.Run(async () =>
            {
                while (catalog.GetMatchStatus().Running) await Task.Delay(1000);
                auto.Queue("Booth 匹配");
            });
            return status;
        });

        group.MapGet("/resources/match/status", (IResourceCatalogService catalog) =>
            catalog.GetMatchStatus());

        group.MapGet("/resources/pending", (IResourceCatalogService catalog) =>
            catalog.GetPendingMatches());

        group.MapPost("/resources/pending/confirm", async (PendingMatchRequest request,
            IResourceCatalogService catalog, CancellationToken ct) =>
            Results.Json(await catalog.ConfirmPendingMatchAsync(request, ct)));

        group.MapPost("/resources/pending/dismiss", async (ResourceKeyRequest request,
            IResourceCatalogService catalog, CancellationToken ct) =>
            Results.Json(await catalog.DismissPendingMatchAsync(request.Key, ct)));

        group.MapPost("/resources/meta", async (ResourceMetaUpdate request,
            IResourceCatalogService catalog, CancellationToken ct) =>
            Results.Json(await catalog.SaveMetaAsync(request, ct)));

        group.MapPost("/resources/meta/reset", async (ResourceKeyRequest request,
            IResourceCatalogService catalog, CancellationToken ct) =>
            Results.Json(await catalog.ResetMetaAsync(request.Key, ct)));

        group.MapPost("/library/zip/extract", async Task<IResult> (ArchiveExtractRequest request,
            IUnityPackageLibraryService library, CancellationToken ct) =>
        {
            if (!File.Exists(request.ArchivePath))
                return Results.Problem("压缩包不存在。", statusCode: 404);

            var paths = await library.ExtractArchiveAsync(request.ArchivePath, request.Entries, ct);
            return Results.Json(new { paths });
        });

        return group;
    }
}
