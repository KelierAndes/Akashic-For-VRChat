using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Akashic.Core.Models;
using Akashic.Core.Services;
using Akashic.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace Akashic.Views;

public sealed partial class ProjectDetailPage : Page
{
    public ProjectDetailViewModel ViewModel { get; }
    private readonly IProjectService _projectService;

    private IReadOnlyList<string> _cloudBlueprintIds = [];

    public ProjectDetailPage()
    {
        ViewModel = App.Services.GetRequiredService<ProjectDetailViewModel>();
        _projectService = App.Services.GetRequiredService<IProjectService>();
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is UnityProject project)
        {
            ViewModel.SetProject(project);
            ProjectNameText.Text = project.Name;
            UnityVersionText.Text = project.UnityVersion;
            ProjectPathText.Text = $"位于: {project.Path}";

            _cloudBlueprintIds = project.CloudBlueprintIds;
            CloudManageButton.Visibility = _cloudBlueprintIds.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            CloudManageText.Text = project.CloudLinkLabel;
            ToolTipService.SetToolTip(CloudManageButton, project.CloudLinkTip);
        }
        base.OnNavigatedTo(e);
    }

    private void CloudManage_Click(object sender, RoutedEventArgs e)
    {
        if (_cloudBlueprintIds.Count == 0) return;

        if (App.MainWindow is MainWindow mw)
            mw.NavigateToVRChatContent(_cloudBlueprintIds);
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadPackagesAsync();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadPackagesAsync();
    }

    private async void UpgradeAll_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.UpgradeCount == 0)
        {
            if (App.MainWindow is MainWindow mw0)
                mw0.ShowNotification("提示", "所有软件包均已是最新版本，无需升级", InfoBarSeverity.Informational);
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "升级所有软件包",
            Content = $"确定要将全部 {ViewModel.UpgradeCount} 个可更新的软件包升级至最新版本吗？",
            PrimaryButtonText = "立即升级",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            ActionProgressBar.Visibility = Visibility.Visible;
            await ViewModel.UpgradeAllPackagesCommand.ExecuteAsync(null);
            ActionProgressBar.Visibility = Visibility.Collapsed;

            if (App.MainWindow is MainWindow mw)
                mw.ShowNotification("升级完成", ViewModel.StatusMessage, InfoBarSeverity.Success);
        }
    }

    private async void UpgradePackage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is UnifiedPackageItem item)
        {
            if (item.Category == PackageCategory.UnityPackage)
            {
                await ImportUnityPackageAsync(item);
                return;
            }

            ActionProgressBar.Visibility = Visibility.Visible;
            await ViewModel.UpgradePackageCommand.ExecuteAsync(item);
            ActionProgressBar.Visibility = Visibility.Collapsed;

            if (App.MainWindow is MainWindow mw)
                mw.ShowNotification("更新完成", ViewModel.StatusMessage, InfoBarSeverity.Success);
        }
    }

    private async void InstallPackage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is UnifiedPackageItem item)
        {
            if (item.Category == PackageCategory.UnityPackage)
            {
                await ImportUnityPackageAsync(item);
                return;
            }

            ActionProgressBar.Visibility = Visibility.Visible;
            await ViewModel.InstallPackageCommand.ExecuteAsync(item);
            ActionProgressBar.Visibility = Visibility.Collapsed;

            NotifyInstallResult();
        }
    }

    /// <summary>Parses the selected .unitypackage, lets the user tick assets, then writes them into the project.</summary>
    private async Task ImportUnityPackageAsync(UnifiedPackageItem item)
    {
        if (item.UnityPackageRef == null) return;

        if (!await ViewModel.PrepareUnityPackageImportAsync(item))
        {
            NotifyInstallResult();
            return;
        }

        ImportDialogSummary.Text = $"{item.UnityPackageRef.FileName} · {item.UnityPackageRef.SizeDisplay} · {ViewModel.ImportEntries.Count} 项";

        if (await UnityPackageImportDialog.ShowAsync() != ContentDialogResult.Primary)
        {
            ViewModel.CancelUnityPackageImport();
            return;
        }

        ActionProgressBar.Visibility = Visibility.Visible;
        await ViewModel.CommitUnityPackageImportAsync();
        ActionProgressBar.Visibility = Visibility.Collapsed;

        NotifyInstallResult();
    }

    private void NotifyInstallResult()
    {
        if (App.MainWindow is not MainWindow mw) return;

        var failed = ViewModel.StatusMessage.Contains("失败");
        mw.ShowNotification(failed ? "安装状态" : "安装完成", ViewModel.StatusMessage,
            failed ? InfoBarSeverity.Error : InfoBarSeverity.Success);
    }

    private void DialogSelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var entry in ViewModel.ImportEntries) entry.IsSelected = true;
    }

    private void DialogDeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var entry in ViewModel.ImportEntries) entry.IsSelected = false;
    }

    private void EntryCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is UnityPackageEntry entry)
            entry.IsSelected = cb.IsChecked ?? false;
    }

    private async void InstallSelected_Click(object sender, RoutedEventArgs e)
    {
        var (vpmCount, upCount, externalCount, skippedCount) = ViewModel.DescribeSelection();
        if (vpmCount + upCount + externalCount == 0)
        {
            if (App.MainWindow is MainWindow mw0)
                mw0.ShowNotification("提示", "勾选项均已安装且无需更新，没有可执行的操作", InfoBarSeverity.Informational);
            return;
        }

        var lines = new List<string>();
        if (vpmCount > 0) lines.Add($"安装 / 升级 {vpmCount} 个 VPM 软件包");
        if (upCount > 0) lines.Add($"导入 {upCount} 个 .unitypackage（写入其全部资产）");
        if (externalCount > 0) lines.Add($"附加 {externalCount} 个外部包（本地包写入 file: 引用，GitHub 包下载并嵌入）");
        if (skippedCount > 0) lines.Add($"跳过 {skippedCount} 个已安装或不适用的项");

        var dialog = new ContentDialog
        {
            Title = "批量安装所选软件包",
            Content = string.Join("\n", lines),
            PrimaryButtonText = "开始",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        ActionProgressBar.Visibility = Visibility.Visible;
        await ViewModel.InstallSelectedPackagesCommand.ExecuteAsync(null);
        ActionProgressBar.Visibility = Visibility.Collapsed;

        NotifyInstallResult();
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearSelectionCommand.Execute(null);
    }

    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ResetFilters();
    }

    private void OpenLibraryPage_Click(object sender, RoutedEventArgs e)
    {
        var tab = sender is FrameworkElement { Tag: UnifiedPackageItem { Category: PackageCategory.External } }
            ? ResourcesViewModel.ExternalTabIndex
            : ResourcesViewModel.UserPackageTabIndex;

        App.Services.GetRequiredService<ResourcesViewModel>().OpenTab(tab);
        if (App.MainWindow is MainWindow mw) mw.NavigateToTag("resources");
    }

    private async void UninstallPackage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is UnifiedPackageItem item)
        {
            var isUnityPackage = item.Category == PackageCategory.UnityPackage;
            var isExternal = item.Category == PackageCategory.External;
            var isGitRepo = isExternal && item.ExternalRef?.Kind != ExternalPackageKind.LocalFolder;
            var dialog = new ContentDialog
            {
                Title = "卸载软件包",
                Content = isUnityPackage
                    ? $"将删除 \"{item.DisplayName}\" 导入的 {item.InstallRecordRef?.InstalledPaths.Count ?? 0} 个文件与文件夹（含 .meta），确定继续吗？"
                    : isGitRepo
                        ? $"将从项目中删除 \"{item.DisplayName}\" 的下载副本（Packages/{item.ExternalRef?.Name}），外部包列表保持不变。确定继续吗？"
                        : isExternal
                            ? $"将从项目的 manifest.json 中移除 \"{item.DisplayName}\" 的引用，本地目录与外部包列表都不会改动。确定继续吗？"
                            : $"确定要从项目中移除 \"{item.DisplayName}\" 吗？",
                PrimaryButtonText = "卸载",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                ActionProgressBar.Visibility = Visibility.Visible;
                await ViewModel.RemovePackageCommand.ExecuteAsync(item);
                ActionProgressBar.Visibility = Visibility.Collapsed;

                if (App.MainWindow is MainWindow mw)
                    mw.ShowNotification("卸载状态", ViewModel.StatusMessage, InfoBarSeverity.Informational);
            }
        }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleSelectAllCommand.Execute(null);
    }

    private void CopyPackageId_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is string pkgId)
        {
            var dp = new DataPackage();
            dp.SetText(pkgId);
            Clipboard.SetContent(dp);

            if (App.MainWindow is MainWindow mw)
                mw.ShowNotification("已复制", $"包标识符 \"{pkgId}\" 已复制到剪贴板", InfoBarSeverity.Informational);
        }
    }

    private void OpenInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Project != null && Directory.Exists(ViewModel.Project.Path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = ViewModel.Project.Path,
                UseShellExecute = true
            });
        }
    }

    private void OpenPackagesFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Project != null && Directory.Exists(ViewModel.Project.PackagesPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = ViewModel.Project.PackagesPath,
                UseShellExecute = true
            });
        }
    }

    private void OpenAssetsFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Project != null && Directory.Exists(ViewModel.Project.AssetsPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = ViewModel.Project.AssetsPath,
                UseShellExecute = true
            });
        }
    }

    private void LaunchUnity_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Project != null)
        {
            var result = _projectService.LaunchUnity(ViewModel.Project);
            if (App.MainWindow is MainWindow mw)
            {
                mw.ShowNotification(
                    result.Success ? "Unity 正在启动" : "无法启动 Unity",
                    result.Message,
                    result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error);
            }
        }
    }

    private async void BackupProject_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Project != null)
        {
            if (App.MainWindow is MainWindow mw)
                mw.ShowNotification("正在备份", $"正在创建项目 \"{ViewModel.Project.Name}\" 的备份...", InfoBarSeverity.Informational);

            var (success, message, _) = await _projectService.CreateProjectBackupAsync(ViewModel.Project);

            if (App.MainWindow is MainWindow mw2)
            {
                mw2.ShowNotification(
                    success ? "备份成功" : "备份失败",
                    message,
                    success ? InfoBarSeverity.Success : InfoBarSeverity.Error);
            }
        }
    }

    private async void VersionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && cb.Tag is UnifiedPackageItem item && !string.IsNullOrEmpty(item.SelectedVersion))
        {
            if (item.IsInstalled && item.SelectedVersion != item.InstalledVersion)
            {
                var targetVersion = item.SelectedVersion;
                var dialog = new ContentDialog
                {
                    Title = "切换软件包版本",
                    Content = $"确定要将 \"{item.DisplayName}\" 的版本从 {item.InstalledVersion} 切换至 {targetVersion} 吗？",
                    PrimaryButtonText = "切换",
                    CloseButtonText = "取消",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = XamlRoot
                };

                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    ActionProgressBar.Visibility = Visibility.Visible;
                    await ViewModel.ChangePackageVersionCommand.ExecuteAsync((item, targetVersion));
                    ActionProgressBar.Visibility = Visibility.Collapsed;

                    if (App.MainWindow is MainWindow mw)
                        mw.ShowNotification("版本切换完成", ViewModel.StatusMessage, InfoBarSeverity.Success);
                }
                else
                {
                    item.SelectedVersion = item.InstalledVersion;
                }
            }
        }
    }
}
