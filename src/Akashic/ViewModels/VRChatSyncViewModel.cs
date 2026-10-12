using System.Collections.ObjectModel;
using Akashic.Core.Models;
using Akashic.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Akashic.ViewModels;

/// <summary>
/// VRChat 数据同步页的视图模型：会话由页面里的 WebView2 登录提供，内容清单与改名改描述都走
/// VRChat 站点自己的 JSON API。Cookie 只留在内存，绝不写盘。
/// </summary>
public partial class VRChatSyncViewModel : ObservableObject
{
    private readonly IVRChatSyncService _sync;
    private readonly IVRChatBlueprintScanService _scan;
    private readonly IProjectService _projectService;

    private List<VRChatContentItem> _owned = [];

    /// <summary>Blueprint id → the local project whose scenes produce it.</summary>
    private Dictionary<string, UnityProject> _localMap = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>由页面提供：把 WebView2 的 vrchat.com Cookie 导出给服务。</summary>
    public Func<Task>? CookieExporter { get; set; }

    /// <summary>由页面提供：让 WebView2 打开 VRChat 登录页。</summary>
    public Func<Task>? LoginNavigator { get; set; }

    public string[] FilterOptions { get; } = ["全部内容", "仅虚拟形象", "仅世界"];

    /// <summary>Where the WebView2 has to go to sign in.</summary>
    public string LoginUrl => _sync.LoginUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RowCount))]
    private ObservableCollection<VRChatContentRow> _rows = [];

    /// <summary>Rows currently shown, so the page knows when to fall back to its empty hint.</summary>
    public int RowCount => Rows.Count;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccountDisplay))]
    private VRChatUser? _account;

    [ObservableProperty]
    private string _status = "尚未读取 VRChat 内容";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isLoginPanelOpen;

    [ObservableProperty]
    private string _filter = "全部内容";

    /// <summary>True while the table still shows the last list read rather than a fresh one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CacheNote))]
    private bool _isFromCache;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CacheNote))]
    private string _cacheSavedAt = string.Empty;

    public string CacheNote => IsFromCache && !string.IsNullOrWhiteSpace(CacheSavedAt)
        ? $"本地缓存 · {CacheSavedAt}"
        : string.Empty;

    /// <summary>
    /// Puts the last successfully read list on screen. Returns false when there is no cache, which is
    /// the only case where the page should claim it has nothing to show.
    /// </summary>
    public bool RestoreFromCache()
    {
        if (_owned.Count > 0) return false;

        var cached = _sync.LoadContentCache();
        if (!cached.HasContent) return false;

        _owned = cached.Items;
        CacheSavedAt = cached.SavedAtDisplay;
        IsFromCache = true;
        ApplyFilter();

        Status = $"显示 {cached.SavedAtDisplay} 读取的本地缓存 · 登录后点刷新可取最新";
        return true;
    }

    /// <summary>Hands the WebView2's exported cookies to the service, which also stores them encrypted.</summary>
    public void ApplySessionCookies(IReadOnlyList<StoredCookie> cookies) =>
        _sync.ImportSession(cookies);

    public string AccountDisplay => Account is null ? "未登录 VRChat" : $"已登录 {Account.DisplayName}";

    public VRChatSyncViewModel(IVRChatSyncService sync, IVRChatBlueprintScanService scan, IProjectService projectService)
    {
        _sync = sync;
        _scan = scan;
        _projectService = projectService;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsLoading) return;
        if (CookieExporter is not null) await CookieExporter();

        if (!_sync.HasSession)
        {
            var restored = RestoreFromCache();
            await ShowLoginAsync();

            if (!restored) Status = "还没有 VRChat 登录态 · 在面板中登录后会自动读取。";
            return;
        }

        IsLoading = true;
        try
        {
            Status = "正在读取 VRChat 我的内容…";

            Account = await _sync.GetSelfAsync();
            _owned = await _sync.GetMyContentAsync();
            IsLoginPanelOpen = false;

            await _sync.SaveContentCacheAsync(_owned);
            IsFromCache = false;

            var linked = await LinkLocalAsync();
            ApplyFilter();

            var avatars = _owned.Count(o => o.Kind == VRChatContentKind.Avatar);
            Status = $"已读取 {Account.DisplayName} 的 {avatars} 个虚拟形象 · {_owned.Count - avatars} 个世界"
                     + (linked > 0 ? $" · 匹配到 {linked} 项本地工程" : string.Empty);
        }
        catch (Exception ex)
        {
            Account = null;

            // A failed read must not throw away what the machine already knows about the account.
            RestoreFromCache();
            await ShowLoginAsync();

            Status = IsFromCache
                ? $"读取失败：{ex.Message} · 正在显示 {CacheSavedAt} 的本地缓存"
                : $"读取失败：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Publishes the cloud ids so the project pages can match in their direction too, then scans the
    /// registered projects for the ids their scenes carry. Returns how many of this account's
    /// contents have a local project behind them.
    /// </summary>
    private async Task<int> LinkLocalAsync()
    {
        _scan.PublishCloudIds(_owned.Select(o => o.Id));
        _localMap = new Dictionary<string, UnityProject>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var projects = await _projectService.GetProjectsAsync();
            _localMap = await _scan.MapByBlueprintAsync(projects);
        }
        catch (Exception ex)
        {
            Status = $"扫描本地工程失败：{ex.Message}";
            return 0;
        }

        // Carried onto the project too, so the detail page reached from here can jump straight back.
        foreach (var item in _owned)
        {
            if (!_localMap.TryGetValue(item.Id, out var project)) continue;
            if (!project.CloudBlueprintIds.Contains(item.Id, StringComparer.OrdinalIgnoreCase))
                project.CloudBlueprintIds.Add(item.Id);
        }

        return _owned.Count(o => _localMap.ContainsKey(o.Id));
    }

    /// <summary>
    /// Marks the rows a jump from a local project landed on. Returns how many were found, so the page
    /// can say so when the content has since left the account.
    /// </summary>
    public int Highlight(IEnumerable<string> blueprintIds)
    {
        var wanted = new HashSet<string>(blueprintIds, StringComparer.OrdinalIgnoreCase);
        var hit = 0;

        foreach (var row in Rows)
        {
            row.IsHighlighted = wanted.Contains(row.BlueprintId);
            if (row.IsHighlighted) hit++;
        }

        return hit;
    }

    /// <summary>Opens the panel and points the WebView2 at the sign-in page in one go.</summary>
    private async Task ShowLoginAsync()
    {
        IsLoginPanelOpen = true;

        if (LoginNavigator is { } navigate) await navigate();
    }

    /// <summary>
    /// Called by the page's watch while the login panel is open: once the browser holds a usable
    /// session, the read takes over without another click from the user.
    /// </summary>
    public async Task<bool> TryCaptureSessionAsync()
    {
        if (IsLoading || CookieExporter is null) return false;

        await CookieExporter();
        if (!_sync.HasSession) return false;

        try
        {
            var account = await _sync.GetSelfAsync();
        }
        catch (InvalidOperationException)
        {
            // Still on the sign-in page, or the session has not been issued yet.
            return false;
        }

        await LoadAsync();
        return true;
    }

    [RelayCommand]
    public async Task OpenLoginAsync() => await ShowLoginAsync();

    [RelayCommand]
    public void CloseLogin() => IsLoginPanelOpen = false;

    partial void OnFilterChanged(string value) => ApplyFilter();

    /// <summary>Opens the edit dialog with the row's live record, since list rows omit fields.</summary>
    public async Task<VRChatContentItem?> PrepareEditAsync(VRChatContentItem item)
    {
        try
        {
            var full = await _sync.GetContentAsync(item.Kind, item.Id);
            if (full is null) Status = $"{item.Name} 在 VRChat 上已经找不到了。";

            return full ?? item;
        }
        catch (Exception ex)
        {
            Status = $"读取详情失败：{ex.Message}";
            return item;
        }
    }

    public async Task<List<string>> GetAvatarStylesAsync()
    {
        if (_styles.Count > 0) return _styles;

        try
        {
            _styles.AddRange(await _sync.GetAvatarStylesAsync());
        }
        catch (Exception ex)
        {
            Status = $"读取形象风格失败:{ex.Message}";
        }

        return _styles;
    }

    private readonly List<string> _styles = [];

    public async Task<string> SaveAsync(VRChatContentEdit edit)
    {
        try
        {
            var saved = await _sync.UpdateContentAsync(edit);

            Replace(saved);
            Status = $"已更新 {saved.KindDisplay}“{saved.Name}”。";
            return Status;
        }
        catch (Exception ex)
        {
            Status = $"更新失败：{ex.Message}";
            return Status;
        }
    }

    /// <summary>Runs one of the row's action endpoints (publish, select, impostors, delete).</summary>
    public async Task<string> RunAsync(Func<Task> action, string doneMessage)
    {
        if (IsLoading) return Status;

        IsLoading = true;
        try
        {
            await action();
            Status = doneMessage;

            // Publish state and ids move rows, so the list is re-read after every action.
            await ReloadQuietlyAsync();
        }
        catch (Exception ex)
        {
            Status = $"{doneMessage}失败：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }

        return Status;
    }

    public Task<string> PublishAsync(VRChatContentItem item) =>
        RunAsync(() => _sync.PublishWorldAsync(item.Id), $"已提交发布 {item.Name}。");

    public Task<string> UnpublishAsync(VRChatContentItem item) =>
        RunAsync(() => _sync.UnpublishWorldAsync(item.Id), $"已取消发布 {item.Name}。");

    public Task<string> SelectAvatarAsync(VRChatContentItem item, bool fallback) =>
        RunAsync(() => _sync.SelectAvatarAsync(item.Id, fallback),
            fallback ? $"已将“{item.Name}”设为备用形象。" : $"已将“{item.Name}”设为当前形象。");

    public Task<string> GenerateImpostorsAsync(VRChatContentItem item, string platform) =>
        RunAsync(() => _sync.EnqueueImpostorAsync(item.Id, platform), $"已提交生成 {platform} 分身。");

    public Task<string> DeleteImpostorsAsync(VRChatContentItem item) =>
        RunAsync(() => _sync.DeleteImpostorsAsync(item.Id), $"已删除 {item.Name} 的分身。");

    public Task<string> DeleteAsync(VRChatContentItem item) =>
        RunAsync(() => _sync.DeleteContentAsync(item.Kind, item.Id), $"已删除 {item.Name}。");

    public Task<string> UploadCoverAsync(string filePath)
    {
        return UploadAndReportAsync(filePath);
    }

    private async Task<string> UploadAndReportAsync(string filePath)
    {
        try
        {
            return await _sync.UploadImageAsync(filePath);
        }
        catch (Exception ex)
        {
            Status = $"封面上传失败：{ex.Message}";
            throw;
        }
    }

    private void Replace(VRChatContentItem saved)
    {
        var index = _owned.FindIndex(o => o.Id == saved.Id);
        if (index >= 0) _owned[index] = saved;
        else _owned.Add(saved);

        ApplyFilter();
    }

    private async Task ReloadQuietlyAsync()
    {
        try
        {
            _owned = await _sync.GetMyContentAsync();
            ApplyFilter();
        }
        catch (Exception)
        {
            // The action itself already reported; a stale list is recoverable with a refresh.
        }
    }

    public void ApplyFilter()
    {
        var wanted = Filter switch
        {
            "仅虚拟形象" => (VRChatContentKind?)VRChatContentKind.Avatar,
            "仅世界" => (VRChatContentKind?)VRChatContentKind.World,
            _ => null
        };

        Rows = new ObservableCollection<VRChatContentRow>(
            _owned.Where(o => wanted is null || o.Kind == wanted)
                .OrderByDescending(o => o.UpdatedAt ?? DateTimeOffset.MinValue)
                .Select(o => new VRChatContentRow(o, _localMap.GetValueOrDefault(o.Id))));
    }
}

/// <summary>One table row: the API record plus the strings the page binds to.</summary>
public sealed partial class VRChatContentRow : ObservableObject
{
    public VRChatContentRow(VRChatContentItem item, UnityProject? localProject)
    {
        Item = item;
        LocalProject = localProject;

        // Covers are served from api.vrchat.cloud without authentication, and some records only
        // carry the full-size artwork, so the thumbnail falls back to it.
        var cover = string.IsNullOrWhiteSpace(item.ThumbnailImageUrl) ? item.ImageUrl : item.ThumbnailImageUrl;
        cover = cover.StartsWith("//") ? "https:" + cover : cover;

        if (Uri.TryCreate(cover, UriKind.Absolute, out var uri))
        {
            try { Cover = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(uri); }
            catch { Cover = null; }
        }
    }

    public VRChatContentItem Item { get; }

    /// <summary>The registered project whose scenes carry this content's blueprint id, if any.</summary>
    public UnityProject? LocalProject { get; }

    public bool HasLocalProject => LocalProject is not null;

    public string LocalProjectTip => LocalProject is null
        ? "没有扫描到产出这份内容的本地工程"
        : $"打开产出它的 Unity 工程：{LocalProject.Name}（{LocalProject.Path}）";

    /// <summary>Set when a jump from a local project landed on this row.</summary>
    [ObservableProperty]
    private bool _isHighlighted;

    public Microsoft.UI.Xaml.Media.ImageSource? Cover { get; }

    public bool HasCover => Cover is not null;

    public string KindDisplay => Item.KindDisplay;

    public string Name => Item.Name;

    public string BlueprintId => Item.BlueprintId;

    public string StatusDisplay => Item.StatusDisplay;

    public string ExtraDisplay => Item.ExtraDisplay;

    public string UnityVersionDisplay => Item.UnityVersionDisplay;

    public string UpdatedDisplay => Item.UpdatedDisplay;

    public string Description => Item.Description;
}
