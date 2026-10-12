using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using Akashic.Core.Models;
using Akashic.Core.Services;
using Akashic.ViewModels;

namespace Akashic.Views;

public sealed partial class SettingsPage : Page
{
    private readonly IThemeService _themeService = App.Services.GetRequiredService<IThemeService>();

    private bool _themeInitialized;
    private bool _editorsLoaded;

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Set the switch before wiring matters little — the handler only acts on real toggles —
        // but flag anyway so the initial assignment is never persisted back.
        ThemeSwitch.IsOn = _themeService.IsDarkTheme;
        _themeInitialized = true;

        // Editor discovery touches several registries and every fixed drive, so it runs once per
        // visit rather than on every navigation.
        if (!_editorsLoaded)
        {
            _editorsLoaded = true;
            _ = ViewModel.RefreshCommand.ExecuteAsync(null);
        }
    }

    private async void ThemeSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_themeInitialized) return;

        var dark = ThemeSwitch.IsOn;
        (App.MainWindow as MainWindow)?.ApplyTheme(dark);
        await _themeService.SetDarkThemeAsync(dark);
    }

    private SettingsViewModel ViewModel { get; } = App.Services.GetRequiredService<SettingsViewModel>();

    private BoothViewModel Booth { get; } = App.Services.GetRequiredService<BoothViewModel>();

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
        if (folder is not null) await Booth.SetDownloadDirectoryCommand.ExecuteAsync(folder);
    }

    private async void OpenBoothDirectory_Click(object sender, RoutedEventArgs e) =>
        await OpenFolderAsync(Booth.DownloadDirectory);

    private async void RefreshEditors_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.RefreshCommand.ExecuteAsync(null);

    private async void AddUnityEditor_Click(object sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync();
        if (folder is not null) await ViewModel.AddEditorAsync(folder);
    }

    private async void RemoveUnityEditor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: UnityEditorEntry entry }) return;

        await ViewModel.RemoveEditorAsync(entry);
    }

    private async void ChooseBackupDirectory_Click(object sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync();
        if (folder is not null) await ViewModel.SetBackupDirectoryAsync(folder);
    }

    private async void OpenBackupDirectory_Click(object sender, RoutedEventArgs e) =>
        await OpenFolderAsync(ViewModel.BackupDirectory);

    private async void ChooseDefaultProjectDirectory_Click(object sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync();
        if (folder is not null) await ViewModel.SetDefaultProjectPathAsync(folder);
    }

    private async void OpenDefaultProjectDirectory_Click(object sender, RoutedEventArgs e) =>
        await OpenFolderAsync(ViewModel.DefaultProjectPath);

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
