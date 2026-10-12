using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Akashic.Core.Models;
using Akashic.Core.Services;

namespace Akashic.ViewModels;

/// <summary>
/// ViewModel for the Booth page. The WebView2 in the page owns the login and hands over both the
/// session cookies and the rendered 已购列表; this class turns those into rows, downloads the
/// archives, and files whatever the archives hold into 用户软件包 / 外部包.
/// </summary>
public partial class BoothViewModel : ObservableObject
{
    private readonly IBoothService _booth;
    private readonly IUnityPackageLibraryService _library;
    private readonly IExternalPackageService _external;

    private Dictionary<string, BoothPurchaseItem> _known = new(StringComparer.Ordinal);

    /// <summary>How many older versions the last read folded away, summed over products.</summary>
    private int _hiddenTotal;

    [ObservableProperty]
    private string _newItemUrl = string.Empty;

    /// <summary>
    /// Supplied by the page: points the WebView2 at a Booth URL and returns the raw result of
    /// <see cref="ExtractorScript"/> once the page has rendered.
    /// </summary>
    public Func<string, Task<string>>? PageReader { get; set; }

    [ObservableProperty]
    private ObservableCollection<BoothItemRow> _items = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private int _itemCount;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyActionEnabled))]
    private bool _isDownloading;

    [ObservableProperty]
    private bool _isLoginPanelOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionDisplay))]
    private bool? _isSignedIn;

    /// <summary>Whether an earlier run left a usable session behind, before Booth confirms it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionDisplay))]
    private bool _hasStoredSession;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string _downloadDirectory = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveDisplay))]
    private string _activeItemName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveDisplay))]
    private double _downloadProgress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _status = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBatchActive))]
    [NotifyPropertyChangedFor(nameof(BatchSummary))]
    private int _selectedItemCount;

    [ObservableProperty]
    private bool _isAllItemsSelected;

    /// <summary>Index into <see cref="KindFilterOptions"/>; 0 (可用软件包) hides demo videos and art packs.</summary>
    [ObservableProperty]
    private int _kindFilter;

    /// <summary>Full row list behind <see cref="Items"/>, kept so the kind filter can switch views.</summary>
    private List<BoothItemRow> _allItemRows = [];

    /// <summary>Free/gift lists mix in demo videos and illustration packs; index 0 keeps those out of sight.</summary>
    public IReadOnlyList<string> KindFilterOptions { get; } = ["可用软件包", "全部商品", "演示·图集"];

    partial void OnKindFilterChanged(int value) => ApplyKindFilter();

    private void ApplyKindFilter()
    {
        foreach (var row in Items)
            row.PropertyChanged -= OnRowChanged;

        Items = new ObservableCollection<BoothItemRow>(KindFilter switch
        {
            1 => _allItemRows,
            2 => _allItemRows.Where(r => r.LooksLikeMedia),
            _ => _allItemRows.Where(r => !r.LooksLikeMedia)
        });

        foreach (var row in Items)
            row.PropertyChanged += OnRowChanged;

        ItemCount = Items.Count;
        SelectedItemCount = 0;
        IsAllItemsSelected = false;
    }

    public BoothViewModel(IBoothService booth, IUnityPackageLibraryService library,
        IExternalPackageService external)
    {
        _booth = booth;
        _library = library;
        _external = external;
        _downloadDirectory = booth.GetDownloadDirectory();

        LoadCachedLibrary();
        RestoreSession();
    }

    /// <summary>
    /// Picks up the session an earlier run stored and asks Booth once whether it still works, so
    /// opening the page after a restart no longer reads as being signed out.
    /// </summary>
    private void RestoreSession()
    {
        if (!_booth.HasSession) return;

        HasStoredSession = true;
        _ = ProbeAsync();
    }

    private async Task ProbeAsync()
    {
        switch (await _booth.ProbeSessionAsync())
        {
            case true:
                IsSignedIn = true;
                Status = "已恢复上次保存的 Booth 登录态 · 点右上角读取最新列表";
                break;

            case false:
                HasStoredSession = false;
                IsSignedIn = false;
                Status = "保存的 Booth 登录态已失效，请重新登录后再读取。";
                break;

            // null means Booth could not be asked this time; the restored wording stands rather
            // than a flaky network appearing to log the user out.
        }
    }

    /// <summary>
    /// Restores the last full library read from disk, so covers and rows are on screen before
    /// any WebView2 navigation. A refresh simply overwrites the snapshot.
    /// </summary>
    private void LoadCachedLibrary()
    {
        try
        {
            var cache = _booth.LoadLibraryCache();
            if (cache.Items.Count == 0) return;

            var items = cache.Items;
            _booth.ReconcileLocalArchives(items);
            _known = items.ToDictionary(i => i.Number, i => i, StringComparer.Ordinal);
            var hidden = DeduplicateVersions(items);
            RebuildRows(items, hidden);
            Status = $"已从本地缓存加载（{cache.SavedAt:yyyy/MM/dd HH:mm}）· 点右上角读取最新列表";
        }
        catch
        {
            // A corrupt cache only costs the head start; the next read rebuilds it.
        }
    }

    public string ExtractorScript => _booth.PurchaseExtractorScript;

    public string FreeLibraryExtractorScript => _booth.FreeLibraryExtractorScript;

    public string ItemPageExtractorScript => _booth.ItemPageExtractorScript;

    public string GetLibraryUrl(int page) => _booth.GetLibraryUrl(page);

    /// <summary>
    /// Booth's own verdict from the last read: the library page only renders for a signed-in
    /// account, while a logged-out one is redirected to the sign-in form. Before any read this run,
    /// a session carried over from an earlier one gets its own wording rather than pretending to be
    /// a verdict it has not asked for yet.
    /// </summary>
    public string SessionDisplay => IsSignedIn switch
    {
        true => "已登录 Booth",
        false => "未登录 Booth",
        _ => HasStoredSession ? "已恢复保存的 Booth 登录态" : "尚未读取 Booth 账号"
    };

    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);
    public bool IsBatchActive => SelectedItemCount > 0;
    public string BatchSummary => $"已选择 {SelectedItemCount} 项";
    public bool IsAnyActionEnabled => !IsDownloading;
    public string Summary => $"共 {ItemCount} 个商品 · 保存到 {DownloadDirectory}" +
                             (_hiddenTotal > 0 ? $" · 已折叠 {_hiddenTotal} 个旧版本" : string.Empty);
    public string ActiveDisplay => string.IsNullOrEmpty(ActiveItemName)
        ? "准备下载"
        : $"正在下载 {ActiveItemName} · {DownloadProgress:0}%";

    /// <summary>Called by the page after every rendered Booth page so downloads reuse the live session.</summary>
    public void ApplySessionCookies(IReadOnlyList<StoredCookie> cookies) =>
        _booth.ImportSession(cookies);

    [RelayCommand]
    public async Task OpenLoginAsync()
    {
        IsLoginPanelOpen = true;
        await LoadLibraryAsync();
    }

    [RelayCommand]
    public void CloseLogin() => IsLoginPanelOpen = false;

    [RelayCommand]
    public async Task LoadLibraryAsync()
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
            var collected = new List<BoothPurchaseItem>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            Status = "正在读取 Booth 已购列表…";
            var first = await ReadPageAsync(1);

            // A logged-out account never reaches the library page, so the URL Booth finally
            // settled on is what says whether signing in is still pending.
            if (first.LoginRequired || !IsLibraryUrl(first.Url))
            {
                IsSignedIn = false;
                IsLoginPanelOpen = true;
                Status = $"还没有登录 Booth（当前页面：{first.Url}），请在面板中登录后点“读取已购列表”。";
                return;
            }

            IsSignedIn = true;
            Collect(collected, seen, first.Items);

            var lastPage = Math.Max(1, first.LastPage);
            for (var page = 2; page <= lastPage; page++)
            {
                Status = $"正在读取第 {page} / {lastPage} 页…";
                var next = await ReadPageAsync(page);
                if (next.LoginRequired || !IsLibraryUrl(next.Url))
                {
                    IsSignedIn = false;
                    Status = "读取途中登录失效，请重新登录 Booth。";
                    return;
                }

                Collect(collected, seen, next.Items);
            }

            var paidCount = collected.Count;

            // Gifted items live on their own page tree and carry working download controls, so
            // the purchase extractor reads them directly.
            var giftCount = 0;
            var giftsFirst = await ReadGiftsPageAsync(1);
            if (!giftsFirst.LoginRequired && IsLibraryUrl(giftsFirst.Url))
            {
                CollectFree(collected, seen, giftsFirst.Items);
                giftCount = collected.Count - paidCount;

                var giftsLastPage = Math.Max(1, giftsFirst.LastPage);
                for (var page = 2; page <= giftsLastPage; page++)
                {
                    Status = $"正在读取礼物列表第 {page} / {giftsLastPage} 页…";
                    var next = await ReadGiftsPageAsync(page);
                    if (next.LoginRequired || !IsLibraryUrl(next.Url)) break;

                    CollectFree(collected, seen, next.Items);
                }
            }

            // Free downloads have no download control on the library page at all: their cards
            // only name the product. Each product page is visited to pick up its downloadables.
            var freeMeta = new List<BoothPurchaseItem>();
            var freeFirstMeta = _booth.ParseFreeLibraryPage(await PageReader(_booth.GetFreeLibraryUrl(1)));
            if (!freeFirstMeta.LoginRequired && IsLibraryUrl(freeFirstMeta.Url))
            {
                CollectCards(freeMeta, freeFirstMeta.Items);

                var freeLastPage = Math.Max(1, freeFirstMeta.LastPage);
                for (var page = 2; page <= freeLastPage; page++)
                {
                    Status = $"正在读取免费领取列表第 {page} / {freeLastPage} 页…";
                    var next = _booth.ParseFreeLibraryPage(await PageReader(_booth.GetFreeLibraryUrl(page)));
                    if (next.LoginRequired || !IsLibraryUrl(next.Url)) break;

                    CollectCards(freeMeta, next.Items);
                }
            }

            var withoutDownload = 0;
            for (var i = 0; i < freeMeta.Count; i++)
            {
                Status = $"正在读取免费商品 {i + 1} / {freeMeta.Count}…";
                BoothLibraryPage page;
                try
                {
                    page = _booth.ParseItemPage(await PageReader(freeMeta[i].ItemUrl));
                }
                catch
                {
                    withoutDownload++;
                    continue;
                }

                if (page.Items.Count == 0)
                {
                    withoutDownload++;
                    continue;
                }

                // The library card's thumbnail is the product's real cover; it outranks whatever
                // the item page's og: tags offer, so it always wins for free items.
                foreach (var download in page.Items)
                {
                    download.IsFree = true;
                    if (!string.IsNullOrWhiteSpace(freeMeta[i].ImageUrl)) download.ImageUrl = freeMeta[i].ImageUrl;
                    if (string.IsNullOrWhiteSpace(download.Title)) download.Title = freeMeta[i].Title;
                    if (string.IsNullOrWhiteSpace(download.Shop)) download.Shop = freeMeta[i].Shop;
                    if (string.IsNullOrWhiteSpace(download.ShopUrl)) download.ShopUrl = freeMeta[i].ShopUrl;
                }

                CollectFree(collected, seen, page.Items);
            }

            var freeCount = collected.Count - paidCount - giftCount;

            _booth.ReconcileLocalArchives(collected);
            var hidden = DeduplicateVersions(collected);
            RebuildRows(collected, hidden);

            try { await _booth.SaveLibraryCacheAsync(collected); }
            catch { /* the snapshot is a convenience, not a requirement */ }

            // Keep the page visible when nothing came back so the account can be checked.
            IsLoginPanelOpen = collected.Count == 0;
            Status = collected.Count == 0
                ? $"已购列表是空的（页面：{first.Url}）· 确认一下登录的是购买过的账号。"
                : $"已读取 {collected.Count} 个商品（已购 {paidCount} · 礼物 {giftCount} · 免费 {freeCount}）" +
                  (withoutDownload > 0 ? $" · {withoutDownload} 个免费商品无下载文件" : string.Empty) +
                  (HiddenTotal == 0 ? string.Empty : $" · 同一商品的 {HiddenTotal} 个旧版本已折叠");
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

    private async Task<BoothLibraryPage> ReadPageAsync(int page)
    {
        var raw = await PageReader!(GetLibraryUrl(page));
        return _booth.ParseLibraryPage(raw);
    }

    private async Task<BoothLibraryPage> ReadGiftsPageAsync(int page)
    {
        var raw = await PageReader!(_booth.GetGiftsUrl(page));
        return _booth.ParseLibraryPage(raw);
    }

    private static void CollectCards(ICollection<BoothPurchaseItem> into, IEnumerable<BoothPurchaseItem> page)
    {
        foreach (var item in page)
            if (!into.Any(c => c.ProductKey == item.ProductKey))
                into.Add(item);
    }

    private static void CollectFree(ICollection<BoothPurchaseItem> into, ISet<string> seen,
        IEnumerable<BoothPurchaseItem> page)
    {
        foreach (var item in page)
        {
            item.IsFree = true;
            if (seen.Add(item.Number))
                into.Add(item);
        }
    }

    /// <summary>
    /// Adds a Booth product by its item URL — the entrance free items need, because they never
    /// show up in the purchase library. The WebView2 opens the product page, the extractor picks
    /// up every downloadable form it offers, and downloads then run anonymously when the item is
    /// free. Paid items still need a signed-in session.
    /// </summary>
    [RelayCommand]
    public async Task AddFreeItemAsync()
    {
        var raw = NewItemUrl.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            Status = "请粘贴 Booth 商品链接，例如 https://booth.pm/ja/items/123456。";
            return;
        }

        if (PageReader is null)
        {
            Status = "登录面板还没有就绪，请稍候再试。";
            return;
        }

        var itemMatch = System.Text.RegularExpressions.Regex.Match(raw, @"/items/(\d+)");
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var parsed) ||
            !parsed.Host.EndsWith("booth.pm", StringComparison.OrdinalIgnoreCase) ||
            !itemMatch.Success)
        {
            Status = "这不是 Booth 商品链接：地址需包含 booth.pm 与 /items/编号。";
            return;
        }

        if (IsDownloading) return;

        IsDownloading = true;
        try
        {
            Status = "正在读取商品页…";
            // The language-less canonical URL redirects to the shop page, where the extractor runs.
            var url = $"https://booth.pm/ja/items/{itemMatch.Groups[1].Value}";
            var page = _booth.ParseItemPage(await PageReader(url));

            if (page.Items.Count == 0)
            {
                Status = "商品页上没有读到可下载的文件：可能不是可下载类商品，或页面结构已变化。";
                return;
            }

            var merged = Items.Select(r => r.Item).ToList();
            var seen = new HashSet<string>(merged.Select(i => i.Number), StringComparer.Ordinal);

            var added = 0;
            foreach (var item in page.Items)
            {
                item.IsFree = true;
                if (seen.Add(item.Number))
                {
                    merged.Add(item);
                    added++;
                }
                else
                {
                    var existing = merged.First(i => i.Number == item.Number);
                    if (string.IsNullOrEmpty(existing.ImageUrl)) existing.ImageUrl = item.ImageUrl;
                    if (string.IsNullOrEmpty(existing.ItemUrl)) existing.ItemUrl = item.ItemUrl;
                }
            }

            // A product page can list several versions of the same download; keep the newest.
            var hidden = DeduplicateVersions(merged);
            RebuildRows(merged, hidden);
            NewItemUrl = string.Empty;

            Status = added == 0
                ? "该商品的下载项已经在列表中。"
                : $"已从商品页加入 {added} 个下载项" +
                  (_hiddenTotal > 0 ? $"（折叠了 {_hiddenTotal} 个旧版本）" : string.Empty) +
                  " · 免费文件无需登录即可下载。";
        }
        catch (Exception ex)
        {
            Status = $"读取商品页失败：{OneLine(ex.Message)}";
        }
        finally
        {
            IsDownloading = false;
        }
    }

    private static bool IsLibraryUrl(string url) => url.Contains("/library", StringComparison.OrdinalIgnoreCase);

    /// <summary>How many older versions the last read folded away, shown in the page summary.</summary>
    public int HiddenTotal => _hiddenTotal;

    /// <summary>
    /// One Booth product can carry several downloadable files — a bundle bought later, a rebuilt
    /// archive, a renamed re-upload — and the library page lists every one of them. Only the
    /// newest is kept: a file whose name states a version wins on that version, and otherwise the
    /// larger downloadables number wins, since Booth assigns those in upload order.
    /// </summary>
    private Dictionary<string, int> DeduplicateVersions(List<BoothPurchaseItem> items)
    {
        _hiddenTotal = 0;
        _knownHidden.Clear();

        var dropped = new List<BoothPurchaseItem>();

        foreach (var group in items.GroupBy(i => i.ProductKey).Where(g => g.Count() > 1))
        {
            var ordered = group.OrderByDescending(i => i,
                Comparer<BoothPurchaseItem>.Create((a, b) => CompareFreshness(a, b))).ToList();
            dropped.AddRange(ordered.Skip(1));
        }

        if (dropped.Count > 0)
        {
            items.RemoveAll(dropped.Contains);
            foreach (var group in dropped.GroupBy(i => i.ProductKey))
            {
                _hiddenTotal += group.Count();
                _knownHidden[group.Key] = group.Count();
            }
        }

        return _knownHidden;
    }

    /// <summary>Hidden-version counts by product key, so folded rows can say what happened.</summary>
    private readonly Dictionary<string, int> _knownHidden = new(StringComparer.Ordinal);

    private static int CompareFreshness(BoothPurchaseItem a, BoothPurchaseItem b)
    {
        var va = ParseVersion(a);
        var vb = ParseVersion(b);

        if (va is not null && vb is not null) return va.CompareTo(vb);
        if (va is not null) return 1;
        if (vb is not null) return -1;

        // No stated version: the bigger downloadables id was uploaded later.
        var na = long.TryParse(a.Number, out var x) ? x : 0;
        var nb = long.TryParse(b.Number, out var y) ? y : 0;
        return na.CompareTo(nb);
    }

    private static Version? ParseVersion(BoothPurchaseItem item)
    {
        foreach (var source in new[] { item.FormLabel, item.DisplayTitle })
        {
            if (string.IsNullOrWhiteSpace(source)) continue;

            var match = System.Text.RegularExpressions.Regex.Match(
                source, @"(?:^|[vV\s_\-\[])(\d{1,3}(?:\.\d{1,3}){1,3})(?:$|[\s_\-\]]|$)");
            if (match.Success && Version.TryParse(match.Groups[1].Value, out var parsed))
                return parsed;
        }

        return null;
    }

    private static void Collect(ICollection<BoothPurchaseItem> into, ISet<string> seen,
        IEnumerable<BoothPurchaseItem> page)
    {
        foreach (var item in page)
            if (seen.Add(item.Number))
                into.Add(item);
    }

    /// <summary>
    /// Rebuilds the table from scratch, but carries the downloaded archive over so a refresh
    /// does not forget what is already on disk.
    /// </summary>
    private void RebuildRows(IReadOnlyList<BoothPurchaseItem> items,
        IReadOnlyDictionary<string, int>? hidden = null)
    {
        var fresh = new Dictionary<string, BoothPurchaseItem>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            if (_known.TryGetValue(item.Number, out var old) && old.IsDownloaded)
            {
                item.LocalZipPath = old.LocalZipPath;
                item.Contents = old.Contents;
                item.ParsedSummary = old.ParsedSummary;
            }

            fresh[item.Number] = item;
        }

        _known = fresh;

        _allItemRows = items.Select(row =>
        {
            var entry = new BoothItemRow(row);
            if (hidden is not null && hidden.TryGetValue(row.ProductKey, out var folded) && folded > 0)
                entry.HiddenVersions = folded;
            return entry;
        }).ToList();

        ApplyKindFilter();
    }

    private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BoothItemRow.IsSelected))
            RefreshSelection();
    }

    private void RefreshSelection()
    {
        SelectedItemCount = Items.Count(r => r.IsSelected);
        IsAllItemsSelected = Items.Count > 0 && Items.All(r => r.IsSelected);
    }

    [RelayCommand]
    public void ToggleSelectAll()
    {
        // Each row change re-enters RefreshSelection, which would otherwise overwrite
        // IsAllItemsSelected halfway through the loop.
        var selectAll = IsAllItemsSelected;
        foreach (var row in Items)
            row.IsSelected = selectAll;

        RefreshSelection();
    }

    [RelayCommand]
    public void ClearSelection()
    {
        foreach (var row in Items)
            row.IsSelected = false;

        RefreshSelection();
    }

    [RelayCommand]
    public async Task DownloadItemAsync(BoothItemRow? row)
    {
        if (row is not null) await DownloadRowsAsync([row]);
    }

    [RelayCommand]
    public async Task DownloadSelectedAsync() =>
        await DownloadRowsAsync(Items.Where(r => r.IsSelected).ToList());

    private async Task DownloadRowsAsync(IReadOnlyList<BoothItemRow> rows)
    {
        if (rows.Count == 0)
        {
            Status = "请先勾选要下载的商品。";
            return;
        }

        if (IsDownloading) return;

        if (!_booth.HasSession)
        {
            Status = "还没有 Booth 会话，请先点“登录 / 读取已购列表”。";
            return;
        }

        IsDownloading = true;
        var done = 0;

        try
        {
            foreach (var row in rows)
            {
                ActiveItemName = row.Title;
                DownloadProgress = 0;
                row.IsBusy = true;
                row.Status = "下载中…";

                try
                {
                    var progress = new Progress<double>(value => DownloadProgress = Math.Round(value));
                    var path = await _booth.DownloadAsync(row.Item, progress);
                    row.IsDownloaded = true;
                    row.Status = $"已下载 · {FormatSize(path)}";
                    done++;
                }
                catch (Exception ex)
                {
                    row.IsDownloaded = row.Item.IsDownloaded;
                    row.Status = $"下载失败：{OneLine(ex.Message)}";
                }
                finally
                {
                    row.IsBusy = false;
                }
            }

            Status = done == rows.Count
                ? $"已下载 {done} 个文件到 {DownloadDirectory}"
                : $"下载完成 {done} / {rows.Count}，失败原因见状态列";
        }
        finally
        {
            IsDownloading = false;
            ActiveItemName = string.Empty;
            DownloadProgress = 0;
        }
    }

    [RelayCommand]
    public async Task ParseItemAsync(BoothItemRow? row)
    {
        if (row is not null) await ParseRowsAsync([row]);
    }

    [RelayCommand]
    public async Task ParseSelectedAsync() =>
        await ParseRowsAsync(Items.Where(r => r.IsSelected && r.IsDownloaded).ToList());

    /// <summary>
    /// Unpacks each archive and registers what it finds: .unitypackage files join 用户软件包,
    /// folders carrying a package.json join 外部包, and loose Assets folders are only reported
    /// because putting them into a Unity project needs the editor itself.
    /// </summary>
    private async Task ParseRowsAsync(IReadOnlyList<BoothItemRow> rows)
    {
        if (rows.Count == 0)
        {
            Status = "没有可解析的项 · 先下载勾选的商品。";
            return;
        }

        if (IsDownloading) return;

        IsDownloading = true;
        var parsed = 0;

        try
        {
            foreach (var row in rows)
            {
                ActiveItemName = row.Title;
                row.IsBusy = true;
                row.Status = "解析中…";

                try
                {
                    var contents = await _booth.InspectAsync(row.Item);
                    row.Status = await FileAsync(row, contents);
                    row.Item.ParsedSummary = row.Status;
                    row.IsParsed = contents.HasAnything;
                    parsed++;
                }
                catch (Exception ex)
                {
                    row.Status = $"解析失败：{OneLine(ex.Message)}";
                }
                finally
                {
                    row.IsBusy = false;
                }
            }

            Status = parsed == rows.Count
                ? $"已解析 {parsed} 个压缩包并写入本地库"
                : $"解析完成 {parsed} / {rows.Count}，失败原因见状态列";
        }
        finally
        {
            IsDownloading = false;
            ActiveItemName = string.Empty;
        }
    }

    private async Task<string> FileAsync(BoothItemRow row, BoothArchiveContents contents)
    {
        if (!contents.HasAnything)
            return "压缩包里没有找到 Unity 内容 · 解压结果保留在 " + contents.ExtractedPath;

        var unityPackages = 0;
        var upmPackages = 0;
        var problems = new List<string>();

        foreach (var file in contents.UnityPackageFiles)
        {
            try
            {
                await _library.AddPackageFileAsync(file, PackageOrigin.Booth);
                unityPackages++;
            }
            catch (Exception ex)
            {
                problems.Add($"{Path.GetFileName(file)}：{OneLine(ex.Message)}");
            }
        }

        foreach (var package in contents.UpmPackages)
        {
            try
            {
                await _external.AddLocalFolderAsync(package.Path, PackageOrigin.Booth);
                upmPackages++;
            }
            catch (Exception ex)
            {
                problems.Add($"{package.DisplayName}：{OneLine(ex.Message)}");
            }
        }

        var notes = new List<string>();
        if (unityPackages > 0) notes.Add($"{unityPackages} 个 .unitypackage → 用户软件包");
        if (upmPackages > 0) notes.Add($"{upmPackages} 个 UPM 包 → 外部包");
        if (contents.AssetFolders.Count > 0)
            notes.Add($"{contents.AssetFolders.Count} 个 Assets 目录需手动导入");
        if (problems.Count > 0)
            notes.Add($"{problems.Count} 项未入库");

        var summary = notes.Count == 0 ? "已解压，但没有可入库的内容" : string.Join(" · ", notes);
        if (problems.Count > 0)
            row.Detail = string.Join("\n", problems);

        return summary;
    }

    [RelayCommand]
    public async Task SetDownloadDirectoryAsync(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return;

        try
        {
            await _booth.SetDownloadDirectoryAsync(directory);
            DownloadDirectory = _booth.GetDownloadDirectory();
            Status = $"下载目录已设为 {DownloadDirectory}";
        }
        catch (Exception ex)
        {
            Status = $"目录设置失败：{ex.Message}";
        }
    }

    private static string FormatSize(string path)
    {
        try
        {
            var bytes = new FileInfo(path).Length;
            return bytes > 1024 * 1024 ? $"{bytes / 1024.0 / 1024.0:0.#} MB" : $"{bytes / 1024.0:0} KB";
        }
        catch (IOException)
        {
            return "已完成";
        }
    }

    private static string OneLine(string message)
    {
        var flat = message.Replace("\r", " ").Replace("\n", " ").Trim();
        return flat.Length > 120 ? flat[..120] + "…" : flat;
    }
}

/// <summary>One downloadable form of a purchased Booth product.</summary>
public partial class BoothItemRow : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isDownloaded;

    [ObservableProperty]
    private bool _isParsed;

    [ObservableProperty]
    private string _status = "未下载";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    private string _detail = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover))]
    private Microsoft.UI.Xaml.Media.ImageSource? _coverImage;

    /// <summary>How many older versions of this product were folded away by the newest-only rule.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHiddenVersions))]
    private int _hiddenVersions;

    public BoothItemRow(BoothPurchaseItem item)
    {
        Item = item;
        _isDownloaded = item.IsDownloaded;
        _isParsed = item.Contents is not null;
        if (_isDownloaded) Status = string.IsNullOrWhiteSpace(item.ParsedSummary) ? "已在本地" : item.ParsedSummary;

        var coverUrl = item.ImageUrl.StartsWith("//") ? "https:" + item.ImageUrl : item.ImageUrl;
        if (Uri.TryCreate(coverUrl, UriKind.Absolute, out var cover))
        {
            try { _coverImage = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(cover); }
            catch { _coverImage = null; }
        }
    }

    public BoothPurchaseItem Item { get; }

    public string Number => Item.Number;
    public string Title => Item.DisplayTitle;
    public string FormDisplay => Item.FormDisplay;
    public string ShopDisplay => string.IsNullOrWhiteSpace(Item.ShopHint) ? "—" : Item.ShopHint;
    public string LocalPathDisplay => Item.LocalZipPath ?? string.Empty;
    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
    public bool HasCover => CoverImage is not null;
    public bool IsFree => Item.IsFree;
    public string PriceDisplay => Item.IsFree ? "免费" : "已购";
    public bool HasHiddenVersions => HiddenVersions > 0;
    public string HiddenVersionsDisplay => $"已折叠 {HiddenVersions} 个旧版本";

    /// <summary>
    /// Free and gift lists mix in demo videos, illustration packs and comics that cannot be
    /// installed into Unity; titles and file labels are matched against those keywords (best
    /// effort only). The demo keywords stay narrow so animation packs whose names merely
    /// mention an intro video (紹介動画) are not thrown away.
    /// </summary>
    private static readonly string[] MediaKeywords =
    [
        // 演示 / 视频
        "デモ動画", "デモムービー", "サンプル動画", "紹介動画", "宣伝動画", "予告動画",
        "演示动画", "演示视频", "视频演示", "sample video", "demo video", "demo movie", "preview movie", "trailer",
        // 画像 / 图集 / 画集 / 漫画 / 小说
        "サンプル画像", "sample画像", "画像集", "sample image", "sample picture",
        "イラスト", "插画", "画集", "图集", "artbook", "art book", "fanbook", "ファンブック", "設定資料集",
        "写真集", "フォト集", "photo book", "photobook", "illustration",
        "コミック", "漫画", "comic", "小説", "小说", "novel"
    ];

    public bool LooksLikeMedia => MediaKeywords.Any(k =>
        (Item.DisplayTitle + " " + Item.FormLabel).Contains(k, StringComparison.OrdinalIgnoreCase));

    public string KindDisplay => LooksLikeMedia ? "演示·图集" : "软件包";
}
