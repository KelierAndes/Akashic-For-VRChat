using System.Diagnostics;
using Akashic.Core.Models;
using Akashic.Core.Services;

namespace Akashic.Host.Api;

/// <summary>
/// Settings, and the two shell actions the shell needs on behalf of every page:
/// opening a folder in Explorer and a link in the default browser.
/// </summary>
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

        // Deliberately narrow: a link is only http(s) and a target is only an existing
        // folder. Shell-executing whatever path a caller names would turn this endpoint
        // into a way to run anything as this user.
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

public sealed record SiteRequest(string Site);

public sealed record OpenRequest(string Target, string Kind = "folder");
