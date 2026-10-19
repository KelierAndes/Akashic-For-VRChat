using Akashic.Core.Models;

namespace Akashic.Core.Services;

public interface IProjectService
{
    Task<List<UnityProject>> GetProjectsAsync();
    Task<UnityProject?> AddProjectAsync(string path);
    Task RemoveProjectAsync(string path);
    Task<UnityProject?> ScanProjectAsync(string path);
    Task<UnityProject> RefreshProjectAsync(UnityProject project);
    bool IsValidUnityProject(string path);
    (bool Success, string Message, string? PathUsed) LaunchUnity(UnityProject project);
    string? FindUnityEditor(string? version = null);

    List<UnityEditorEntry> DiscoverUnityEditors();

    Task<string> AddUnityEditorAsync(string pickedPath);

    Task<string> RemoveUnityEditorAsync(string exePath);

    string GetBackupDirectory();

    Task SetBackupDirectoryAsync(string directory);

    bool BackupExcludesPackages { get; }

    Task SetBackupExcludesPackagesAsync(bool exclude);

    string GetDefaultProjectPath();

    Task SetDefaultProjectPathAsync(string directory);

    Task<(bool Success, string Message, string? BackupPath)> CreateProjectBackupAsync(UnityProject project, IProgress<double>? progress = null);

    List<BackupArchive> ListBackupArchives();

    Task<(bool Success, string Message)> RestoreBackupAsync(
        BackupArchive archive, string targetProjectPath, bool overwrite,
        IProgress<double>? progress = null, CancellationToken ct = default);

    List<ProjectTemplate> GetTemplates();

    string GetTemplatesDirectory();

    Task<string> SaveTemplateFromProjectAsync(UnityProject project, string name, string notes);

    Task<string> DeleteTemplateAsync(ProjectTemplate template);

    Task<(bool Success, string Message, string? ProjectPath)> CreateProjectAsync(
        string name, string? parentDirectory, ProjectTemplate? template, string unityVersion);

    Task<(int Added, int Already, string Message)> DetectProjectsAsync(string directory);
}
