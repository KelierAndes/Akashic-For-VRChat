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
    Task<(bool Success, string Message, string? BackupPath)> CreateProjectBackupAsync(UnityProject project, IProgress<double>? progress = null);
}
