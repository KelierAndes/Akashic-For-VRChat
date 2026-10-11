using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Akashic.ViewModels;
using Windows.Storage.Pickers;
using Windows.System;

namespace Akashic.Views;

/// <summary>
/// Asset Store import page. The WebView2 here owns the Unity login and renders 我的资产; the
/// downloaded .unitypackage files are registered straight into 用户软件包, the same flow the
/// Booth page follows. The 资源 page only lists what has already been fetched.
/// </summary>
public sealed partial class AssetStorePage : Page
{
    // Unity renders the 我的资产 cards after the document reports itself loaded.
    private static readonly TimeSpan RenderPoll = TimeSpan.FromMilliseconds(350);

    // 读取链路任一环卡死都不能把 IsLoading 一起拖死，所以整次页面读取有总时限。搜索页在
    // 隐藏的 WebView2 里渲染得很慢，时限放宽到两分钟。
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(120);

    // 面板打开期间轮询当前页面，登录跳转完成后自动接手读取。
    private static readonly TimeSpan WatchPoll = TimeSpan.FromSeconds(1.5);

    public AssetStoreViewModel ViewModel { get; }

    private DispatcherTimer? _unityWatchTimer;
    private bool _unityCapturing;

    public AssetStorePage()
    {
        ViewModel = App.Services.GetRequiredService<AssetStoreViewModel>();
        InitializeComponent();

        ViewModel.PageReader = ReadUnityPageAsync;
        ViewModel.SearchPageReader = ReadSearchPageAsync;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        // 入库/下载完成后，资源页的用户软件包与 Asset Store 分栏要跟着刷新。
        ViewModel.LibraryChanged += async (_, _) =>
        {
            var resources = App.Services.GetRequiredService<ResourcesViewModel>();
            await resources.LoadPackagesCommand.ExecuteAsync(null);
            await resources.LoadStoreAcquiredCommand.ExecuteAsync(null);
        };
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AssetStoreViewModel.IsLoginPanelOpen)) return;

        if (ViewModel.IsLoginPanelOpen) StartUnityWatch();
        else StopUnityWatch();
    }

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // The cache scan reads every archive on disk, so it waits for the page to be opened.
        if (!ViewModel.HasScanned)
            await ViewModel.LoadCacheCommand.ExecuteAsync(null);
    }

    private async void RefreshAssetStore_Click(object sender, RoutedEventArgs e)
        => await ViewModel.LoadCacheCommand.ExecuteAsync(null);

    private async void UnityLogin_Click(object sender, RoutedEventArgs e)
        => await ViewModel.OpenLoginCommand.ExecuteAsync(null);

    private async void UnityRead_Click(object sender, RoutedEventArgs e)
        => await ViewModel.LoadPurchasesCommand.ExecuteAsync(null);

    private void UnityPanelClose_Click(object sender, RoutedEventArgs e)
        => ViewModel.CloseLoginCommand.Execute(null);

    private void SelectAllAssets_Click(object sender, RoutedEventArgs e)
        => ViewModel.ToggleSelectAllAssetsCommand.Execute(null);

    private void ClearAssetSelection_Click(object sender, RoutedEventArgs e)
        => ViewModel.ClearAssetSelectionCommand.Execute(null);

    private async void AddSelectedToLibrary_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.AddSelectedToLibraryCommand.ExecuteAsync(null);
        Notify(ViewModel.Status);
    }

    private async void DownloadSelectedAsset_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.DownloadSelectedCommand.ExecuteAsync(null);
        Notify(ViewModel.Status);
    }

    private async void DownloadAsset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: AssetStoreRow row }) return;

        await ViewModel.DownloadAssetCommand.ExecuteAsync(row);
        Notify(ViewModel.Status);
    }

    private async void AddAssetToLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: AssetStoreRow row }) return;

        await ViewModel.AddToLibraryCommand.ExecuteAsync(row);
        Notify(ViewModel.Status);
    }

    private async void OpenAssetDirectory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: AssetStoreRow row }) return;

        var directory = Path.GetDirectoryName(row.LocalPathDisplay);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;

        await Launcher.LaunchFolderAsync(await Windows.Storage.StorageFolder.GetFolderFromPathAsync(directory));
    }

    private async void ChooseCacheDirectory_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;

        await ViewModel.SetCacheDirectoryCommand.ExecuteAsync(folder.Path);
        Notify(ViewModel.Status);
    }

    /// <summary>
    /// Navigates the Asset Store WebView2 and resolves with the extractor script's result once the
    /// page has rendered; the session cookies are exported on the way out.
    /// </summary>
    private async Task<string> ReadUnityPageAsync(string url)
    {
        var core = await EnsureUnityCoreAsync();
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            core.NavigationCompleted -= OnCompleted;

            // The redirect into Unity's sign-in flow usually reports a failed navigation even
            // though the login page itself rendered, so always extract and let the URL decide.
            _ = ExtractUnityAsync(sender, tcs);
        }

        core.NavigationCompleted += OnCompleted;
        core.Navigate(url);

        var read = tcs.Task;
        var done = await Task.WhenAny(read, Task.Delay(ReadTimeout));
        if (done != read) throw new InvalidOperationException("页面读取超时，请重试。");

        return await read;
    }

    /// <summary>
    /// Opens a storefront search page and resolves with the cover extractor's result once the
    /// result cards have rendered. Runs while the login panel is closed too — a hidden WebView2
    /// still navigates and renders.
    /// </summary>
    private async Task<string> ReadSearchPageAsync(string url)
    {
        var core = await EnsureUnityCoreAsync();
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            core.NavigationCompleted -= OnCompleted;

            if (!args.IsSuccess)
                tcs.TrySetException(new InvalidOperationException($"搜索页加载失败（{args.WebErrorStatus}）。"));
            else
                _ = ExtractSearchAsync(sender, tcs);
        }

        core.NavigationCompleted += OnCompleted;
        core.Navigate(url);

        var read = tcs.Task;
        var done = await Task.WhenAny(read, Task.Delay(ReadTimeout));
        if (done != read) throw new InvalidOperationException("搜索页读取超时。");

        return await read;
    }

    private async Task ExtractSearchAsync(CoreWebView2 core, TaskCompletionSource<string> tcs)
    {
        try
        {
            const string Cards = "document.querySelectorAll('article[data-test^=\"search-results-product-card-\"]').length";

            // The panel WebView2 is usually hidden at this point, and Chromium throttles hidden
            // pages heavily — the cards can take tens of seconds instead of the usual few.
            for (var attempt = 0; attempt < 90; attempt++)
            {
                if (await core.ExecuteScriptAsync(Cards) != "0") break;
                await Task.Delay(RenderPoll);
            }

            var result = await core.ExecuteScriptAsync(ViewModel.SearchCoverExtractorScript);
            await ExportUnityCookiesAsync(core);
            tcs.TrySetResult(result);
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }
    }

    private async Task ExtractUnityAsync(CoreWebView2 core, TaskCompletionSource<string> tcs)
    {
        try
        {
            await WaitForAssetRowsAsync(core);
            var result = await ExtractUnityPagesAsync(core);
            await ExportUnityCookiesAsync(core);
            tcs.TrySetResult(result);
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }
    }

    // WebView2's ExecuteScriptAsync does not wait for promises, so the page scripts stay
    // synchronous and the 下一页 pagination is walked from here instead.
    private const string UnityFirstRowScript =
        "(() => { const r = document.querySelector('[data-test^=\"asset-item-\"]');" +
        " return r ? r.getAttribute('data-test') : ''; })()";

    private const string UnityNextPageScript =
        "(() => { const b = document.querySelector('button[label=\"下一页\"], button[aria-label=\"下一页\"]');" +
        " if (!b || b.disabled) return 'no'; b.click(); return 'yes'; })()";

    /// <summary>Collects every rendered 我的资源 page into one JSON array of page results.</summary>
    private async Task<string> ExtractUnityPagesAsync(CoreWebView2 core)
    {
        var pages = new List<string>();

        for (var page = 0; page < 30; page++)
        {
            pages.Add(await core.ExecuteScriptAsync(ViewModel.ExtractorScript));

            var before = await core.ExecuteScriptAsync(UnityFirstRowScript);
            if (await core.ExecuteScriptAsync(UnityNextPageScript) != "\"yes\"") break;

            var moved = false;
            for (var wait = 0; wait < 25; wait++)
            {
                await Task.Delay(RenderPoll);
                var now = await core.ExecuteScriptAsync(UnityFirstRowScript);
                if (now != "null" && now != "\"\"" && now != before)
                {
                    moved = true;
                    break;
                }
            }

            if (!moved) break;
        }

        return pages.Count == 1 ? pages[0] : System.Text.Json.JsonSerializer.Serialize(pages);
    }

    /// <summary>
    /// Signing in makes the storefront move on its own: sometimes with a real redirect, sometimes
    /// only with client-side routing that reports no navigation at all. So while the panel is open
    /// the current page is polled, and 我的资产 is captured as soon as it appears — with its
    /// cookies, which is what lets the following read use the session. The watch only ends with a
    /// real list: stopping early would leave a fresh login unnoticed.
    /// </summary>
    private void StartUnityWatch()
    {
        if (_unityWatchTimer is not null) return;

        _unityWatchTimer = new DispatcherTimer { Interval = WatchPoll };
        _unityWatchTimer.Tick += UnityWatchTick;
        _unityWatchTimer.Start();
    }

    private void StopUnityWatch()
    {
        if (_unityWatchTimer is null) return;

        _unityWatchTimer.Stop();
        _unityWatchTimer.Tick -= UnityWatchTick;
        _unityWatchTimer = null;
    }

    private async void UnityWatchTick(object? sender, object e)
    {
        if (UnityWebView.CoreWebView2 is not { } core) return;
        if (ViewModel.IsLoading || _unityCapturing) return;

        var onList = await core.ExecuteScriptAsync("location.pathname.indexOf('account/assets')>=0 ? 1 : 0");
        if (onList != "1") return;

        _unityCapturing = true;
        try
        {
            await ExportUnityCookiesAsync(core);
            var page = await ExtractUnityPagesAsync(core);

            // 列表还没渲染就下一轮再看；读到东西才算数。
            if (ViewModel.CountPageItems(page) == 0) return;

            StopUnityWatch();
            await ViewModel.FinishFromPageAsync(page);
        }
        catch (Exception)
        {
            // 页面正在跳转，下一轮再看。
        }
        finally
        {
            _unityCapturing = false;
        }
    }

    /// <summary>
    /// The storefront renders 我的资源 rows as [data-test="asset-item-…"] divs long after the
    /// document reports itself loaded, so the extractor waits for one of them. Nothing else on the
    /// page carries that attribute, so a stray banner link can't satisfy the wait.
    /// </summary>
    private async Task WaitForAssetRowsAsync(CoreWebView2 core)
    {
        const string Rows = "document.querySelectorAll('[data-test^=\"asset-item-\"]').length";

        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (await core.ExecuteScriptAsync(Rows) != "0") return;

            // Unity's sign-in page never grows rows, so waiting there only delays the answer.
            if (await core.ExecuteScriptAsync("/login|passport|sign-in/.test(location.href)") == "true") return;

            await Task.Delay(RenderPoll);
        }
    }

    private async Task ExportUnityCookiesAsync(CoreWebView2 core)
    {
        var cookies = new Dictionary<string, string>(StringComparer.Ordinal);

        // Querying the storefront origin also returns the domain cookies a request to it would
        // carry, which is exactly the set the JSON API and the download endpoint need.
        foreach (var cookie in await core.CookieManager.GetCookiesAsync(ViewModel.MyAssetsUrl))
            cookies[cookie.Name] = cookie.Value;

        ViewModel.ApplySessionCookies(cookies);
    }

    private async Task<CoreWebView2> EnsureUnityCoreAsync()
    {
        if (UnityWebView.CoreWebView2 is { } existing) return existing;

        await WaitForUnityLoadedAsync();
        await UnityWebView.EnsureCoreWebView2Async();

        return UnityWebView.CoreWebView2
               ?? throw new InvalidOperationException("WebView2 未能初始化，请确认系统已安装 WebView2 运行时。");
    }

    private async Task WaitForUnityLoadedAsync()
    {
        if (UnityWebView.IsLoaded) return;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnLoaded(object sender, RoutedEventArgs e) => tcs.TrySetResult(true);

        UnityWebView.Loaded += OnLoaded;
        try
        {
            await tcs.Task;
        }
        finally
        {
            UnityWebView.Loaded -= OnLoaded;
        }
    }

    private void Notify(string message)
    {
        if (App.MainWindow is MainWindow mw && !string.IsNullOrEmpty(message))
            mw.ShowNotification("Asset Store", message, InfoBarSeverity.Informational);
    }
}
