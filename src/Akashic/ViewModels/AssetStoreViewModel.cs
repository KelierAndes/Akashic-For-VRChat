using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Akashic.Core.Models;
using Akashic.Core.Services;

namespace Akashic.ViewModels;

/// <summary>
/// ViewModel for the Asset Store tab: what Unity already has in a cache folder, plus what the
/// signed-in account owns. The WebView2 in the page owns the login and hands the session cookies
/// over; nothing Unity-related is written to settings.json except the two directories.
/// </summary>
public partial class AssetStoreViewModel : ObservableObject
{
    private readonly IAssetStoreService _store;
    private readonly IUnityPackageLibraryService _library;

    private List<AssetStoreRow> _allRows = [];
    private List<AssetStoreAsset> _accountItems = [];
    private bool _hasAccount;
    private bool _cachedAccountLoaded;
    private DateTime _cachedAccountAt;

    /// <summary>The account rows currently on screen come from the disk cache, not a live read.</summary>
    public bool CachedAccountLoaded => _cachedAccountLoaded;

    /// <summary>
    /// Supplied by the page: points the WebView2 at the 我的资产 page and returns the raw result of
    /// <see cref="ExtractorScript"/> once it has rendered, with the cookies exported on the way out.
    /// </summary>
    public Func<string, Task<string>>? PageReader { get; set; }

    /// <summary>
    /// Supplied by the page: opens a storefront search page and returns the raw result of
    /// <see cref="SearchCoverExtractorScript"/> once the result cards have rendered.
    /// </summary>
    public Func<string, Task<string>>? SearchPageReader { get; set; }

    /// <summary>Raised after files join 用户软件包, so the package tab can re-read its list.</summary>
    public event EventHandler? LibraryChanged;

    [ObservableProperty]
    private ObservableCollection<AssetStoreRow> _assets = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    [NotifyPropertyChangedFor(nameof(FilterNote))]
    [NotifyPropertyChangedFor(nameof(HasFilterNote))]
    private int _assetCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterNote))]
    [NotifyPropertyChangedFor(nameof(HasFilterNote))]
    private int _visibleCount;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Keeps the page from scanning the cache before its tab has ever been opened.</summary>
    [ObservableProperty]
    private bool _hasScanned;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyActionEnabled))]
    private bool _isWorking;

    [ObservableProperty]
    private bool _isLoginPanelOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionDisplay))]
    private bool? _isSignedIn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string _cacheDirectory = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string _downloadDirectory = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveDisplay))]
    private string _activeAssetName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveDisplay))]
    private double _downloadProgress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _status = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBatchActive))]
    [NotifyPropertyChangedFor(nameof(BatchSummary))]
    private int _selectedAssetCount;

    [ObservableProperty]
    private bool _isAllAssetsSelected;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private int _viewFilter;

    public AssetStoreViewModel(IAssetStoreService store, IUnityPackageLibraryService library)
    {
        _store = store;
        _library = library;
        _cacheDirectory = store.GetCacheDirectory();
        _downloadDirectory = store.GetDownloadDirectory();
    }

    public string ExtractorScript => _store.MyAssetsExtractorScript;

    public string SearchCoverExtractorScript => _store.SearchCoverExtractorScript;

    public string MyAssetsUrl => _store.MyAssetsUrl;

    /// <summary>
    /// 全部资源 / 入库状态 / 列表来源. Index 0 shows everything, which is what the tab starts with.
    /// </summary>
    public IReadOnlyList<string> ViewFilterOptions { get; } =
        ["全部资源", "仅未入库", "仅已缓存", "仅我的资产", "仅本地缓存"];

    public string SessionDisplay => IsSignedIn switch
    {
        true => "已登录 Unity",
        false => "未登录 Unity",
        _ => "尚未读取 Unity 账号"
    };

    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);
    public bool IsBatchActive => SelectedAssetCount > 0;
    public string BatchSummary => $"已选择 {SelectedAssetCount} 项";
    public bool IsAnyActionEnabled => !IsWorking;

    public string Summary => $"共 {AssetCount} 项资源 · 缓存目录 {CacheDirectory}";

    /// <summary>Only appears while a filter or a search hides rows, so the totals stay honest.</summary>
    public bool HasFilterNote => VisibleCount != AssetCount;

    public string FilterNote => HasFilterNote
        ? $"当前显示 {VisibleCount} / {AssetCount} 项"
        : string.Empty;

    public string ActiveDisplay => string.IsNullOrEmpty(ActiveAssetName)
        ? "准备传输"
        : $"正在下载 {ActiveAssetName} · {DownloadProgress:0}%";

    /// <summary>Called by the page after the WebView2 renders, so downloads reuse the live session.</summary>
    public void ApplySessionCookies(IReadOnlyList<StoredCookie> cookies) =>
        _store.ImportSession(cookies);

    [RelayCommand]
    public async Task LoadCacheAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        try
        {
            CacheDirectory = _store.GetCacheDirectory();
            DownloadDirectory = _store.GetDownloadDirectory();

            // The account list from the last session joins the scan, so rows and covers are on
            // screen without a storefront login; a fresh read overwrites it.
            if (!_hasAccount)
            {
                var cachedAccount = _store.LoadAccountCache();
                if (cachedAccount.Items.Count > 0)
                {
                    _accountItems = cachedAccount.Items;
                    _hasAccount = true;
                    _cachedAccountLoaded = true;
                    _cachedAccountAt = cachedAccount.SavedAt;
                }
            }

            await RebuildRowsAsync();
            HasScanned = true;
            Status = AssetCount == 0
                ? $"缓存目录里还没有 .unitypackage（{CacheDirectory}）"
                : CachedAccountLoaded
                    ? $"已加载 {AssetCount} 项资源（我的资产来自本地缓存 {_cachedAccountAt:yyyy/MM/dd HH:mm}）"
                    : $"已扫描 {AssetCount} 项本地资源";
        }
        catch (Exception ex)
        {
            Status = $"扫描失败：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task OpenLoginAsync()
    {
        IsLoginPanelOpen = true;
        await LoadPurchasesAsync();
    }

    [RelayCommand]
    public void CloseLogin() => IsLoginPanelOpen = false;

    [RelayCommand]
    public async Task LoadPurchasesAsync()
    {
        if (PageReader is null)
        {
            Status = "登录面板还没有就绪，请稍候再试。";
            return;
        }

        if (IsLoading) return;

        IsLoading = true;
        try
        {
            IsSignedIn = _store.HasSession;
            Status = "正在读取 Unity 我的资产…";

            // The cookies arrive with the page read, so the JSON API gets the first chance and the
            // rendered cards are the fallback when the storefront refuses the call.
            await CompleteReadAsync(await PageReader(_store.MyAssetsUrl));
        }
        catch (Exception ex)
        {
            IsLoginPanelOpen = true;
            Status = $"读取失败：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Takes a page the panel already rendered. Signing in inside the WebView2 makes the storefront
    /// navigate itself, so the list has to be readable without asking for a fresh navigation.
    /// </summary>
    public async Task FinishFromPageAsync(string extractorResultJson)
    {
        if (IsLoading) return;

        IsLoading = true;
        try
        {
            await CompleteReadAsync(extractorResultJson);
        }
        catch (Exception ex)
        {
            Status = $"读取失败：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>How many rows a page read carries, so the watcher can wait for a rendered list.</summary>
    public int CountPageItems(string extractorResultJson) =>
        _store.ParseMyAssets(extractorResultJson).Items.Count;

    private async Task CompleteReadAsync(string raw)
    {
        var scraped = _store.ParseMyAssets(raw);
        if (scraped.Items.Count == 0)
        {
            var page = string.IsNullOrWhiteSpace(scraped.ReadDiagnostics) ? "无页面数据" : scraped.ReadDiagnostics;
            IsSignedIn = false;
            IsLoginPanelOpen = true;
            Status = $"没有读到任何资产 · 当前页面：{page} · 请在面板中登录购买过的 Unity 账号后点「读取我的资产」。";
            return;
        }

        _accountItems = scraped.Items;
        _hasAccount = true;
        IsSignedIn = _store.HasSession;

        await RebuildRowsAsync();
        HasScanned = true;

        IsLoginPanelOpen = false;
        Status = $"已读取 {scraped.Items.Count} 项我的资产";

        try { await _store.SaveAccountCacheAsync(_accountItems); }
        catch { /* the snapshot is a convenience, not a requirement */ }
    }

    /// <summary>
    /// Merges the account list with what sits on disk. Account rows keep their own entry and pick up
    /// the local file; cache files no account row claims are appended so nothing scanned is lost.
    /// </summary>
    private async Task RebuildRowsAsync()
    {
        var cached = await _store.ScanCacheAsync();

        List<AssetStoreAsset> merged;
        if (!_hasAccount)
        {
            merged = cached;
        }
        else
        {
            _store.MergeCachedFiles(_accountItems, cached);

            var onDisk = _accountItems
                .Where(a => a.LocalPath is not null)
                .Select(a => a.LocalPath!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            merged = _accountItems
                .Concat(cached.Where(c => c.LocalPath is null || !onDisk.Contains(c.LocalPath)))
                .ToList();
        }

        foreach (var row in _allRows)
            row.PropertyChanged -= OnRowChanged;

        _allRows = merged
            .OrderBy(a => a.IsFromAccount ? 0 : 1)
            .ThenBy(a => a.Publisher, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(a => new AssetStoreRow(a))
            .ToList();

        // Cache rows the account list never named can still get a cover the service remembers.
        foreach (var row in _allRows.Where(r => !r.HasCover))
        {
            if (_store.TryGetCoverUrl(row.Name, out var url) &&
                Uri.TryCreate(url, UriKind.Absolute, out var cover))
            {
                try { row.CoverImage = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(cover); }
                catch { /* a dead URL just keeps the default glyph */ }
            }
        }

        AssetCount = _allRows.Count;
        IsAllAssetsSelected = false;
        ApplyFilter();

        _ = FillMissingCoversAsync();
    }

    private bool _fillingCovers;

    /// <summary>
    /// Cache-only rows carry no cover: the storefront search page is opened for each missing
    /// name in the panel's WebView2 and the first result card's key image is remembered. Best
    /// effort — a failed lookup just leaves the default glyph.
    /// </summary>
    private async Task FillMissingCoversAsync()
    {
        if (SearchPageReader is null || _fillingCovers) return;

        var pending = _allRows
            .Where(r => !r.HasCover)
            .Select(r => r.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct()
            .Where(n => !_store.TryGetCoverUrl(n, out _))
            .Take(20)
            .ToList();

        if (pending.Count == 0) return;

        _fillingCovers = true;
        var resolved = 0;
        try
        {
            for (var i = 0; i < pending.Count; i++)
            {
                var name = pending[i];
                Status = $"正在从 Asset Store 获取封面 {i + 1} / {pending.Count}：{name}";

                string url;
                try
                {
                    var raw = await SearchPageReader(_store.GetSearchUrl(name));
                    var covers = _store.ParseSearchCovers(raw);
                    var key = _store.MatchKey(name);
                    var hit = covers.FirstOrDefault(c => _store.MatchKey(c.Name) == key)
                              ?? covers.FirstOrDefault(c =>
                              {
                                  var ck = _store.MatchKey(c.Name);
                                  return ck.Length > 3 && (ck.Contains(key) || key.Contains(ck));
                              });

                    url = hit?.ImageUrl ?? string.Empty;
                    _store.CacheCoverUrl(name, url);
                }
                catch
                {
                    _store.CacheCoverUrl(name, string.Empty);
                    continue;
                }

                if (url.Length == 0 || !Uri.TryCreate(url, UriKind.Absolute, out var cover)) continue;
                resolved++;

                foreach (var row in _allRows.Where(r => !r.HasCover && _store.MatchKey(r.Name) == _store.MatchKey(name)))
                {
                    try { row.CoverImage = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(cover); }
                    catch { /* keep the glyph */ }
                }
            }
        }
        finally
        {
            _fillingCovers = false;
            if (resolved > 0) Status = $"已为 {resolved} 个未入库缓存资源补齐封面";
        }
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();
    partial void OnViewFilterChanged(int value) => ApplyFilter();

    private void ApplyFilter()
    {
        foreach (var row in _allRows)
            row.PropertyChanged -= OnRowChanged;

        IEnumerable<AssetStoreRow> query = _allRows;

        query = ViewFilter switch
        {
            1 => query.Where(r => !r.IsInLibrary),
            2 => query.Where(r => r.HasLocal),
            3 => query.Where(r => r.IsFromAccount),
            4 => query.Where(r => !r.IsFromAccount),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            query = query.Where(r =>
                r.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                r.FolderDisplay.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));
        }

        Assets = new ObservableCollection<AssetStoreRow>(query);

        foreach (var row in Assets)
            row.PropertyChanged += OnRowChanged;

        VisibleCount = Assets.Count;
        RefreshSelection();
    }

    private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AssetStoreRow.IsSelected))
            RefreshSelection();
    }

    private void RefreshSelection()
    {
        SelectedAssetCount = Assets.Count(r => r.IsSelected);
        IsAllAssetsSelected = Assets.Count > 0 && Assets.All(r => r.IsSelected);
    }

    [RelayCommand]
    public void ToggleSelectAllAssets()
    {
        // Each row change re-enters RefreshSelection, which would otherwise overwrite
        // IsAllAssetsSelected halfway through the loop.
        var selectAll = IsAllAssetsSelected;
        foreach (var row in Assets)
            row.IsSelected = selectAll;

        RefreshSelection();
    }

    [RelayCommand]
    public void ClearAssetSelection()
    {
        foreach (var row in Assets)
            row.IsSelected = false;

        RefreshSelection();
    }

    [RelayCommand]
    public async Task AddToLibraryAsync(AssetStoreRow? row)
    {
        if (row is not null) await AddRowsAsync([row]);
    }

    [RelayCommand]
    public async Task AddSelectedToLibraryAsync() =>
        await AddRowsAsync(Assets.Where(r => r.IsSelected && r.CanAddToLibrary).ToList());

    /// <summary>Registers the .unitypackage itself in 用户软件包, tagged as an Asset Store source.</summary>
    private async Task AddRowsAsync(IReadOnlyList<AssetStoreRow> rows)
    {
        if (rows.Count == 0)
        {
            Status = "没有可入库的项 · 只有已缓存到本地的资源能加入包列表。";
            return;
        }

        var stored = 0;
        var failures = new List<string>();

        foreach (var row in rows)
        {
            try
            {
                await _library.AddPackageFileAsync(row.Asset.LocalPath!, PackageOrigin.AssetStore);
                row.IsInLibrary = true;
                row.RowStatus = "已加入用户软件包";
                stored++;
            }
            catch (Exception ex)
            {
                failures.Add($"{row.Name}：{OneLine(ex.Message)}");
                row.RowStatus = "入库失败";
            }
        }

        if (stored > 0) LibraryChanged?.Invoke(this, EventArgs.Empty);

        Status = failures.Count == 0
            ? $"已把 {stored} 个 .unitypackage 加入用户软件包（来源记为 Asset Store）"
            : $"入库 {stored} / {rows.Count} · {string.Join(" · ", failures)}";
    }

    [RelayCommand]
    public async Task DownloadAssetAsync(AssetStoreRow? row)
    {
        if (row is not null) await DownloadRowsAsync([row]);
    }

    [RelayCommand]
    public async Task DownloadSelectedAsync() =>
        await DownloadRowsAsync(Assets.Where(r => r.IsSelected && r.CanDownload).ToList());

    private async Task DownloadRowsAsync(IReadOnlyList<AssetStoreRow> rows)
    {
        if (rows.Count == 0)
        {
            Status = "请先勾选要下载的资源。";
            return;
        }

        if (IsWorking) return;

        if (!_store.HasSession)
        {
            Status = "还没有 Unity 会话，请先点“登录 / 读取我的资产”。";
            return;
        }

        IsWorking = true;
        var done = 0;

        try
        {
            foreach (var row in rows)
            {
                ActiveAssetName = row.Name;
                DownloadProgress = 0;
                row.IsBusy = true;
                row.RowStatus = "下载中…";

                try
                {
                    var progress = new Progress<double>(value => DownloadProgress = Math.Round(value));
                    var path = await _store.DownloadAsync(row.Asset, progress);

                    // Booth 式处理：下载完成即登记进用户软件包，资源页因此能直接列出已获取的资源。
                    try
                    {
                        await _library.AddPackageFileAsync(path, PackageOrigin.AssetStore);
                        row.IsInLibrary = true;
                        row.RowStatus = "已下载并入库";
                    }
                    catch (Exception ex)
                    {
                        row.RowStatus = $"已下载 · 入库失败：{OneLine(ex.Message)}";
                    }

                    row.RefreshFromAsset();
                    done++;
                }
                catch (Exception ex)
                {
                    row.RowStatus = $"下载失败：{OneLine(ex.Message)}";
                }
                finally
                {
                    row.IsBusy = false;
                }
            }

            Status = done == rows.Count
                ? $"已下载 {done} 个文件到 {DownloadDirectory}"
                : $"下载完成 {done} / {rows.Count}，失败原因见状态列";

            if (done > 0) await LoadCacheAsync();
        }
        finally
        {
            IsWorking = false;
            ActiveAssetName = string.Empty;
            DownloadProgress = 0;
        }
    }

    [RelayCommand]
    public async Task SetCacheDirectoryAsync(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return;

        try
        {
            await _store.SetCacheDirectoryAsync(directory);
            CacheDirectory = _store.GetCacheDirectory();
            Status = $"缓存目录已设为 {CacheDirectory}";
            await LoadCacheAsync();
        }
        catch (Exception ex)
        {
            Status = $"目录设置失败：{ex.Message}";
        }
    }

    [RelayCommand]
    public async Task SetDownloadDirectoryAsync(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return;

        try
        {
            await _store.SetDownloadDirectoryAsync(directory);
            DownloadDirectory = _store.GetDownloadDirectory();
            Status = $"下载目录已设为 {DownloadDirectory}";
        }
        catch (Exception ex)
        {
            Status = $"目录设置失败：{ex.Message}";
        }
    }

    private static string OneLine(string message)
    {
        var flat = message.Replace("\r", " ").Replace("\n", " ").Trim();
        return flat.Length > 120 ? flat[..120] + "…" : flat;
    }
}

/// <summary>
/// One row of the Asset Store table. The backing asset mutates while files are downloaded, so the
/// local fields are observable copies kept in step by <see cref="RefreshFromAsset"/>.
/// </summary>
public partial class AssetStoreRow : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _rowStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLocal))]
    [NotifyPropertyChangedFor(nameof(LocalPathDisplay))]
    [NotifyPropertyChangedFor(nameof(StatusDisplay))]
    [NotifyPropertyChangedFor(nameof(CanAddToLibrary))]
    [NotifyPropertyChangedFor(nameof(Meta))]
    private string? _localPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeDisplay))]
    [NotifyPropertyChangedFor(nameof(Meta))]
    private long _size;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Meta))]
    private DateTime _lastModified;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusDisplay))]
    [NotifyPropertyChangedFor(nameof(CanAddToLibrary))]
    private bool _isInLibrary;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover))]
    private Microsoft.UI.Xaml.Media.ImageSource? _coverImage;

    public AssetStoreRow(AssetStoreAsset asset)
    {
        Asset = asset;
        _localPath = asset.LocalPath;
        _size = asset.Size;
        _lastModified = asset.LastModified;
        _isInLibrary = asset.IsInLibrary;

        // The store hands covers back as protocol-relative URLs ("//cdn…/x.png"), which Uri
        // refuses as absolute, so they are completed here before BitmapImage sees them.
        var coverUrl = asset.ImageUrl.StartsWith("//") ? "https:" + asset.ImageUrl : asset.ImageUrl;

        if (Uri.TryCreate(coverUrl, UriKind.Absolute, out var cover))
        {
            try { _coverImage = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(cover); }
            catch { _coverImage = null; }
        }
    }

    public AssetStoreAsset Asset { get; }

    public bool HasCover => CoverImage is not null;

    public string Name => Asset.Name;
    public string Publisher => Asset.Publisher;
    public string FolderDisplay => Asset.FolderDisplay;
    public bool IsFromAccount => Asset.IsFromAccount;
    public string OriginDisplay => Asset.IsFromAccount ? "我的资产" : "本地缓存";
    public string VersionDisplay => Asset.VersionDisplay;
    public string ProductIdDisplay => Asset.ProductId ?? "—";
    public string LocalPathDisplay => LocalPath ?? string.Empty;
    public bool HasLocal => !string.IsNullOrWhiteSpace(LocalPath) && File.Exists(LocalPath);
    public bool CanAddToLibrary => HasLocal && !IsInLibrary;
    public bool CanDownload => !string.IsNullOrWhiteSpace(Asset.ProductId);
    public string StatusDisplay => IsInLibrary ? "已入库" : HasLocal ? "已缓存" : "未下载";

    public string SizeDisplay => Size switch
    {
        >= 1048576 => $"{(double)Size / 1048576:F1} MB",
        >= 1024 => $"{(double)Size / 1024:F1} KB",
        > 0 => $"{Size} B",
        _ => "—"
    };

    public string Meta => HasLocal
        ? $"{SizeDisplay} · {Asset.LastModifiedDisplay}"
        : Asset.IsFromAccount ? "尚未下载到本地" : Asset.LastModifiedDisplay;

    public void RefreshFromAsset()
    {
        LocalPath = Asset.LocalPath;
        Size = Asset.Size;
        LastModified = Asset.LastModified;
        IsInLibrary = Asset.IsInLibrary;
    }
}
