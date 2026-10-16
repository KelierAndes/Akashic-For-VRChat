using Akashic.Core.Models;
using Akashic.Core.Services;

namespace Akashic.Host.Api;

/// <summary>
/// Project templates, and the inputs the "new project" dialog needs. The display
/// strings live on <see cref="ProjectTemplate"/> as [JsonIgnore] computed members, so
/// they are resolved here rather than rebuilt in the frontend.
/// </summary>
public static class TemplateEndpoints
{
    public static RouteGroupBuilder MapTemplates(this RouteGroupBuilder group)
    {
        group.MapGet("/templates", (IProjectService projects) =>
            projects.GetTemplates().Select(TemplateRow.From).ToList());

        group.MapGet("/templates/directory", (IProjectService projects) =>
            new { directory = projects.GetTemplatesDirectory() });

        group.MapPost("/templates/save", async Task<IResult> (SaveTemplateRequest request,
            IProjectService projects) =>
        {
            var project = await projects.ScanProjectAsync(request.Path);
            if (project is null) return Results.Problem("找不到该工程。", statusCode: 404);

            var message = await projects.SaveTemplateFromProjectAsync(project, request.Name, request.Notes);
            return Results.Json(new ResultLine(message));
        });

        group.MapPost("/templates/delete", async Task<IResult> (DirectoryRequest request,
            IProjectService projects) =>
        {
            var template = projects.GetTemplates().FirstOrDefault(t =>
                string.Equals(t.Directory, request.Directory, StringComparison.OrdinalIgnoreCase));

            if (template is null) return Results.Problem("模板不存在。", statusCode: 404);
            if (!template.CanDelete) return Results.Problem("官方模板随 Creator Companion 提供，不能删除。", statusCode: 400);

            return Results.Json(new ResultLine(await projects.DeleteTemplateAsync(template)));
        });

        // Unity only loads projects from its own minor line, so the dialog picks a
        // version first and offers only the templates that match it.
        group.MapGet("/project-creation", (IProjectService projects) =>
        {
            var templates = projects.GetTemplates().Select(TemplateRow.From).ToList();

            var versions = projects.DiscoverUnityEditors()
                .Select(editor => editor.Version)
                .Where(version => !string.IsNullOrWhiteSpace(version))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(version => version, Comparer<string>.Create(BuiltinTemplates.CompareUnityVersions))
                .Select(version => new CreationVersion(
                    version,
                    templates.Any(t => t.IsBuiltIn && BuiltinTemplates.MatchesUnity(t.UnityVersion, version)),
                    templates.Where(t => BuiltinTemplates.MatchesUnity(t.UnityVersion, version)).ToList()))
                .ToList();

            var fallback = versions.FindIndex(v => v.Templates.Count > 0);
            var preferred = versions.FindIndex(v => v.IsBuiltIn);

            return new CreationInput(
                versions,
                preferred >= 0 ? preferred : fallback,
                projects.GetDefaultProjectPath());
        });

        return group;
    }
}

public sealed record TemplateRow(
    string Name,
    string Kind,
    string UnityVersion,
    string Notes,
    string Directory,
    string CreatedAt,
    string NotesDisplay,
    string SourceDisplay,
    string CreatedAtDisplay,
    string KindLabel,
    bool IsBuiltIn,
    bool CanDelete,
    bool IsAvailable)
{
    public static TemplateRow From(ProjectTemplate template) => new(
        template.Name,
        template.Kind,
        template.UnityVersion,
        template.Notes,
        template.Directory,
        template.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
        template.NotesDisplay,
        template.SourceDisplay,
        template.CreatedAtDisplay,
        template.KindLabel,
        template.IsBuiltIn,
        template.CanDelete,
        template.IsAvailable);
}

public sealed record CreationVersion(string Version, bool IsBuiltIn, List<TemplateRow> Templates);

public sealed record CreationInput(List<CreationVersion> Versions, int DefaultIndex, string DefaultProjectPath);
