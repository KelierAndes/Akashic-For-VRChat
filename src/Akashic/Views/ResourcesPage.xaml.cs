using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Akashic.ViewModels;
using Windows.Storage.Pickers;
using Windows.System;

namespace Akashic.Views;

public sealed partial class ResourcesPage : Page
{
    public ResourcesViewModel ViewModel { get; }

    public ResourcesPage()
    {
        ViewModel = App.Services.GetRequiredService<ResourcesViewModel>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAllCommand.ExecuteAsync(null);
    }

    private async void RefreshRepositories_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadRepositoriesCommand.ExecuteAsync(null);
    }

    private async void ImportFromVcc_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ImportFromVccCommand.ExecuteAsync(null);
        Notify(ViewModel.RepositoryStatus);
    }

    private async void RefreshPackages_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadPackagesCommand.ExecuteAsync(null);
    }

    private async void AddRepository_Click(object sender, RoutedEventArgs e)
    {
        var urlBox = new TextBox
        {
            PlaceholderText = "https://example.com/repo.json",
            Header = "存储库 URL"
        };

        var dialog = new ContentDialog
        {
            Title = "添加存储库",
            Content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    urlBox,
                    new TextBlock
                    {
                        Text = "存储库按列表顺序解析，可用每行的 ↑ / ↓ 调整解析优先级。",
                        Opacity = 0.75,
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            },
            PrimaryButtonText = "添加",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        await ViewModel.AddRepositoryCommand.ExecuteAsync(urlBox.Text);
        Notify(ViewModel.RepositoryStatus);
    }

    private async void RemoveRepository_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: RepositoryRow row }) return;

        var dialog = new ContentDialog
        {
            Title = "移除存储库",
            Content = $"确定要从配置中移除 \"{row.Name}\"？\n{row.Url}",
            PrimaryButtonText = "移除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.RemoveRepositoryCommand.ExecuteAsync(row);
            Notify(ViewModel.RepositoryStatus);
        }
    }

    private async void RemoveSelectedRepositories_Click(object sender, RoutedEventArgs e)
    {
        var count = ViewModel.SelectedRepositoryCount;
        var dialog = new ContentDialog
        {
            Title = "批量移除存储库",
            Content = $"确定要从配置中移除勾选的 {count} 个存储库？\n仅移除源地址，不会删除本地缓存文件。",
            PrimaryButtonText = "移除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.RemoveSelectedRepositoriesCommand.ExecuteAsync(null);
            Notify(ViewModel.RepositoryStatus);
        }
    }

    private void ClearRepositorySelection_Click(object sender, RoutedEventArgs e)
        => ViewModel.ClearRepositorySelectionCommand.Execute(null);

    private void SelectAllRepositories_Click(object sender, RoutedEventArgs e)
        => ViewModel.ToggleSelectAllRepositoriesCommand.Execute(null);

    private async void MoveRepositoryUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: RepositoryRow row }) return;

        await ViewModel.MoveRepositoryUpCommand.ExecuteAsync(row);
        Notify(ViewModel.RepositoryStatus);
    }

    private async void MoveRepositoryDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: RepositoryRow row }) return;

        await ViewModel.MoveRepositoryDownCommand.ExecuteAsync(row);
        Notify(ViewModel.RepositoryStatus);
    }

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder == null) return;

        await ViewModel.AddLibraryFolderCommand.ExecuteAsync(folder.Path);
        Notify(ViewModel.PackageStatus);
    }

    private async void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string folder }) return;

        await ViewModel.RemoveLibraryFolderCommand.ExecuteAsync(folder);
        Notify(ViewModel.PackageStatus);
    }

    private async void RemovePackage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: UserPackageRow row }) return;

        await ViewModel.RemovePackageCommand.ExecuteAsync(row);
        Notify(ViewModel.PackageStatus);
    }

    private async void RemoveSelectedPackages_Click(object sender, RoutedEventArgs e)
    {
        var count = ViewModel.SelectedPackageCount;
        var dialog = new ContentDialog
        {
            Title = "批量移出包列表",
            Content = $"确定从包列表移除勾选的 {count} 个软件包？\n只会移除库记录，磁盘上的 .unitypackage 文件不会被删除。",
            PrimaryButtonText = "移除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.RemoveSelectedPackagesCommand.ExecuteAsync(null);
            Notify(ViewModel.PackageStatus);
        }
    }

    private void ClearPackageSelection_Click(object sender, RoutedEventArgs e)
        => ViewModel.ClearPackageSelectionCommand.Execute(null);

    private void SelectAllPackages_Click(object sender, RoutedEventArgs e)
        => ViewModel.ToggleSelectAllPackagesCommand.Execute(null);

    // ---- Booth 导入 tab ----

    private void SelectAllBoothPackages_Click(object sender, RoutedEventArgs e)
        => ViewModel.ToggleSelectAllBoothPackagesCommand.Execute(null);

    private void ClearBoothPackageSelection_Click(object sender, RoutedEventArgs e)
        => ViewModel.ClearBoothPackageSelectionCommand.Execute(null);

    private async void RemoveBoothPackage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: UserPackageRow row }) return;

        await ViewModel.RemoveBoothPackageCommand.ExecuteAsync(row);
        Notify(ViewModel.PackageStatus);
    }

    private async void RemoveSelectedBoothPackages_Click(object sender, RoutedEventArgs e)
    {
        var count = ViewModel.SelectedBoothPackageCount;
        var dialog = new ContentDialog
        {
            Title = "批量移出包列表",
            Content = $"确定从包列表移除勾选的 {count} 个 Booth 导入软件包？\n只会移除库记录，磁盘上的 .unitypackage 文件不会被删除。",
            PrimaryButtonText = "移除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.RemoveSelectedBoothPackagesCommand.ExecuteAsync(null);
            Notify(ViewModel.PackageStatus);
        }
    }

    private void OpenBoothPage_Click(object sender, RoutedEventArgs e)
        => (App.MainWindow as MainWindow)?.NavigateToTag("booth");

    // ---- 外部包 tab ----

    private async void RefreshExternals_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadExternalCommand.ExecuteAsync(null);
    }

    private async void AddLocalPackage_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder == null) return;

        await ViewModel.AddLocalPackageCommand.ExecuteAsync(folder.Path);
        Notify(ViewModel.ExternalStatus);
    }

    private async void AddGitPackage_Click(object sender, RoutedEventArgs e)
    {
        var urlBox = new TextBox
        {
            PlaceholderText = "https://github.com|gitlab.com|gitee.com/所有者/仓库",
            Header = "仓库 URL（平台按地址自动识别）"
        };

        var dialog = new ContentDialog
        {
            Title = "添加代码仓库包",
            Content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    urlBox,
                    new TextBlock
                    {
                        Text = "支持 GitHub、GitLab（含自建实例）与 Gitee。仓库需包含 package.json（根目录或 Package / upm / src 等子目录）。地址带 /tree/分支 时按该分支解析，安装时下载 zip 快照并复制到项目的 Packages 目录。",
                        Opacity = 0.75,
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            },
            PrimaryButtonText = "添加",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        await ViewModel.AddGitPackageCommand.ExecuteAsync(urlBox.Text);
        Notify(ViewModel.ExternalStatus);
    }

    private async void RemoveExternal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ExternalPackageRow row }) return;

        var dialog = new ContentDialog
        {
            Title = "移除外部包",
            Content = $"确定从外部包列表移除 \"{row.Name}\"？\n{row.LocationDisplay}\n已安装到项目的引用不会被改动。",
            PrimaryButtonText = "移除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.RemoveExternalCommand.ExecuteAsync(row);
            Notify(ViewModel.ExternalStatus);
        }
    }

    private async void RemoveSelectedExternals_Click(object sender, RoutedEventArgs e)
    {
        var count = ViewModel.SelectedExternalCount;
        var dialog = new ContentDialog
        {
            Title = "批量移除外部包",
            Content = $"确定从外部包列表移除勾选的 {count} 个包？\n只移除列表记录，磁盘文件与项目引用都不会改动。",
            PrimaryButtonText = "移除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.RemoveSelectedExternalsCommand.ExecuteAsync(null);
            Notify(ViewModel.ExternalStatus);
        }
    }

    private void ClearExternalSelection_Click(object sender, RoutedEventArgs e)
        => ViewModel.ClearExternalSelectionCommand.Execute(null);

    private void SelectAllExternals_Click(object sender, RoutedEventArgs e)
        => ViewModel.ToggleSelectAllExternalsCommand.Execute(null);

    // ---- Asset Store（已获取）tab ----

    private async void RefreshStoreAcquired_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadStoreAcquiredCommand.ExecuteAsync(null);
    }

    private async void AddStoreAcquiredToLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: AssetStoreRow row }) return;

        await ViewModel.AddStoreAcquiredToLibraryCommand.ExecuteAsync(row);
        Notify(ViewModel.StoreStatus);
    }

    private async void OpenAssetDirectory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: AssetStoreRow row }) return;

        var directory = Path.GetDirectoryName(row.LocalPathDisplay);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;

        await Launcher.LaunchFolderAsync(await Windows.Storage.StorageFolder.GetFolderFromPathAsync(directory));
    }

    private void OpenAssetStorePage_Click(object sender, RoutedEventArgs e)
        => (App.MainWindow as MainWindow)?.NavigateToTag("assetstore");

    private void Notify(string message)
    {
        if (App.MainWindow is MainWindow mw && !string.IsNullOrEmpty(message))
            mw.ShowNotification("资源", message, InfoBarSeverity.Informational);
    }
}
