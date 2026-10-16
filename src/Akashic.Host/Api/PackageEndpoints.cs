using Akashic.Core.Models;
using Akashic.Core.Services;

namespace Akashic.Host.Api;

/// <summary>
/// The VPM registry: browsing, searching and installing into a project. Anything
/// that touches a project is addressed by its folder path, like the rest of the API.
/// </summary>
public static class PackageEndpoints
{
    public static RouteGroupBuilder MapPackages(this RouteGroupBuilder group)
    {
        group.MapGet("/packages", (IVpmRegistryService registry, CancellationToken ct) =>
            registry.GetAllPackagesAsync(ct));

        group.MapGet("/packages/search", (string query, IVpmRegistryService registry, CancellationToken ct) =>
            registry.SearchPackagesAsync(query, ct));

        group.MapGet("/packages/detail", async Task<IResult> (string name, IVpmRegistryService registry,
            CancellationToken ct) =>
        {
            var package = await registry.GetPackageAsync(name, ct);
            return package is null
                ? Results.Problem("注册表里找不到这个包。", statusCode: 404)
                : Results.Json(package);
        });

        group.MapPost("/packages/install", async Task<IResult> (InstallRequest request,
            IProjectService projects, IVpmRegistryService registry, IPackageInstaller installer,
            EventHub hub, CancellationToken ct) =>
        {
            var project = await projects.ScanProjectAsync(request.Path);
            if (project is null) return Results.Problem("找不到该工程。", statusCode: 404);

            var package = await registry.GetPackageAsync(request.PackageName, ct);
            if (package is null) return Results.Problem("注册表里找不到这个包。", statusCode: 404);

            var id = Guid.NewGuid().ToString("N");
            hub.Publish("install.start", new { id, package = package.Name, project = project.Path });
            await installer.InstallVpmPackageAsync(project, package, hub.Progress("install", id), ct);
            hub.Publish("install.done", new { id, package = package.Name, project = project.Path });

            return Results.Json(await projects.ScanProjectAsync(request.Path));
        });

        group.MapPost("/packages/remove", async Task<IResult> (PackageRef request,
            IProjectService projects, IPackageInstaller installer, CancellationToken ct) =>
        {
            var project = await projects.ScanProjectAsync(request.Path);
            if (project is null) return Results.Problem("找不到该工程。", statusCode: 404);

            await installer.RemoveVpmPackageAsync(project, request.PackageName, ct);
            return Results.Json(await projects.ScanProjectAsync(request.Path));
        });

        // Registry order is resolution priority, so the list itself is the setting.
        group.MapGet("/repositories", (IVpmRegistryService registry) => new
        {
            urls = registry.GetRegistryUrls(),
            known = registry.GetKnownRepositories(),
        });

        group.MapPost("/repositories/add", async Task<IResult> (UrlRequest request,
            IVpmRegistryService registry, CancellationToken ct) =>
        {
            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var parsed)
                || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
                return Results.Problem("请输入合法的 http / https 仓库源 URL。", statusCode: 400);

            if (registry.GetRegistryUrls().Any(u => string.Equals(u, request.Url, StringComparison.OrdinalIgnoreCase)))
                return Results.Problem("这个仓库源已经在列表里了。", statusCode: 400);

            await registry.AddRegistryUrlAsync(request.Url);
            return Results.Json(new { urls = registry.GetRegistryUrls() });
        });

        group.MapPost("/repositories/remove", async (UrlRequest request, IVpmRegistryService registry) =>
        {
            await registry.RemoveRegistryUrlAsync(request.Url);
            return Results.Json(new { urls = registry.GetRegistryUrls() });
        });

        group.MapPost("/repositories/order", async (OrderRequest request, IVpmRegistryService registry,
            CancellationToken ct) =>
        {
            await registry.SaveRegistryOrderAsync(request.Urls, ct);
            return Results.Json(new { urls = registry.GetRegistryUrls() });
        });

        group.MapPost("/repositories/import-vcc", async (IVpmRegistryService registry, CancellationToken ct) =>
        {
            var before = registry.GetKnownRepositories().Count;
            var imported = await registry.ImportVccRepositoriesAsync();
            return new { before, imported, urls = registry.GetRegistryUrls() };
        });

        group.MapPost("/repositories/resolve", async Task<IResult> (UrlRequest request,
            IVpmRegistryService registry, CancellationToken ct) =>
            Results.Json(new
            {
                url = request.Url,
                name = await registry.FetchRepositoryNameAsync(request.Url, ct),
            }));

        return group;
    }
}

public sealed record UrlRequest(string Url);

public sealed record OrderRequest(List<string> Urls);

public sealed record InstallRequest(string Path, string PackageName);

public sealed record PackageRef(string Path, string PackageName);
