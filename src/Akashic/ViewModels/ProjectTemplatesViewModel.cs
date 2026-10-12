using System.Collections.ObjectModel;
using Akashic.Core.Models;
using Akashic.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Akashic.ViewModels;

/// <summary>
/// 项目模板页：Creator Companion 自带的官方模板会自动列出，也可以把某个调好的工程存成模板，
/// 之后新建项目直接从它起步。自建模板只留 ProjectSettings 与 Packages 清单，软件包交给 Resolver 重新拉取。
/// </summary>
public partial class ProjectTemplatesViewModel : ObservableObject
{
    private readonly IProjectService _projects;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemplateCount))]
    private ObservableCollection<ProjectTemplate> _templates = [];

    [ObservableProperty]
    private string _status = "还没有模板 · 未检测到 Creator Companion 的官方模板，可以从项目页的「存为项目模板」生成一个";

    [ObservableProperty]
    private bool _isLoading;

    public int TemplateCount => Templates.Count;

    public ProjectTemplatesViewModel(IProjectService projects)
    {
        _projects = projects;
    }

    [RelayCommand]
    public void Refresh()
    {
        var found = _projects.GetTemplates();
        Templates = new ObservableCollection<ProjectTemplate>(found);

        var saved = found.Count(t => !t.IsBuiltIn);
        Status = found.Count == 0
            ? "还没有模板 · 未检测到 Creator Companion 的官方模板，可以从项目页的「存为项目模板」生成一个"
            : $"共 {found.Count} 个模板 · 内置 {found.Count - saved} · 自建 {saved}";
    }

    public async Task<string> DeleteAsync(ProjectTemplate template)
    {
        try
        {
            Status = await _projects.DeleteTemplateAsync(template);
            Refresh();
            return Status;
        }
        catch (Exception ex)
        {
            Status = $"删除失败：{ex.Message}";
            return Status;
        }
    }
}
