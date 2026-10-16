using Akashic.Core.Models;
using Akashic.Core.Services;

namespace Akashic.Host.Api;

/// <summary>
/// Local projects, the editors that open them, the templates new ones start from, and
/// the blueprint ids that tie them to the cloud. Everything is addressed by folder path,
/// which is the only identifier a Unity project really has.
/// </summary>
public static class ProjectEndpoints
{
    public static RouteGroupBuilder MapProjects(this RouteGroupBuilder group)
    {
        group.MapGet("/projects", (IProjectService projects) => projects.GetProjectsAsync());

        group.MapGet("/projects/detail", async Task<IResult> (string path, IProjectService projects) =>
            await Project(projects, path, async found => found));

        group.MapPost("/projects/add", async Task<IResult> (PathRequest request, IProjectService projects) =>
        {
            var added = await projects.AddProjectAsync(request.Path);
            return added is null
                ? NotFound("该目录不是一个 Unity 工程（缺少 Assets 与 ProjectSettings）。")
                : Results.Json(added);
        });

        group.MapPost("/projects/remove", async (PathRequest request, IProjectService projects) =>
        {
            await projects.RemoveProjectAsync(request.Path);
            return new { removed = request.Path };
        });

        group.MapPost("/projects/detect", async (DirectoryRequest request, IProjectService projects) =>
        {
            var (added, already, message) = await projects.DetectProjectsAsync(request.Directory);
            return new DetectResult(added, already, message);
        });

        group.MapPost("/projects/launch", async Task<IResult> (PathRequest request, IProjectService projects) =>
            await Project(projects, request.Path, found =>
            {
                var (success, message, editorPath) = projects.LaunchUnity(found);
                return new LaunchResult(success, message, editorPath);
            }));

        group.MapPost("/projects/backup", async Task<IResult> (PathRequest request, IProjectService projects,
            EventHub hub, CancellationToken ct) =>
            await Project(projects, request.Path, async found =>
            {
                var id = Guid.NewGuid().ToString("N");
                hub.Publish("backup.start", new { id, path = found.Path });

                var (success, message, backupPath) =
                    await projects.CreateProjectBackupAsync(found, hub.Progress("backup", id));

                hub.Publish("backup.done", new { id, path = found.Path, success });
                return new BackupResult(success, message, backupPath);
            }));

        group.MapPost("/projects/create", async Task<IResult> (CreateProjectRequest request,
            IProjectService projects) =>
        {
            ProjectTemplate? template = null;
            if (request.TemplateDirectory is not null)
            {
                template = projects.GetTemplates().FirstOrDefault(t =>
                    string.Equals(t.Directory, request.TemplateDirectory, StringComparison.OrdinalIgnoreCase));

                if (template is null) return BadRequest("模板不存在，可能已被删除。");
            }

            var (success, message, projectPath) = await projects.CreateProjectAsync(
                request.Name, request.ParentDirectory, template, request.UnityVersion);

            if (!success) return BadRequest(message);

            var created = projectPath is null ? null : await projects.ScanProjectAsync(projectPath);
            return Results.Json(new CreateResult(true, message, projectPath, created));
        });

        group.MapGet("/editors", (IProjectService projects) => projects.DiscoverUnityEditors());

        group.MapPost("/editors/add", async (PathRequest request, IProjectService projects) =>
            new ResultLine(await projects.AddUnityEditorAsync(request.Path)));

        group.MapPost("/editors/remove", async (ExePathRequest request, IProjectService projects) =>
            new ResultLine(await projects.RemoveUnityEditorAsync(request.ExePath)));

        group.MapGet("/blueprints", (IVRChatBlueprintScanService scan) => new { ids = scan.CloudIds });

        group.MapPost("/blueprints/publish", (CloudIdsRequest request, IVRChatBlueprintScanService scan) =>
        {
            scan.PublishCloudIds(request.Ids);
            return new { ids = scan.CloudIds };
        });

        // One project at a time: the shell renders the list first and fills the cloud
        // badge in as these come back, exactly like the desktop version does.
        group.MapPost("/blueprints/scan", async Task<IResult> (ScanBlueprintRequest request,
            IProjectService projects, IVRChatBlueprintScanService scan, CancellationToken ct) =>
            await Project(projects, request.Path, async found => new
            {
                path = found.Path,
                ids = await scan.ScanAsync(found, request.Force, ct)
            }));

        return group;
    }

    /// <summary>Runs <paramref name="action"/> against the project at that folder, or answers 404.</summary>
    private static async Task<IResult> Project<T>(IProjectService projects, string path,
        Func<UnityProject, Task<T>> action)
    {
        var found = await projects.ScanProjectAsync(path);
        return found is null ? NotFound("找不到该工程。") : Results.Json(await action(found));
    }

    private static async Task<IResult> Project<T>(IProjectService projects, string path,
        Func<UnityProject, T> action)
    {
        var found = await projects.ScanProjectAsync(path);
        return found is null ? NotFound("找不到该工程。") : Results.Json(action(found));
    }

    private static async Task<IResult> Project(IProjectService projects, string path,
        Func<UnityProject, Task> action)
    {
        var found = await projects.ScanProjectAsync(path);
        if (found is null) return NotFound("找不到该工程。");

        await action(found);
        return Results.Ok();
    }

    private static IResult NotFound(string detail) => Results.Problem(detail, statusCode: 404);

    private static IResult BadRequest(string detail) => Results.Problem(detail, statusCode: 400);
}

public sealed record PathRequest(string Path);

public sealed record ExePathRequest(string ExePath);

public sealed record DirectoryRequest(string Directory);

public sealed record ResultLine(string Message);

public sealed record DetectResult(int Added, int Already, string Message);

public sealed record LaunchResult(bool Success, string Message, string? EditorPath);

public sealed record BackupResult(bool Success, string Message, string? BackupPath);

public sealed record CreateResult(bool Success, string Message, string? ProjectPath, UnityProject? Project);

public sealed record CreateProjectRequest(
    string Name, string? ParentDirectory, string UnityVersion, string? TemplateDirectory);

public sealed record SaveTemplateRequest(string Path, string Name, string Notes);

public sealed record CloudIdsRequest(List<string> Ids);

public sealed record ScanBlueprintRequest(string Path, bool Force = false);
