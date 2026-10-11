using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using Akashic.ViewModels;

namespace Akashic.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    private BoothViewModel ViewModel { get; } = App.Services.GetRequiredService<BoothViewModel>();

    private AssetStoreViewModel StoreViewModel { get; } = App.Services.GetRequiredService<AssetStoreViewModel>();

    private void OpenResources_Click(object sender, RoutedEventArgs e)
    {
        var tag = (sender as FrameworkElement)?.Tag as string;

        // Asset Store 的导入流程已挪到工具分栏的独立页面，资源页只保留已获取资源。
        if (tag == "assetstore")
        {
            (App.MainWindow as MainWindow)?.NavigateToTag("assetstore");
            return;
        }

        var tab = tag switch
        {
            "packages" => ResourcesViewModel.UserPackageTabIndex,
            _ => ResourcesViewModel.RepositoryTabIndex
        };

        App.Services.GetRequiredService<ResourcesViewModel>().OpenTab(tab);
        (App.MainWindow as MainWindow)?.NavigateToTag("resources");
    }

    private async void ChooseBoothDirectory_Click(object sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync();
        if (folder is not null) await ViewModel.SetDownloadDirectoryCommand.ExecuteAsync(folder);
    }

    private async void OpenBoothDirectory_Click(object sender, RoutedEventArgs e) =>
        await OpenFolderAsync(ViewModel.DownloadDirectory);

    private async void ChooseAssetStoreCache_Click(object sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync();
        if (folder is not null) await StoreViewModel.SetCacheDirectoryCommand.ExecuteAsync(folder);
    }

    private async void OpenAssetStoreCache_Click(object sender, RoutedEventArgs e) =>
        await OpenFolderAsync(StoreViewModel.CacheDirectory);

    private async void ChooseAssetStoreDownload_Click(object sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync();
        if (folder is not null) await StoreViewModel.SetDownloadDirectoryCommand.ExecuteAsync(folder);
    }

    private async void OpenAssetStoreDownload_Click(object sender, RoutedEventArgs e) =>
        await OpenFolderAsync(StoreViewModel.DownloadDirectory);

    /// <summary>The picker needs the main window's handle, so the choice is bound to it.</summary>
    private static async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.Downloads };

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private static async Task OpenFolderAsync(string path)
    {
        if (!Directory.Exists(path)) return;

        var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(path);
        await Windows.System.Launcher.LaunchFolderAsync(folder);
    }
}
