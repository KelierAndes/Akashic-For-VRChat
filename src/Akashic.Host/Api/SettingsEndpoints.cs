using System.Diagnostics;
using Akashic.Core.Models;
using Akashic.Core.Services;
using Akashic.Core.Services.Preview;

namespace Akashic.Host.Api;

public static class SettingsEndpoints
{
    public static RouteGroupBuilder MapSettings(this RouteGroupBuilder group)
    {
        group.MapPost("/settings/default-project-path", async (DirectoryRequest request,
            IProjectService projects) =>
        {
            await projects.SetDefaultProjectPathAsync(request.Directory);
            return new { defaultProjectPath = projects.GetDefaultProjectPath() };
        });

        group.MapPost("/settings/backup-path", async (DirectoryRequest request,
            IProjectService projects) =>
        {
            await projects.SetBackupDirectoryAsync(request.Directory);
            return new { backupDirectory = projects.GetBackupDirectory() };
        });

        group.MapGet("/settings/preview-cache", (IProjectService projects) =>
        {
            var root = projects.GetPreviewCacheDirectory();
            return Results.Json(new
            {
                directory = root,
                size = PreviewCachePaths.MeasureBytes(root),
                quota = projects.GetPreviewCacheQuota()
            });
        });

        group.MapPost("/settings/preview-cache-dir", async (DirectoryRequest request,
            IProjectService projects) =>
        {
            var applied = await projects.SetPreviewCacheDirectoryAsync(request.Directory);
            return new
            {
                directory = applied,
                size = PreviewCachePaths.MeasureBytes(applied),
                quota = projects.GetPreviewCacheQuota()
            };
        });

        group.MapPost("/settings/preview-cache-quota", async (QuotaRequest request,
            IProjectService projects) =>
        {
            var quota = await projects.SetPreviewCacheQuotaAsync(request.Bytes);
            var root = projects.GetPreviewCacheDirectory();
            return new
            {
                directory = root,
                size = PreviewCachePaths.MeasureBytes(root),
                quota
            };
        });

        group.MapPost("/settings/preview-cache-clear", async Task<IResult> (IProjectService projects,
            ILoggerFactory loggerFactory) =>
        {
            var root = projects.GetPreviewCacheDirectory();
            var freed = PreviewCachePaths.MeasureBytes(root);
            try
            {
                PreviewCachePaths.Clear(root);
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger("settings").LogError(ex, "清空预览缓存失败 {root}", root);
                return Results.Problem("缓存目录正被占用，先关闭预览窗口再试。", statusCode: 409);
            }

            return Results.Json(new { directory = root, freed });
        });

        group.MapPost("/settings/backup-excludes-packages", async (ExcludePackages request,
            IProjectService projects) =>
        {
            await projects.SetBackupExcludesPackagesAsync(request.Exclude);
            return new { backupExcludesPackages = projects.BackupExcludesPackages };
        });

        group.MapPost("/settings/booth-download-dir", async (DirectoryRequest request,
            IBoothService booth) =>
        {
            await booth.SetDownloadDirectoryAsync(request.Directory);
            return new { directory = booth.GetDownloadDirectory() };
        });

        group.MapPost("/settings/assetstore-cache-dir", async (DirectoryRequest request,
            IAssetStoreService store) =>
        {
            await store.SetCacheDirectoryAsync(request.Directory);
            return new { directory = store.GetCacheDirectory() };
        });

        group.MapPost("/settings/assetstore-download-dir", async (DirectoryRequest request,
            IAssetStoreService store) =>
        {
            await store.SetDownloadDirectoryAsync(request.Directory);
            return new { directory = store.GetDownloadDirectory() };
        });

        group.MapPost("/settings/forget-session", (SiteRequest request, ISessionVault vault) =>
        {
            vault.Clear(request.Site);
            return new { forgotten = request.Site };
        });

        group.MapPost("/shell/open", (OpenRequest request) =>
        {
            if (request.Kind == "url")
            {
                if (!Uri.TryCreate(request.Target, UriKind.Absolute, out var link)
                    || (link.Scheme != Uri.UriSchemeHttp && link.Scheme != Uri.UriSchemeHttps))
                {
                    return Results.Problem("只允许打开 http / https 链接。", statusCode: 400);
                }

                Process.Start(new ProcessStartInfo(link.AbsoluteUri) { UseShellExecute = true });
                return Results.Json(new { opened = link.AbsoluteUri });
            }

            if (!Directory.Exists(request.Target))
                return Results.Problem("文件夹不存在。", statusCode: 404);

            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{request.Target}\"")
            {
                UseShellExecute = true
            });
            return Results.Json(new { opened = request.Target });
        });

        return group;
    }
}

public sealed record ExcludePackages(bool Exclude);

public sealed record QuotaRequest(long Bytes);

public sealed record SiteRequest(string Site);

public sealed record OpenRequest(string Target, string Kind = "folder");
