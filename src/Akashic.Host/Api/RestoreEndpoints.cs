using Akashic.Core.Models;
using Akashic.Core.Services;

namespace Akashic.Host.Api;

/// <summary>
/// The two ways a project comes back or goes out: the backup archives on disk, and the
/// hosting tools installed on the machine. Both address a project by its folder, and both
/// keep their answers in one shape so the page can render a list plus a result line.
/// </summary>
public static class RestoreEndpoints
{
    public static RouteGroupBuilder MapRestore(this RouteGroupBuilder group)
    {
        group.MapGet("/backups", async Task<IResult> (IProjectService projects) =>
        {
            var known = await projects.GetProjectsAsync();
            var archives = projects.ListBackupArchives();

            var rows = archives.Select(archive =>
            {
                var match = known.FirstOrDefault(p =>
                                string.Equals(p.Path, archive.ProjectName, StringComparison.OrdinalIgnoreCase))
                            ?? known.FirstOrDefault(p => archive.LooksLike(p.Name));

                return BackupRow.From(archive, match?.Name, match?.Path,
                    match != null && Directory.Exists(match.Path));
            }).ToList();

            return Results.Json(new BackupList(projects.GetBackupDirectory(), rows));
        });

        // The project cards want just a count, so this avoids shipping the whole archive
        // list to a page that shows one pill.
        group.MapGet("/backups/counts", async Task<IResult> (IProjectService projects) =>
        {
            var known = await projects.GetProjectsAsync();
            var archives = projects.ListBackupArchives();

            var counts = new Dictionary<string, int>();
            foreach (var project in known)
            {
                var matched = archives.Count(a => a.LooksLike(project.Name));
                if (matched > 0) counts[project.Path] = matched;
            }

            return Results.Json(new BackupCounts(counts));
        });

        group.MapPost("/backups/restore", async Task<IResult> (RestoreRequest request,
            IProjectService projects, EventHub hub, CancellationToken ct) =>
        {
            var archive = projects.ListBackupArchives()
                .FirstOrDefault(a => string.Equals(a.FilePath, request.FilePath, StringComparison.OrdinalIgnoreCase));

            if (archive is null) return Results.Problem("在备份目录里找不到这个压缩包。", statusCode: 404);
            if (string.IsNullOrWhiteSpace(request.TargetPath))
                return Results.Problem("请先选择还原到哪个工程目录。", statusCode: 400);

            var id = Guid.NewGuid().ToString("N");
            hub.Publish("restore.start", new { id, archive = archive.FileName, target = request.TargetPath });

            var (success, message) = await projects.RestoreBackupAsync(
                archive, request.TargetPath, request.Overwrite, hub.Progress("restore", id), ct);

            hub.Publish("restore.done", new { id, success, archive = archive.FileName });
            return Results.Json(new RestoreResult(success, message, request.TargetPath));
        });

        // refresh=true is what the settings page's 重新检测 sends: without it a tool installed
        // or removed outside this app would never be noticed.
        group.MapGet("/vcs/tools", async Task<object> (IVersionControlService vcs,
            CancellationToken ct, bool refresh = false) =>
        {
            if (refresh) vcs.ForgetTools();
            return await vcs.DetectToolsAsync(ct);
        });

        group.MapGet("/vcs/state", async Task<object> (string path, IVersionControlService vcs,
            bool deep = false, CancellationToken ct = default) =>
            await vcs.ReadStateAsync(path, deep, ct));

        // No paths means "every registered project", which is what the page opens with.
        group.MapPost("/vcs/states", async Task<object> (VcsPathsRequest request,
            IProjectService projects, IVersionControlService vcs, CancellationToken ct) =>
        {
            var paths = request.Paths is { Count: > 0 }
                ? request.Paths
                : (await projects.GetProjectsAsync()).Select(p => p.Path).ToList();

            var states = new List<ProjectVcsState>();
            foreach (var path in paths)
            {
                ct.ThrowIfCancellationRequested();
                states.Add(await vcs.ReadStateAsync(path, request.Deep, ct));
            }

            return states;
        });

        // An install runs winget, which may raise a UAC prompt; the caller waits on it.
        group.MapPost("/vcs/install", async Task<IResult> (VcsInstallRequest request,
            IVersionControlService vcs, CancellationToken ct) =>
            Results.Json(await vcs.InstallAsync(request.Tool, ct)));

        group.MapPost("/vcs/run", async Task<IResult> (VcsRunRequest request,
            IVersionControlService vcs, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Path))
                return Results.Problem("没有指定工程目录。", statusCode: 400);

            var result = request.Action switch
            {
                "init" => await vcs.InitializeAsync(request.Path, request.Kind ?? "git", ct),
                "gitignore" => await vcs.WriteGitignoreAsync(request.Path, ct),
                "lfs" => await vcs.EnableLfsAsync(request.Path, ct),
                "unlfs" => await vcs.DisableLfsAsync(request.Path, ct),
                "remote" => await vcs.SetRemoteAsync(request.Path, request.Url ?? string.Empty, ct),
                "submit" => await vcs.SubmitAsync(request.Path, request.Message ?? string.Empty,
                    request.Push, ct),
                "update" => await vcs.UpdateAsync(request.Path, ct),
                "push" => await vcs.PushAsync(request.Path, ct),
                _ => VcsRunResult.Fail(request.Action, request.Path, "未知的操作", request.Action)
            };

            return Results.Json(result);
        });

        return group;
    }
}

public sealed record BackupRow(
    string FilePath,
    string FileName,
    string ProjectName,
    string Source,
    string SizeDisplay,
    string ContentDisplay,
    string CreatedAtDisplay,
    bool Readable,
    string? ProjectMatch,
    string? ProjectPath,
    bool TargetExists)
{
    public static BackupRow From(BackupArchive archive, string? matchName, string? matchPath, bool exists) => new(
        archive.FilePath,
        archive.FileName,
        archive.ProjectName,
        archive.Source,
        archive.SizeDisplay,
        archive.ContentDisplay,
        archive.CreatedAtDisplay,
        archive.Readable,
        matchName,
        matchPath,
        exists);
}

public sealed record BackupList(string Directory, List<BackupRow> Items);

public sealed record BackupCounts(Dictionary<string, int> Counts);

public sealed record RestoreRequest(string FilePath, string TargetPath, bool Overwrite);

public sealed record RestoreResult(bool Success, string Message, string TargetPath);

public sealed record VcsPathsRequest(List<string>? Paths, bool Deep = false);

public sealed record VcsInstallRequest(string Tool);

public sealed record VcsRunRequest(
    string Action, string Path, string? Kind = null, string? Message = null, string? Url = null, bool Push = false);
