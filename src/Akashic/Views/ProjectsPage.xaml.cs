using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Akashic.Core.Models;
using Akashic.Core.Services;
using Akashic.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace Akashic.Views;

public sealed partial class ProjectsPage : Page
{
    public ProjectsViewModel ViewModel { get; }
    private readonly IProjectService _projectService;

    public ProjectsPage()
    {
        ViewModel = App.Services.GetRequiredService<ProjectsViewModel>();
        _projectService = App.Services.GetRequiredService<IProjectService>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        LoadingRing.IsActive = true;
        await ViewModel.LoadProjectsCommand.ExecuteAsync(null);
        LoadingRing.IsActive = false;
        UpdateEmptyState();

        if (App.MainWindow is MainWindow mw)
            mw.UpdateProjectCount(ViewModel.Projects.Count);
    }

    private async void AddProject_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            await ViewModel.AddProjectCommand.ExecuteAsync(folder.Path);
            UpdateEmptyState();

            if (!string.IsNullOrEmpty(ViewModel.StatusMessage))
            {
                StatusInfoBar.Message = ViewModel.StatusMessage;
                StatusInfoBar.Severity = ViewModel.Projects.Count > 0
                    ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
                StatusInfoBar.IsOpen = true;
            }

            if (App.MainWindow is MainWindow mw)
                mw.UpdateProjectCount(ViewModel.Projects.Count);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        LoadingRing.IsActive = true;
        await ViewModel.LoadProjectsCommand.ExecuteAsync(null);
        LoadingRing.IsActive = false;
        UpdateEmptyState();

        if (App.MainWindow is MainWindow mw)
            mw.UpdateProjectCount(ViewModel.Projects.Count);
    }

    private void ProjectCard_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // Ignore if clicked on a button or menu
        if (e.OriginalSource is DependencyObject dep && IsDescendantOfButton(dep))
            return;

        if (sender is FrameworkElement element && element.DataContext is UnityProject project)
        {
            if (App.MainWindow is MainWindow mw)
                mw.NavigateToProjectDetail(project);
        }
    }

    private static bool IsDescendantOfButton(DependencyObject? obj)
    {
        while (obj != null)
        {
            if (obj is Button) return true;
            obj = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(obj);
        }
        return false;
    }

    private void LaunchUnity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is UnityProject project)
        {
            var result = _projectService.LaunchUnity(project);
            if (App.MainWindow is MainWindow mw)
            {
                mw.ShowNotification(
                    result.Success ? "Unity 正在启动" : "无法启动 Unity",
                    result.Message,
                    result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error);
            }
        }
    }

    private void ManageProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is UnityProject project)
        {
            if (App.MainWindow is MainWindow mw)
                mw.NavigateToProjectDetail(project);
        }
    }

    private async void BackupProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is UnityProject project)
        {
            if (App.MainWindow is MainWindow mw)
                mw.ShowNotification("正在备份", $"正在创建项目 \"{project.Name}\" 的备份...", InfoBarSeverity.Informational);

            var (success, message, _) = await _projectService.CreateProjectBackupAsync(project);

            if (App.MainWindow is MainWindow mw2)
            {
                mw2.ShowNotification(
                    success ? "备份成功" : "备份失败",
                    message,
                    success ? InfoBarSeverity.Success : InfoBarSeverity.Error);
            }
        }
    }

    private void OpenExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is UnityProject project)
        {
            if (Directory.Exists(project.Path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = project.Path,
                    UseShellExecute = true
                });
            }
        }
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is UnityProject project)
        {
            var dp = new DataPackage();
            dp.SetText(project.Path);
            Clipboard.SetContent(dp);

            if (App.MainWindow is MainWindow mw)
                mw.ShowNotification("已复制", $"项目路径已复制到剪贴板: {project.Path}", InfoBarSeverity.Informational);
        }
    }

    private async void RemoveProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is UnityProject project)
        {
            var dialog = new ContentDialog
            {
                Title = "移除项目",
                Content = $"确定要从列表中移除 \"{project.Name}\" 吗？\n（不会删除磁盘中的项目文件）",
                PrimaryButtonText = "移除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await ViewModel.RemoveProjectCommand.ExecuteAsync(project);
                UpdateEmptyState();

                if (App.MainWindow is MainWindow mw)
                    mw.UpdateProjectCount(ViewModel.Projects.Count);
            }
        }
    }

    private void UpdateEmptyState()
    {
        EmptyState.Visibility = ViewModel.Projects.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
        ProjectsScroller.Visibility = ViewModel.Projects.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;
    }
}
