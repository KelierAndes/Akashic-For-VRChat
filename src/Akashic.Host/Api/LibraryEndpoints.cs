using Akashic.Core.Models;
using Akashic.Core.Services;

namespace Akashic.Host.Api;

public static class LibraryEndpoints
{
    public static RouteGroupBuilder MapLibrary(this RouteGroupBuilder group)
    {
        group.MapGet("/library/files", (IUnityPackageLibraryService library, CancellationToken ct) =>
            library.ScanAsync(ct));

        group.MapGet("/library/folders", (IUnityPackageLibraryService library) => library.GetLibraryFolders());

        group.MapGet("/library/added", (IUnityPackageLibraryService library) => library.GetAddedFiles());

        group.MapPost("/library/folders/add", async (PathRequest request,
            IUnityPackageLibraryService library, CancellationToken ct) =>
        {
            await library.AddLibraryFolderAsync(request.Path, ct);
            return Results.Json(library.GetLibraryFolders());
        });

        group.MapPost("/library/folders/remove", async (PathRequest request,
            IUnityPackageLibraryService library, CancellationToken ct) =>
        {
            await library.RemoveLibraryFolderAsync(request.Path, ct);
            return Results.Json(library.GetLibraryFolders());
        });

        group.MapPost("/library/files/add", async (AddPackageFile request,
            IUnityPackageLibraryService library, CancellationToken ct) =>
        {
            await library.AddPackageFileAsync(request.Path, request.Origin, ct);
            return Results.Json(await library.ScanAsync(ct));
        });

        group.MapPost("/library/files/remove", async (PathRequest request,
            IUnityPackageLibraryService library, CancellationToken ct) =>
        {
            await library.RemovePackageFileAsync(request.Path, ct);
            return Results.Json(await library.ScanAsync(ct));
        });

        group.MapGet("/library/records", async (string path, IUnityPackageLibraryService library,
            CancellationToken ct) => Results.Json(await library.GetRecordsAsync(path, ct)));

        group.MapPost("/library/describe", (DescribeRequest request,
            IUnityPackageLibraryService library) => Results.Json(library.DescribePackage(request.Path)));

        group.MapPost("/library/parse", async Task<IResult> (DescribeRequest request,
            IUnityPackageExtractor extractor, CancellationToken ct) =>
        {
            if (!File.Exists(request.Path)) return NotFound("文件不存在。");

            var archive = await extractor.ParseAsync(request.Path, ct);
            return Results.Json(new PackagePreview(
                archive.FilePath,
                archive.Name,
                archive.TotalSize,
                archive.FileCount,
                archive.DirectoryCount,
                archive.Entries.Select(entry => new PackageEntryView(
                    entry.PathName, entry.FileName, entry.Guid, entry.Size,
                    entry.IsDirectory, entry.HasMeta, entry.IsSelected)).ToList()));
        });

        group.MapPost("/library/install", async Task<IResult> (InstallPackageRequest request,
            IProjectService projects, IUnityPackageExtractor extractor, IPackageInstaller installer,
            IUnityPackageLibraryService library, EventHub hub, CancellationToken ct) =>
        {
            var project = await projects.ScanProjectAsync(request.Path);
            if (project is null) return NotFound("找不到该工程。");
            if (!File.Exists(request.PackagePath)) return NotFound("包文件不存在。");

            var archive = await extractor.ParseAsync(request.PackagePath, ct);
            if (request.SelectedPaths is { Count: > 0 })
            {
                var wanted = new HashSet<string>(request.SelectedPaths, StringComparer.Ordinal);
                foreach (var entry in archive.Entries) entry.IsSelected = wanted.Contains(entry.PathName);
            }

            var id = Guid.NewGuid().ToString("N");
            hub.Publish("package.start", new { id, package = archive.Name });
            var paths = await installer.InstallUnityPackageAsync(project, archive,
                hub.Progress("package", id), ct);
            hub.Publish("package.done", new { id, package = archive.Name, written = paths.Count });

            var file = library.DescribePackage(request.PackagePath);
            await library.SaveRecordAsync(project.Path, new UnityPackageInstallRecord
            {
                PackageId = file.PackageId,
                FilePath = file.FilePath,
                DisplayName = file.DisplayName,
                Version = file.Version,
                InstalledAt = DateTime.Now,
                FileCount = archive.Entries.Count(e => e.IsSelected),
                InstalledPaths = paths.ToList()
            });

            return Results.Json(await projects.ScanProjectAsync(request.Path));
        });

        group.MapPost("/library/uninstall", async Task<IResult> (UninstallPackageRequest request,
            IProjectService projects, IPackageInstaller installer, IUnityPackageLibraryService library,
            CancellationToken ct) =>
        {
            var project = await projects.ScanProjectAsync(request.Path);
            if (project is null) return Results.Problem("找不到该工程。", statusCode: 404);

            var records = await library.GetRecordsAsync(project.Path, ct);
            var record = records.FirstOrDefault(r =>
                string.Equals(r.PackageId, request.PackageId, StringComparison.OrdinalIgnoreCase));

            if (record is null)
                return Results.Problem("这个工程没有该包的导入台账，无法精确撤销。", statusCode: 400);

            await installer.RemoveUnityPackageAsync(project, record.InstalledPaths, ct);
            await library.RemoveRecordAsync(project.Path, record.PackageId, ct);
            return Results.Json(await projects.ScanProjectAsync(request.Path));
        });

        group.MapGet("/external", (IExternalPackageService external) => external.GetAll());

        group.MapPost("/external/add-folder", async (AddExternalFolder request,
            IExternalPackageService external, CancellationToken ct) =>
            Results.Json(await external.AddLocalFolderAsync(request.Path, request.Origin, ct)));

        group.MapPost("/external/add-repository", async (AddExternalRepository request,
            IExternalPackageService external, CancellationToken ct) =>
            Results.Json(await external.AddRepositoryAsync(request.Url, request.Reference, ct)));

        group.MapPost("/external/remove", async (IdRequest request, IExternalPackageService external,
            CancellationToken ct) =>
            new { removed = await external.RemoveAsync(request.Id, ct) });

        group.MapPost("/external/install", async Task<IResult> (ExternalInstallRequest request,
            IProjectService projects, IExternalPackageService external, EventHub hub, CancellationToken ct) =>
        {
            var project = await projects.ScanProjectAsync(request.Path);
            var package = external.GetAll().FirstOrDefault(p =>
                string.Equals(p.Id, request.Id, StringComparison.OrdinalIgnoreCase));

            if (project is null || package is null)
                return Results.Problem("找不到工程或外部包。", statusCode: 404);

            var id = Guid.NewGuid().ToString("N");
            hub.Publish("external.start", new { id, package = package.DisplayName });
            await external.InstallAsync(project, package, hub.Progress("external", id), ct);
            hub.Publish("external.done", new { id, package = package.DisplayName });

            return Results.Json(await projects.ScanProjectAsync(request.Path));
        });

        group.MapPost("/external/remove-from-project", async Task<IResult> (ExternalInstallRequest request,
            IProjectService projects, IExternalPackageService external, CancellationToken ct) =>
        {
            var project = await projects.ScanProjectAsync(request.Path);
            var package = external.GetAll().FirstOrDefault(p =>
                string.Equals(p.Id, request.Id, StringComparison.OrdinalIgnoreCase));

            if (project is null || package is null)
                return Results.Problem("找不到工程或外部包。", statusCode: 404);

            await external.RemoveFromProjectAsync(project, package, ct);
            return Results.Json(await projects.ScanProjectAsync(request.Path));
        });

        return group;
    }

    private static IResult NotFound(string detail) => Results.Problem(detail, statusCode: 404);
}

public sealed record PackageEntryView(
    string PathName,
    string FileName,
    string Guid,
    long Size,
    bool IsDirectory,
    bool HasMeta,
    bool IsSelected);

public sealed record PackagePreview(
    string FilePath,
    string Name,
    long TotalSize,
    int FileCount,
    int DirectoryCount,
    List<PackageEntryView> Entries);

public sealed record AddPackageFile(string Path, PackageOrigin Origin = PackageOrigin.Local);

public sealed record DescribeRequest(string Path);

public sealed record InstallPackageRequest(string Path, string PackagePath, List<string>? SelectedPaths);

public sealed record UninstallPackageRequest(string Path, string PackageId);

public sealed record IdRequest(string Id);

public sealed record AddExternalFolder(string Path, PackageOrigin Origin = PackageOrigin.Local);

public sealed record AddExternalRepository(string Url, string? Reference);

public sealed record ExternalInstallRequest(string Path, string Id);
