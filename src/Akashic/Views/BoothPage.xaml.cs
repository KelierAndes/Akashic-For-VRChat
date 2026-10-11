using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Akashic.ViewModels;
using Windows.Storage.Pickers;
using Windows.System;

namespace Akashic.Views;

/// <summary>
/// Booth page. The WebView2 here is the only thing that touches Booth's site: it logs the user
/// in, renders the 已购列表 and exports the session cookies the downloads reuse.
/// </summary>
public sealed partial class BoothPage : Page
{
    // Booth fills part of the library rows in after the document reports itself loaded.
    private static readonly TimeSpan RenderPoll = TimeSpan.FromMilliseconds(350);

    public BoothViewModel ViewModel { get; }

    public BoothPage()
    {
        ViewModel = App.Services.GetRequiredService<BoothViewModel>();
        InitializeComponent();
        ViewModel.PageReader = ReadPageAsync;
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
        => await ViewModel.OpenLoginCommand.ExecuteAsync(null);

    private async void Read_Click(object sender, RoutedEventArgs e)
        => await ViewModel.LoadLibraryCommand.ExecuteAsync(null);

    private void PanelClose_Click(object sender, RoutedEventArgs e)
        => ViewModel.CloseLoginCommand.Execute(null);

    private async void AddFreeItem_Click(object sender, RoutedEventArgs e)
        => await ViewModel.AddFreeItemCommand.ExecuteAsync(null);

    private async void FreeItem_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        => await ViewModel.AddFreeItemCommand.ExecuteAsync(null);

    private void SelectAll_Click(object sender, RoutedEventArgs e)
        => ViewModel.ToggleSelectAllCommand.Execute(null);

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
        => ViewModel.ClearSelectionCommand.Execute(null);

    private async void DownloadItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: BoothItemRow row }) return;
        await ViewModel.DownloadItemCommand.ExecuteAsync(row);
    }

    private async void ParseItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: BoothItemRow row }) return;
        await ViewModel.ParseItemCommand.ExecuteAsync(row);
    }

    private async void DownloadSelected_Click(object sender, RoutedEventArgs e)
        => await ViewModel.DownloadSelectedCommand.ExecuteAsync(null);

    private async void ParseSelected_Click(object sender, RoutedEventArgs e)
    {
        var count = ViewModel.Items.Count(r => r.IsSelected && r.IsDownloaded);
        var dialog = new ContentDialog
        {
            Title = "解析所选商品",
            Content = $"解压勾选的 {count} 个压缩包，并把其中的 .unitypackage 登记到用户软件包、带 package.json 的目录登记到外部包。\n解压结果保存在下载目录中，不会删除原始文件。",
            PrimaryButtonText = "解析并入库",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.ParseSelectedCommand.ExecuteAsync(null);
    }

    private async void ChooseDirectory_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.Downloads };

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;

        await ViewModel.SetDownloadDirectoryCommand.ExecuteAsync(folder.Path);
    }

    private async void OpenDirectory_Click(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(ViewModel.DownloadDirectory))
            await Launcher.LaunchFolderAsync(await Windows.Storage.StorageFolder.GetFolderFromPathAsync(ViewModel.DownloadDirectory));
    }

    /// <summary>
    /// Navigates the WebView2 and resolves with the matching extractor's result once the page
    /// has rendered: the free-downloads page carries no download controls at all, so it needs
    /// its own wait condition and script. The session cookies are exported on the way out.
    /// </summary>
    private async Task<string> ReadPageAsync(string url)
    {
        var core = await EnsureCoreAsync();
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var freeLibrary = url.Contains("free_downloads", StringComparison.OrdinalIgnoreCase);

        void OnCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            core.NavigationCompleted -= OnCompleted;

            if (!args.IsSuccess)
                tcs.TrySetException(new InvalidOperationException($"Booth 页面加载失败（{args.WebErrorStatus}）。"));
            else
                _ = ExtractAsync(sender, tcs, freeLibrary);
        }

        core.NavigationCompleted += OnCompleted;
        core.Navigate(url);

        return await tcs.Task;
    }

    private async Task ExtractAsync(CoreWebView2 core, TaskCompletionSource<string> tcs, bool freeLibrary)
    {
        try
        {
            if (freeLibrary)
            {
                await WaitForFreeCardsAsync(core);
                var result = await core.ExecuteScriptAsync(ViewModel.FreeLibraryExtractorScript);
                await ExportCookiesAsync(core);
                tcs.TrySetResult(result);
            }
            else
            {
                await WaitForRowsAsync(core);
                var result = await core.ExecuteScriptAsync(ViewModel.ExtractorScript);
                await ExportCookiesAsync(core);
                tcs.TrySetResult(result);
            }
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }
    }

    /// <summary>
    /// Free library cards have no .js-download-button; they render as product links instead,
    /// so the wait looks for those. The tab strip also links to items, hence the length test.
    /// </summary>
    private async Task WaitForFreeCardsAsync(CoreWebView2 core)
    {
        const string Cards = "document.querySelectorAll('a[href*=\"/items/\"]').length";

        for (var attempt = 0; attempt < 6; attempt++)
        {
            if (await core.ExecuteScriptAsync(Cards) != "0") return;
            await Task.Delay(RenderPoll);
        }
    }

    /// <summary>
    /// Booth appends the download controls of each library row after the document itself has
    /// loaded, so the extractor waits for them instead of trusting one fixed pause.
    /// </summary>
    private async Task WaitForRowsAsync(CoreWebView2 core)
    {
        const string Rows = "document.querySelectorAll('.js-download-button').length";

        for (var attempt = 0; attempt < 6; attempt++)
        {
            if (await core.ExecuteScriptAsync(Rows) != "0") return;
            await Task.Delay(RenderPoll);
        }
    }

    private async Task ExportCookiesAsync(CoreWebView2 core)
    {
        var cookies = new Dictionary<string, string>(StringComparer.Ordinal);

        // .booth.pm covers both the store and the account pages, and the session cookie itself
        // is named after the deployment, so the whole set is handed over instead of one name.
        foreach (var origin in new[] { "https://booth.pm", "https://accounts.booth.pm" })
        {
            var list = await core.CookieManager.GetCookiesAsync(origin);
            foreach (var cookie in list)
                cookies[cookie.Name] = cookie.Value;
        }

        ViewModel.ApplySessionCookies(cookies);
    }

    private async Task<CoreWebView2> EnsureCoreAsync()
    {
        if (BoothWebView.CoreWebView2 is { } existing) return existing;

        await WaitForLoadedAsync();
        await BoothWebView.EnsureCoreWebView2Async();

        return BoothWebView.CoreWebView2
               ?? throw new InvalidOperationException("WebView2 未能初始化，请确认系统已安装 WebView2 运行时。");
    }

    private async Task WaitForLoadedAsync()
    {
        if (BoothWebView.IsLoaded) return;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnLoaded(object sender, RoutedEventArgs e) => tcs.TrySetResult(true);

        BoothWebView.Loaded += OnLoaded;
        try
        {
            await tcs.Task;
        }
        finally
        {
            BoothWebView.Loaded -= OnLoaded;
        }
    }
}
