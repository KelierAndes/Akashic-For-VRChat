using Akashic.Core.Models;
using Akashic.Core.Services;

namespace Akashic.Host.Api;

/// <summary>
/// The three account-backed surfaces. Reading Booth's and Unity's lists still needs a
/// real browser, but that browser now lives in the frontend: the sidecar hands out the
/// extractor scripts and turns whatever JSON they return back into rows, so all of the
/// parsing, de-duplication and cache rules stay in the verified C# layer.
/// </summary>
public static class AccountEndpoints
{
    public static RouteGroupBuilder MapAccounts(this RouteGroupBuilder group)
    {
        // ---------- Booth ----------

        group.MapGet("/booth/scripts", (IBoothService booth) => new
        {
            purchase = booth.PurchaseExtractorScript,
            freeLibrary = booth.FreeLibraryExtractorScript,
            itemPage = booth.ItemPageExtractorScript,
            downloadDirectory = booth.GetDownloadDirectory(),
        });

        group.MapGet("/booth/url", (string kind, int page, IBoothService booth) => new
        {
            url = kind switch
            {
                "free" => booth.GetFreeLibraryUrl(page),
                "gifts" => booth.GetGiftsUrl(page),
                _ => booth.GetLibraryUrl(page),
            }
        });

        group.MapPost("/booth/parse", (ParseRequest request, IBoothService booth) => Results.Json(
            request.Kind switch
            {
                "free" => booth.ParseFreeLibraryPage(request.Json),
                "item" => booth.ParseItemPage(request.Json),
                _ => booth.ParseLibraryPage(request.Json),
            }));

        // LocalZipPath and IsDownloaded are [JsonIgnore] on the model, so the numbers that
        // already have an archive are handed to the frontend next to the rows.
        group.MapGet("/booth/library", (IBoothService booth, IUnityPackageLibraryService library) =>
            Results.Json(Snapshot(booth, library)));

        group.MapPost("/booth/library", async Task<IResult> (BoothLibrarySave request, IBoothService booth,
            IUnityPackageLibraryService library, CancellationToken ct) =>
        {
            // Reconciling marks which rows already have a local archive before the list is stored,
            // and folding keeps one row per product so the snapshot is not a pile of old versions.
            booth.ReconcileLocalArchives(request.Items);
            booth.FoldOldVersions(request.Items, new Dictionary<string, int>());
            await booth.SaveLibraryCacheAsync(request.Items, ct);
            return Results.Json(Snapshot(booth, library));
        });

        group.MapPost("/booth/download", async Task<IResult> (NumberRequest request,
            IBoothService booth, EventHub hub, CancellationToken ct) =>
        {
            var item = Find(booth, request.Number);
            if (item is null) return Results.Problem("这条商品不在已读取的列表里。", statusCode: 404);
            if (!booth.HasSession) return Results.Problem("Booth 登录态缺失，请先登录。", statusCode: 401);

            var id = Guid.NewGuid().ToString("N");
            hub.Publish("booth.start", new { id, number = item.Number, title = item.DisplayTitle });
            var path = await booth.DownloadAsync(item, hub.Progress("booth", id), ct);
            hub.Publish("booth.done", new { id, number = item.Number, path });

            return Results.Json(new { path });
        });

        group.MapPost("/booth/inspect", async Task<IResult> (NumberRequest request, IBoothService booth,
            CancellationToken ct) =>
        {
            var item = Find(booth, request.Number);
            if (item is null) return Results.Problem("这条商品不在已读取的列表里。", statusCode: 404);

            return Results.Json(await booth.InspectAsync(item, ct));
        });

        // ---------- Unity Asset Store ----------

        group.MapGet("/assetstore/scripts", (IAssetStoreService store) => new
        {
            myAssets = store.MyAssetsExtractorScript,
            searchCover = store.SearchCoverExtractorScript,
            myAssetsUrl = store.MyAssetsUrl,
            cacheDirectory = store.GetCacheDirectory(),
            downloadDirectory = store.GetDownloadDirectory(),
        });

        group.MapGet("/assetstore/search-url", (string term, IAssetStoreService store) =>
            new { url = store.GetSearchUrl(term) });

        group.MapPost("/assetstore/parse", (ParseRequest request, IAssetStoreService store) => Results.Json(
            request.Kind == "cover" ? store.ParseSearchCovers(request.Json) : store.ParseMyAssets(request.Json)));

        group.MapGet("/assetstore/cache", (IAssetStoreService store, CancellationToken ct) =>
            store.ScanCacheAsync(ct));

        group.MapGet("/assetstore/account-cache", (IAssetStoreService store) =>
            Results.Json(store.LoadAccountCache()));

        group.MapPost("/assetstore/account-cache", async Task<IResult> (AssetStoreSave request,
            IAssetStoreService store, CancellationToken ct) =>
        {
            await store.SaveAccountCacheAsync(request.Items, ct);
            return Results.Json(store.LoadAccountCache());
        });

        // Account rows first with their local file attached, then whatever the cache
        // folder holds that the account list did not claim — otherwise the shelf
        // looks empty for everyone whose account list has not been read yet.
        group.MapPost("/assetstore/merge", async Task<IResult> (AssetStoreSave request,
            IAssetStoreService store, CancellationToken ct) =>
        {
            var cached = await store.ScanCacheAsync(ct);
            store.MergeCachedFiles(request.Items, cached);

            var claimed = request.Items
                .Where(i => !string.IsNullOrWhiteSpace(i.LocalPath))
                .Select(i => System.IO.Path.GetFullPath(i.LocalPath!))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var extra = cached
                .Where(c => c.LocalPath is null || !claimed.Contains(System.IO.Path.GetFullPath(c.LocalPath)))
                .ToList();

            // A collection expression has no type to infer from here, so the union is
            // spelled out as a list.
            return Results.Json(request.Items.Concat(extra).ToList());
        });

        group.MapPost("/assetstore/cover", (CoverRequest request, IAssetStoreService store) =>
        {
            store.CacheCoverUrl(request.Name, request.Url);
            return Results.Json(new { cached = true });
        });

        group.MapGet("/assetstore/cover", (string name, IAssetStoreService store) =>
            store.TryGetCoverUrl(name, out var url)
                ? Results.Json(new { url })
                : Results.Problem("没有这个名称的封面缓存。", statusCode: 404));

        // When the store never gave a cover, the package itself can: the first preview image
        // inside the .unitypackage, extracted once and kept on disk. Answered as a local path,
        // which the shell reads directly instead of pulling bytes through this socket.
        group.MapPost("/assetstore/thumbnail", async Task<IResult> (AssetStoreThumbnail request,
            IUnityPackageIconService icons, CancellationToken ct) =>
            Results.Json(new { path = await icons.GetThumbnailAsync(request.Path, ct) }));

        group.MapPost("/assetstore/download", async Task<IResult> (AssetStoreDownload request,
            IAssetStoreService store, EventHub hub, CancellationToken ct) =>
        {
            var saved = store.LoadAccountCache();
            var asset = saved.Items.FirstOrDefault(a =>
                (!string.IsNullOrWhiteSpace(request.ProductId)
                    && string.Equals(a.ProductId, request.ProductId, StringComparison.OrdinalIgnoreCase))
                || string.Equals(a.Name, request.Name, StringComparison.OrdinalIgnoreCase));

            if (asset is null) return Results.Problem("这个资产不在我的资产列表里。", statusCode: 404);

            var id = Guid.NewGuid().ToString("N");
            hub.Publish("assetstore.start", new { id, asset = asset.Name });
            var path = await store.DownloadAsync(asset, hub.Progress("assetstore", id), ct);
            hub.Publish("assetstore.done", new { id, asset = asset.Name, path });

            return Results.Json(new { path });
        });

        // ---------- VRChat ----------

        group.MapGet("/vrchat/self", (IVRChatSyncService vrchat, CancellationToken ct) =>
            vrchat.GetSelfAsync(ct));

        group.MapGet("/vrchat/content", (IVRChatSyncService vrchat, CancellationToken ct) =>
            vrchat.GetMyContentAsync(ct));

        group.MapPost("/vrchat/content/update", async Task<IResult> (VRChatEditRequest request,
            IVRChatSyncService vrchat, CancellationToken ct) =>
        {
            if (!Enum.TryParse<VRChatContentKind>(request.Kind, true, out var kind))
                return Results.Problem("未知的内容类型。", statusCode: 400);

            // Always edit against a freshly fetched record: VRChat's PUT resets any field
            // the request leaves out, so the list row is not a safe base.
            var source = await vrchat.GetContentAsync(kind, request.Id, ct);
            if (source is null) return Results.Problem("云端找不到这条内容。", statusCode: 404);

            var edit = new VRChatContentEdit
            {
                Source = source,
                Name = request.Name,
                Description = request.Description,
                Tags = request.Tags,
                ReleaseStatus = request.ReleaseStatus,
                ImageUrl = request.ImageUrl,
                MaxCapacity = request.MaxCapacity,
                PreviewYoutubeId = request.PreviewYoutubeId,
                UrlList = request.UrlList,
                ContentSettings = request.ContentSettings,
                DisabledPropAbilities = request.DisabledPropAbilities,
                Publish = request.Publish,
                Styles = request.Styles,
            };

            return Results.Json(await vrchat.UpdateContentAsync(edit, ct));
        });

        group.MapPost("/vrchat/content/delete", async Task<IResult> (VRChatRefRequest request,
            IVRChatSyncService vrchat, CancellationToken ct) =>
        {
            if (!Enum.TryParse<VRChatContentKind>(request.Kind, true, out var kind))
                return Results.Problem("未知的内容类型。", statusCode: 400);

            await vrchat.DeleteContentAsync(kind, request.Id, ct);
            return Results.Json(await vrchat.GetMyContentAsync(ct));
        });

        group.MapPost("/vrchat/content/publish", async (VRChatIdRequest request, IVRChatSyncService vrchat,
            CancellationToken ct) =>
        {
            await vrchat.PublishWorldAsync(request.Id, ct);
            return Results.Json(await vrchat.GetMyContentAsync(ct));
        });

        group.MapPost("/vrchat/content/unpublish", async (VRChatIdRequest request, IVRChatSyncService vrchat,
            CancellationToken ct) =>
        {
            await vrchat.UnpublishWorldAsync(request.Id, ct);
            return Results.Json(await vrchat.GetMyContentAsync(ct));
        });

        group.MapPost("/vrchat/avatar/select", async (VRChatSelectAvatarRequest request,
            IVRChatSyncService vrchat, CancellationToken ct) =>
        {
            await vrchat.SelectAvatarAsync(request.Id, request.Fallback, ct);
            return Results.Json(await vrchat.GetSelfAsync(ct));
        });

        group.MapGet("/vrchat/styles", (IVRChatSyncService vrchat, CancellationToken ct) =>
            vrchat.GetAvatarStylesAsync(ct));

        group.MapGet("/vrchat/content-cache", (IVRChatSyncService vrchat) =>
            Results.Json(vrchat.LoadContentCache()));

        group.MapPost("/vrchat/content-cache", async Task<IResult> (VRChatCacheSave request,
            IVRChatSyncService vrchat, CancellationToken ct) =>
        {
            await vrchat.SaveContentCacheAsync(request.Items, ct);
            return Results.Json(vrchat.LoadContentCache());
        });

        // One scan per call: the map is computed, published, and returned from the same pass,
        // because walking every project's Packages folder is the slow part of this page.
        group.MapPost("/vrchat/local-map", async Task<IResult> (IProjectService projects,
            IVRChatBlueprintScanService scan, CancellationToken ct) =>
        {
            var list = await projects.GetProjectsAsync();
            var map = await scan.MapByBlueprintAsync(list, ct);
            scan.PublishCloudIds(map.Keys);
            return Results.Json(new { map, cloudIds = scan.CloudIds });
        });

        return group;
    }

    private static object Snapshot(IBoothService booth, IUnityPackageLibraryService library)
    {
        var cache = booth.LoadLibraryCache();
        booth.ReconcileLocalArchives(cache.Items);

        // A snapshot written before folding existed still carries every version, so the
        // page gets the same view either way, plus what the survivors stand in for.
        var hiddenByNumber = new Dictionary<string, int>(StringComparer.Ordinal);
        var hiddenTotal = booth.FoldOldVersions(cache.Items, hiddenByNumber);

        var files = library.ScanAsync(CancellationToken.None).GetAwaiter().GetResult();
        var paths = files
            .Where(f => f.Origin == PackageOrigin.Booth)
            .Select(f => System.IO.Path.GetFullPath(f.FilePath))
            .ToList();

        return new
        {
            savedAt = cache.SavedAt,
            items = cache.Items,
            downloaded = cache.Items.Where(i => i.IsDownloaded).Select(i => i.Number).ToList(),
            // A purchase counts as shelved when one of the .unitypackage files the library knows
            // about came out of that archive's own -extracted folder. Matching on the product
            // title never works: the package carries the file name, and versions differ.
            shelved = cache.Items.Where(i => Shelved(i, paths)).Select(i => i.Number).ToList(),
            hiddenVersions = hiddenByNumber,
            hiddenTotal = hiddenTotal,
        };
    }

    private static bool Shelved(BoothPurchaseItem item, List<string> libraryPaths)
    {
        if (item.LocalZipPath is not { } zip) return false;
        var directory = System.IO.Path.GetDirectoryName(zip) ?? ".";
        var extracted = System.IO.Path.Combine(directory,
            System.IO.Path.GetFileNameWithoutExtension(zip) + "-extracted");

        return libraryPaths.Any(path =>
            path.StartsWith(extracted + System.IO.Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase));
    }

    private static BoothPurchaseItem? Find(IBoothService booth, string number)
    {
        var cache = booth.LoadLibraryCache();
        // The archive path is not part of the cached JSON, so the download folder is
        // walked again here — otherwise inspect would report "还没有下载" for a zip
        // that is plainly on disk.
        booth.ReconcileLocalArchives(cache.Items);
        return cache.Items.FirstOrDefault(i =>
            string.Equals(i.Number, number, StringComparison.Ordinal));
    }
}

public sealed record ParseRequest(string Json, string Kind = "purchase");

public sealed record BoothLibrarySave(List<BoothPurchaseItem> Items);

public sealed record NumberRequest(string Number);

public sealed record AssetStoreSave(List<AssetStoreAsset> Items);

public sealed record AssetStoreDownload(string Name, string? ProductId);

public sealed record CoverRequest(string Name, string Url);

public sealed record AssetStoreThumbnail(string Path);

public sealed record VRChatIdRequest(string Id);

public sealed record VRChatRefRequest(string Id, string Kind);

public sealed record VRChatSelectAvatarRequest(string Id, bool Fallback);

public sealed record VRChatCacheSave(List<VRChatContentItem> Items);

public sealed record VRChatEditRequest(
    string Id,
    string Kind,
    string? Name,
    string? Description,
    List<string>? Tags,
    string? ReleaseStatus,
    string? ImageUrl,
    int? MaxCapacity,
    string? PreviewYoutubeId,
    List<string>? UrlList,
    Dictionary<string, bool>? ContentSettings,
    List<string>? DisabledPropAbilities,
    bool? Publish,
    VRChatAvatarStyles? Styles);
