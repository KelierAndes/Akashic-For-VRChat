using Akashic.Core.Models;

namespace Akashic.Core.Services;

/// <summary>
/// Service for managing Unity projects.
/// </summary>
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

    /// <summary>Editors known to this machine: manual list, Unity Hub, VCC and the scanned folders.</summary>
    List<UnityEditorEntry> DiscoverUnityEditors();

    /// <summary>Registers an editor picked by folder or exe path; returns a line for the status text.</summary>
    Task<string> AddUnityEditorAsync(string pickedPath);

    Task<string> RemoveUnityEditorAsync(string exePath);

    string GetBackupDirectory();

    Task SetBackupDirectoryAsync(string directory);

    /// <summary>Whether backups leave the package folders out and keep only the manifests.</summary>
    bool BackupExcludesPackages { get; }

    Task SetBackupExcludesPackagesAsync(bool exclude);

    /// <summary>Where new projects are created and where a folder scan looks for projects.</summary>
    string GetDefaultProjectPath();

    Task SetDefaultProjectPathAsync(string directory);

    Task<(bool Success, string Message, string? BackupPath)> CreateProjectBackupAsync(UnityProject project, IProgress<double>? progress = null);

    /// <summary>Templates saved from existing projects, newest first.</summary>
    List<ProjectTemplate> GetTemplates();

    /// <summary>The folder this app owns its template snapshots in.</summary>
    string GetTemplatesDirectory();

    /// <summary>Snapshots a project's ProjectSettings and package manifests as a reusable template.</summary>
    Task<string> SaveTemplateFromProjectAsync(UnityProject project, string name, string notes);

    /// <summary>Forgets a template and deletes the snapshot folder this app created for it.</summary>
    Task<string> DeleteTemplateAsync(ProjectTemplate template);

    /// <summary>Creates a new Unity project, optionally from a template, under the given parent folder.</summary>
    Task<(bool Success, string Message, string? ProjectPath)> CreateProjectAsync(
        string name, string? parentDirectory, ProjectTemplate? template, string unityVersion);

    /// <summary>Registers every Unity project found directly inside a folder. Returns added and already-known counts.</summary>
    Task<(int Added, int Already, string Message)> DetectProjectsAsync(string directory);
}
