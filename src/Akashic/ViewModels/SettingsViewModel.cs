using System.Collections.ObjectModel;
using Akashic.Core.Models;
using Akashic.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Akashic.ViewModels;

/// <summary>
/// The settings page's own state: which Unity editors exist on this machine — so a project of any
/// version starts with the right one — and where project backups are written.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IProjectService _projects;

    public SettingsViewModel(IProjectService projects)
    {
        _projects = projects;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditorCount))]
    private ObservableCollection<UnityEditorEntry> _editors = [];

    [ObservableProperty]
    private string _backupDirectory = string.Empty;

    [ObservableProperty]
    private string _defaultProjectPath = string.Empty;

    /// <summary>Backed by the service, so a load that assigns the same value never writes back.</summary>
    [ObservableProperty]
    private bool _backupExcludesPackages;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public int EditorCount => Editors.Count;

    partial void OnBackupExcludesPackagesChanged(bool value)
    {
        if (value == _projects.BackupExcludesPackages) return;
        _ = ApplyBackupOptionAsync(value);
    }

    private async Task ApplyBackupOptionAsync(bool exclude)
    {
        try
        {
            await _projects.SetBackupExcludesPackagesAsync(exclude);
            Status = exclude
                ? "备份将只保留 Packages 的 manifest 清单，软件包本体不打包"
                : "备份将包含完整的 Packages 目录";
        }
        catch (Exception ex)
        {
            BackupExcludesPackages = _projects.BackupExcludesPackages;
            Status = $"设置备份选项失败：{ex.Message}";
        }
    }

    public async Task<string> SetDefaultProjectPathAsync(string directory)
    {
        try
        {
            await _projects.SetDefaultProjectPathAsync(directory);
            DefaultProjectPath = _projects.GetDefaultProjectPath();
            Status = $"新项目将创建在 {DefaultProjectPath}";
            return Status;
        }
        catch (Exception ex)
        {
            Status = $"设置默认项目路径失败：{ex.Message}";
            return Status;
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        try
        {
            var found = await Task.Run(_projects.DiscoverUnityEditors);

            Editors = new ObservableCollection<UnityEditorEntry>(found);
            BackupDirectory = _projects.GetBackupDirectory();
            DefaultProjectPath = _projects.GetDefaultProjectPath();
            BackupExcludesPackages = _projects.BackupExcludesPackages;
            Status = $"共识别到 {found.Count} 个 Unity 编辑器 · 备份写入 {BackupDirectory}";
        }
        catch (Exception ex)
        {
            Status = $"扫描失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Adds a hand-picked editor folder or exe; the manual entry then wins the version match.</summary>
    public async Task<string> AddEditorAsync(string pickedPath)
    {
        try
        {
            var message = await _projects.AddUnityEditorAsync(pickedPath);
            await RefreshAsync();
            // The refresh rewrites the summary line, so the result of the action goes last.
            Status = message;
            return message;
        }
        catch (Exception ex)
        {
            Status = $"添加失败：{ex.Message}";
            return Status;
        }
    }

    public async Task<string> RemoveEditorAsync(UnityEditorEntry entry)
    {
        try
        {
            var message = await _projects.RemoveUnityEditorAsync(entry.Path);
            await RefreshAsync();
            Status = message;
            return message;
        }
        catch (Exception ex)
        {
            Status = $"移除失败：{ex.Message}";
            return Status;
        }
    }

    public async Task<string> SetBackupDirectoryAsync(string directory)
    {
        try
        {
            await _projects.SetBackupDirectoryAsync(directory);
            BackupDirectory = _projects.GetBackupDirectory();
            Status = $"备份将写入 {BackupDirectory}";
            return Status;
        }
        catch (Exception ex)
        {
            Status = $"设置备份目录失败：{ex.Message}";
            return Status;
        }
    }
}
