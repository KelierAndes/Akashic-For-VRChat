using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Akashic.Core.Models;
using Akashic.Core.Services;

namespace Akashic.ViewModels;

/// <summary>
/// ViewModel for the Projects list page.
/// </summary>
public partial class ProjectsViewModel : ObservableObject
{
    private readonly IProjectService _projectService;

    [ObservableProperty]
    private ObservableCollection<UnityProject> _projects = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ProjectsViewModel(IProjectService projectService)
    {
        _projectService = projectService;
    }

    [RelayCommand]
    public async Task LoadProjectsAsync()
    {
        IsLoading = true;
        StatusMessage = "正在加载项目...";

        try
        {
            var projects = await _projectService.GetProjectsAsync();
            Projects = new ObservableCollection<UnityProject>(projects);
            StatusMessage = $"已加载 {projects.Count} 个项目";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task AddProjectAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        IsLoading = true;
        try
        {
            var project = await _projectService.AddProjectAsync(path);
            if (project != null)
            {
                Projects.Add(project);
                StatusMessage = $"已添加项目: {project.Name}";
            }
            else
            {
                StatusMessage = "无效的 Unity 项目路径";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"添加失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task RemoveProjectAsync(UnityProject project)
    {
        await _projectService.RemoveProjectAsync(project.Path);
        Projects.Remove(project);
        StatusMessage = $"已移除项目: {project.Name}";
    }

    [RelayCommand]
    public async Task RefreshProjectAsync(UnityProject project)
    {
        var index = Projects.IndexOf(project);
        if (index < 0) return;

        var refreshed = await _projectService.RefreshProjectAsync(project);
        Projects[index] = refreshed;
    }
}
