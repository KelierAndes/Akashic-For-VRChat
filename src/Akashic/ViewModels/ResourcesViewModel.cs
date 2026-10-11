using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using Akashic.Core.Models;
using Akashic.Core.Services;

namespace Akashic.ViewModels;

/// <summary>
/// ViewModel for the 资源 page: VPM repositories and the local .unitypackage library, each with
/// its own checkbox-driven batch bar, following the ALCOM resource layout.
/// </summary>
public partial class ResourcesViewModel : ObservableObject
{
    public const int RepositoryTabIndex = 0;
    public const int UserPackageTabIndex = 1;
    public const int ExternalTabIndex = 2;
    public const int BoothTabIndex = 3;
    public const int AssetStoreTabIndex = 4;

    private readonly IVpmRegistryService _registryService;
    private readonly IUnityPackageLibraryService _libraryService;
    private readonly IExternalPackageService _externalService;
    private readonly IAssetStoreService _storeService;
    private readonly IUnityPackageIconService _iconService;

    private List<UnityPackageFileInfo> _allPackageFiles = [];
    private List<ExternalPackage> _allExternals = [];
    private List<string> _manualFiles = [];
    private readonly Dictionary<string, ImageSource?> _iconMemory = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    private int _selectedTab = RepositoryTabIndex;

    [ObservableProperty]
    private ObservableCollection<RepositoryRow> _repositories = [];

    [ObservableProperty]
    private ObservableCollection<UserPackageRow> _packages = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LibraryFoldersSummary))]
    private ObservableCollection<string> _libraryFolders = [];

    [ObservableProperty]
    private bool _isLoadingRepositories;

    [ObservableProperty]
    private bool _isLoadingPackages;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRepositoryStatus))]
    private string _repositoryStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPackageStatus))]
    private string _packageStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RepositorySummary))]
    private int _repositoryCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PackageSummary))]
    private int _packageCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRepositorySelectionActive))]
    [NotifyPropertyChangedFor(nameof(RepositorySelectionSummary))]
    private int _selectedRepositoryCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPackageSelectionActive))]
    [NotifyPropertyChangedFor(nameof(PackageSelectionSummary))]
    private int _selectedPackageCount;

    [ObservableProperty]
    private bool _isAllRepositoriesSelected;

    [ObservableProperty]
    private bool _isAllPackagesSelected;

    [ObservableProperty]
    private string _packageSearchQuery = string.Empty;

    /// <summary>Index into <see cref="OriginFilterOptions"/>; 0 keeps every source.</summary>
    [ObservableProperty]
    private int _packageOriginFilter;

    [ObservableProperty]
    private int _externalOriginFilter;

    [ObservableProperty]
    private ObservableCollection<ExternalPackageRow> _externalPackages = [];

    [ObservableProperty]
    private bool _isLoadingExternal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExternalStatus))]
    private string _externalStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExternalSummary))]
    private int _externalCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExternalSelectionActive))]
    [NotifyPropertyChangedFor(nameof(ExternalSelectionSummary))]
    private int _selectedExternalCount;

    [ObservableProperty]
    private bool _isAllExternalSelected;

    // ---- Booth 导入 tab ----

    [ObservableProperty]
    private ObservableCollection<UserPackageRow> _boothPackages = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BoothPackageSummary))]
    private int _boothPackageCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBoothPackageSelectionActive))]
    [NotifyPropertyChangedFor(nameof(BoothPackageSelectionSummary))]
    private int _selectedBoothPackageCount;

    [ObservableProperty]
    private bool _isAllBoothPackagesSelected;

    // ---- Asset Store 已获取 tab ----

    [ObservableProperty]
    private ObservableCollection<AssetStoreRow> _storeAcquired = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StoreAcquiredSummary))]
    private int _storeAcquiredCount;

    [ObservableProperty]
    private bool _isLoadingStore;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStoreStatus))]
    private string _storeStatus = string.Empty;

    public bool IsBoothPackageSelectionActive => SelectedBoothPackageCount > 0;
    public string BoothPackageSelectionSummary => $"已选择 {SelectedBoothPackageCount} 个软件包";
    public string BoothPackageSummary => $"Booth 导入的 .unitypackage · 共 {BoothPackageCount} 个";
    public string StoreAcquiredSummary => $"共 {StoreAcquiredCount} 个已获取的 Asset Store 资源 · 导入与下载在工具的 Asset Store 页进行";
    public bool HasStoreStatus => !string.IsNullOrEmpty(StoreStatus);

    public ResourcesViewModel(IVpmRegistryService registryService, IUnityPackageLibraryService libraryService,
        IExternalPackageService externalService, IAssetStoreService storeService,
        IUnityPackageIconService iconService)
    {
        _registryService = registryService;
        _libraryService = libraryService;
        _externalService = externalService;
        _storeService = storeService;
        _iconService = iconService;
    }

    public string RepositorySummary => $"共 {RepositoryCount} 个存储库 · 列表顺序即解析优先级";
    public string PackageSummary => $"共 {PackageCount} 个 .unitypackage" +
                                    OriginBreakdown(_allPackageFiles.Select(f => f.Origin));
    public string ExternalSummary => $"共 {ExternalCount} 个外部包 · 安装时写入项目的 Packages/manifest.json" +
                                     OriginBreakdown(_allExternals.Select(p => p.Origin));
    public bool HasRepositoryStatus => !string.IsNullOrEmpty(RepositoryStatus);
    public bool HasPackageStatus => !string.IsNullOrEmpty(PackageStatus);
    public bool IsRepositorySelectionActive => SelectedRepositoryCount > 0;
    public string RepositorySelectionSummary => $"已选择 {SelectedRepositoryCount} 个存储库";
    public bool IsPackageSelectionActive => SelectedPackageCount > 0;
    public string PackageSelectionSummary => $"已选择 {SelectedPackageCount} 个软件包";
    public bool HasExternalStatus => !string.IsNullOrEmpty(ExternalStatus);
    public bool IsExternalSelectionActive => SelectedExternalCount > 0;
    public string ExternalSelectionSummary => $"已选择 {SelectedExternalCount} 个外部包";
    public string LibraryFoldersSummary => LibraryFolders.Count == 0
        ? "尚未配置库目录 · 可单独选择文件加入包列表"
        : $"库目录 {LibraryFolders.Count} 个：{string.Join("   ·   ", LibraryFolders)}";

    /// <summary>Source filter labels for the 用户软件包 tab; index 0 keeps everything. Booth imports
    /// live in their own tab, so this list never offers (or contains) them.</summary>
    public IReadOnlyList<string> PackageFilterOptions { get; } = ["全部来源", "本地", "Asset Store"];

    /// <summary>Source filter labels for the 外部包 tab; index 0 keeps everything.</summary>
    public IReadOnlyList<string> OriginFilterOptions { get; } = ["全部来源", "本地", "Booth 导入", "Asset Store"];

    partial void OnPackageSearchQueryChanged(string value) => ApplyPackageFilter();
    partial void OnPackageOriginFilterChanged(int value) => ApplyPackageFilter();
    partial void OnExternalOriginFilterChanged(int value) => ApplyExternalFilter();

    private static PackageOrigin? OriginAt(int filterIndex) => filterIndex switch
    {
        1 => PackageOrigin.Local,
        2 => PackageOrigin.Booth,
        3 => PackageOrigin.AssetStore,
        _ => null
    };

    private static PackageOrigin? PackageOriginAt(int filterIndex) => filterIndex switch
    {
        1 => PackageOrigin.Local,
        2 => PackageOrigin.AssetStore,
        _ => null
    };

    private static string OriginBreakdown(IEnumerable<PackageOrigin> origins)
    {
        var booth = origins.Count(o => o == PackageOrigin.Booth);
        var store = origins.Count(o => o == PackageOrigin.AssetStore);

        var parts = new List<string>();
        if (booth > 0) parts.Add($"Booth 导入 {booth}");
        if (store > 0) parts.Add($"Asset Store {store}");

        return parts.Count == 0 ? string.Empty : " · " + string.Join(" · ", parts);
    }

    [RelayCommand]
    public async Task LoadAllAsync()
    {
        await LoadRepositoriesAsync();
        await LoadPackagesAsync();
        await LoadExternalAsync();
        await LoadStoreAcquiredAsync();
    }

    [RelayCommand]
    public async Task LoadRepositoriesAsync()
    {
        IsLoadingRepositories = true;
        try
        {
            DetachRepositoryHandlers();

            Repositories = new ObservableCollection<RepositoryRow>(
                _registryService.GetRegistryUrls().Select(url => new RepositoryRow(url)));

            foreach (var row in Repositories)
                row.PropertyChanged += OnRepositoryRowChanged;

            RepositoryCount = Repositories.Count;
            RefreshRepositorySelection();
            RepositoryStatus = string.Empty;
        }
        catch (Exception ex)
        {
            RepositoryStatus = $"读取存储库失败: {ex.Message}";
        }
        finally
        {
            IsLoadingRepositories = false;
        }

        // Name probing hits the network, so it runs after the local load spinner is done.
        await ResolveRepositoryNamesAsync();
    }

    /// <summary>Fetches each repository's advertised name so the 名称 column is not just the URL again.</summary>
    private async Task ResolveRepositoryNamesAsync()
    {
        var targets = Repositories.Where(r => r.Name == r.DerivedName).ToList();

        await Parallel.ForEachAsync(
            targets.Select((row, index) => (row, index)),
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = CancellationToken.None },
            async (pair, _) =>
            {
                var name = await _registryService.FetchRepositoryNameAsync(pair.row.Url);
                if (name != null) pair.row.Name = name;
            });
    }

    [RelayCommand]
    public async Task AddRepositoryAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            RepositoryStatus = "请输入仓库源 URL";
            return;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            RepositoryStatus = "无效的 URL 格式";
            return;
        }

        var clean = url.Trim();
        if (Repositories.Any(r => string.Equals(r.Url, clean, StringComparison.OrdinalIgnoreCase)))
        {
            RepositoryStatus = "该存储库已在列表中";
            return;
        }

        await _registryService.AddRegistryUrlAsync(clean);
        var row = new RepositoryRow(clean);
        row.PropertyChanged += OnRepositoryRowChanged;
        Repositories.Add(row);
        RepositoryCount = Repositories.Count;
        RepositoryStatus = $"已添加存储库 {row.Name}";

        var fetched = await _registryService.FetchRepositoryNameAsync(clean);
        if (fetched != null) row.Name = fetched;
    }

    /// <summary>
    /// Brings in the repositories VCC itself uses, so packages installed through VCC resolve to a
    /// repository that is listed here too.
    /// </summary>
    [RelayCommand]
    public async Task ImportFromVccAsync()
    {
        var known = _registryService.GetKnownRepositories().Count;
        var added = await _registryService.ImportVccRepositoriesAsync();

        await LoadRepositoriesAsync();
        RepositoryStatus = added == 0
            ? $"VCC 的 {known} 个存储库已全部在列表中"
            : $"已从 VCC 同步 {added} 个存储库（VCC 共 {known} 个）";
    }

    [RelayCommand]
    public async Task RemoveRepositoryAsync(RepositoryRow? row)
    {
        if (row == null) return;

        DetachRepositoryHandlers([row]);
        await _registryService.RemoveRegistryUrlAsync(row.Url);
        Repositories.Remove(row);
        RepositoryCount = Repositories.Count;
        RefreshRepositorySelection();
        RepositoryStatus = $"已移除存储库 {row.Name}";
    }

    [RelayCommand]
    public async Task RemoveSelectedRepositoriesAsync()
    {
        var selected = Repositories.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0) return;

        foreach (var row in selected)
        {
            DetachRepositoryHandlers([row]);
            await _registryService.RemoveRegistryUrlAsync(row.Url);
            Repositories.Remove(row);
        }

        RepositoryCount = Repositories.Count;
        RefreshRepositorySelection();
        RepositoryStatus = $"已移除 {selected.Count} 个存储库";
    }

    /// <summary>Moves a repository in the priority list and persists the new order.</summary>
    [RelayCommand]
    public async Task MoveRepositoryUpAsync(RepositoryRow? row) => await MoveRepositoryAsync(row, -1);

    [RelayCommand]
    public async Task MoveRepositoryDownAsync(RepositoryRow? row) => await MoveRepositoryAsync(row, 1);

    private async Task MoveRepositoryAsync(RepositoryRow? row, int delta)
    {
        if (row == null) return;

        var index = Repositories.IndexOf(row);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Repositories.Count) return;

        Repositories.Move(index, target);
        await _registryService.SaveRegistryOrderAsync(Repositories.Select(r => r.Url));
        RepositoryStatus = $"{row.Name} 现为第 {target + 1} 顺位";
    }

    [RelayCommand]
    public void ToggleSelectAllRepositories()
    {
        var selectAll = IsAllRepositoriesSelected;
        foreach (var row in Repositories)
            row.IsSelected = selectAll;

        RefreshRepositorySelection();
    }

    [RelayCommand]
    public void ClearRepositorySelection()
    {
        foreach (var row in Repositories)
            row.IsSelected = false;

        RefreshRepositorySelection();
    }

    private void DetachRepositoryHandlers(IEnumerable<RepositoryRow>? only = null)
    {
        foreach (var row in only ?? Repositories)
            row.PropertyChanged -= OnRepositoryRowChanged;
    }

    private void OnRepositoryRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RepositoryRow.IsSelected))
            RefreshRepositorySelection();
    }

    private void RefreshRepositorySelection()
    {
        SelectedRepositoryCount = Repositories.Count(r => r.IsSelected);
        IsAllRepositoriesSelected = Repositories.Count > 0 && Repositories.All(r => r.IsSelected);
    }

    [RelayCommand]
    public async Task LoadPackagesAsync()
    {
        IsLoadingPackages = true;
        try
        {
            _manualFiles = _libraryService.GetAddedFiles();
            _allPackageFiles = await _libraryService.ScanAsync();
            LibraryFolders = new ObservableCollection<string>(_libraryService.GetLibraryFolders());

            PackageCount = _allPackageFiles.Count;
            ApplyPackageFilter();
            ApplyBoothFilter();
            PackageStatus = string.Empty;
        }
        catch (Exception ex)
        {
            PackageStatus = $"读取包列表失败: {ex.Message}";
        }
        finally
        {
            IsLoadingPackages = false;
        }

        await ResolvePackageIconsAsync();
    }

    /// <summary>
    /// Fills the icon column in the background: Booth/Asset Store covers come from the cached
    /// row data, other .unitypackage files get a thumbnail pulled out of the archive itself and
    /// disk-cached, and anything without one keeps the default glyph.
    /// </summary>
    private async Task ResolvePackageIconsAsync()
    {
        var pending = Packages.Concat(BoothPackages)
            .Where(r => r.Icon is null && !r.IconFailed)
            .Distinct()
            .ToList();

        foreach (var row in pending)
        {
            var key = row.File.FilePath;
            if (_iconMemory.TryGetValue(key, out var cached))
            {
                row.SetIcon(cached);
                continue;
            }

            ImageSource? icon = null;
            try
            {
                var thumb = await _iconService.GetThumbnailAsync(key);
                if (thumb is not null && File.Exists(thumb))
                {
                    icon = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(thumb));
                }
            }
            catch
            {
                // An unreadable archive just keeps the default glyph.
            }

            _iconMemory[key] = icon;
            row.SetIcon(icon);
        }
    }

    [RelayCommand]
    public async Task AddLibraryFolderAsync(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            PackageStatus = "目录不存在";
            return;
        }

        await _libraryService.AddLibraryFolderAsync(folder);
        await LoadPackagesAsync();
        PackageStatus = $"已添加库目录 {folder}";
    }

    [RelayCommand]
    public async Task RemoveLibraryFolderAsync(string folder)
    {
        await _libraryService.RemoveLibraryFolderAsync(folder);
        await LoadPackagesAsync();
        PackageStatus = $"已移除库目录 {folder}";
    }

    [RelayCommand]
    public async Task RemovePackageAsync(UserPackageRow? row)
    {
        if (row == null) return;

        if (!row.IsManual)
        {
            PackageStatus = $"{row.Name} 来自库目录，需在下方“库目录”中移除该目录才会从包列表消失";
            return;
        }

        await _libraryService.RemovePackageFileAsync(row.File.FilePath);
        await LoadPackagesAsync();
        PackageStatus = $"已从包列表移除 {row.Name}";
    }

    [RelayCommand]
    public async Task RemoveSelectedPackagesAsync()
    {
        var selected = Packages.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0) return;

        var removed = 0;
        foreach (var row in selected.Where(r => r.IsManual))
        {
            await _libraryService.RemovePackageFileAsync(row.File.FilePath);
            removed++;
        }

        await LoadPackagesAsync();
        PackageStatus = removed == 0
            ? $"勾选的 {selected.Count} 个软件包都来自库目录，未做任何移除"
            : $"已从包列表移除 {removed} 个手动添加的软件包";
    }

    [RelayCommand]
    public void ToggleSelectAllPackages()
    {
        var selectAll = IsAllPackagesSelected;
        foreach (var row in Packages)
            row.IsSelected = selectAll;

        RefreshPackageSelection();
    }

    [RelayCommand]
    public void ClearPackageSelection()
    {
        foreach (var row in Packages)
            row.IsSelected = false;

        RefreshPackageSelection();
    }

    private void ApplyPackageFilter()
    {
        foreach (var row in Packages)
            row.PropertyChanged -= OnPackageRowChanged;

        // Booth imports have their own tab, so the master list never repeats them here.
        IEnumerable<UnityPackageFileInfo> query = _allPackageFiles.Where(f => f.Origin != PackageOrigin.Booth);

        var origin = PackageOriginAt(PackageOriginFilter);
        if (origin is not null)
            query = query.Where(f => f.Origin == origin);

        if (!string.IsNullOrWhiteSpace(PackageSearchQuery))
        {
            query = query.Where(f =>
                f.DisplayName.Contains(PackageSearchQuery, StringComparison.OrdinalIgnoreCase) ||
                f.FileName.Contains(PackageSearchQuery, StringComparison.OrdinalIgnoreCase) ||
                f.FilePath.Contains(PackageSearchQuery, StringComparison.OrdinalIgnoreCase));
        }

        Packages = new ObservableCollection<UserPackageRow>(
            query.Select(f => new UserPackageRow(f, IsManual(f.FilePath))));

        foreach (var row in Packages)
            row.PropertyChanged += OnPackageRowChanged;

        RefreshPackageSelection();
    }

    private bool IsManual(string filePath) =>
        _manualFiles.Any(m => string.Equals(m, filePath, StringComparison.OrdinalIgnoreCase));

    private void OnPackageRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UserPackageRow.IsSelected))
            RefreshPackageSelection();
    }

    private void RefreshPackageSelection()
    {
        SelectedPackageCount = Packages.Count(p => p.IsSelected);
        IsAllPackagesSelected = Packages.Count > 0 && Packages.All(p => p.IsSelected);
    }

    // ---- Booth 导入 tab ----

    /// <summary>The Booth tab always lists exactly the imports tagged Booth, ignoring the master tab's filter.</summary>
    private void ApplyBoothFilter()
    {
        foreach (var row in BoothPackages)
            row.PropertyChanged -= OnPackageRowChanged;

        BoothPackages = new ObservableCollection<UserPackageRow>(
            _allPackageFiles
                .Where(f => f.Origin == PackageOrigin.Booth)
                .Select(f => new UserPackageRow(f, IsManual(f.FilePath))));

        foreach (var row in BoothPackages)
            row.PropertyChanged += OnPackageRowChanged;

        BoothPackageCount = BoothPackages.Count;
        RefreshBoothPackageSelection();
    }

    [RelayCommand]
    public void ToggleSelectAllBoothPackages()
    {
        var selectAll = IsAllBoothPackagesSelected;
        foreach (var row in BoothPackages)
            row.IsSelected = selectAll;

        RefreshBoothPackageSelection();
    }

    [RelayCommand]
    public void ClearBoothPackageSelection()
    {
        foreach (var row in BoothPackages)
            row.IsSelected = false;

        RefreshBoothPackageSelection();
    }

    private void RefreshBoothPackageSelection()
    {
        SelectedBoothPackageCount = BoothPackages.Count(p => p.IsSelected);
        IsAllBoothPackagesSelected = BoothPackages.Count > 0 && BoothPackages.All(p => p.IsSelected);
    }

    [RelayCommand]
    public async Task RemoveBoothPackageAsync(UserPackageRow? row)
    {
        if (row == null) return;

        if (!row.IsManual)
        {
            PackageStatus = $"{row.Name} 来自库目录，需在“用户软件包”分栏的“库目录”中移除该目录";
            return;
        }

        await _libraryService.RemovePackageFileAsync(row.File.FilePath);
        await LoadPackagesAsync();
        PackageStatus = $"已从包列表移除 {row.Name}";
    }

    [RelayCommand]
    public async Task RemoveSelectedBoothPackagesAsync()
    {
        var selected = BoothPackages.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0) return;

        var removed = 0;
        foreach (var row in selected.Where(r => r.IsManual))
        {
            await _libraryService.RemovePackageFileAsync(row.File.FilePath);
            removed++;
        }

        await LoadPackagesAsync();
        PackageStatus = removed == 0
            ? $"勾选的 {selected.Count} 个软件包都来自库目录，未做任何移除"
            : $"已从包列表移除 {removed} 个手动添加的软件包";
    }

    // ---- Asset Store 已获取 tab ----

    /// <summary>
    /// Lists the Asset Store resources this machine already holds — files in Unity's cache or the
    /// app's download folder. Downloading new ones happens on the 工具 Asset Store page.
    /// </summary>
    [RelayCommand]
    public async Task LoadStoreAcquiredAsync()
    {
        if (IsLoadingStore) return;

        IsLoadingStore = true;
        try
        {
            var cached = await _storeService.ScanCacheAsync();

            StoreAcquired = new ObservableCollection<AssetStoreRow>(
                cached.Where(a => a.IsCached)
                      .Select(a => new AssetStoreRow(a)));

            // Covers remembered by the Asset Store page (account reads and search lookups) are
            // handed out here too, so cache-only rows show the product image when one is known.
            foreach (var row in StoreAcquired.Where(r => !r.HasCover))
            {
                if (_storeService.TryGetCoverUrl(row.Name, out var url) &&
                    Uri.TryCreate(url, UriKind.Absolute, out var cover))
                {
                    try { row.CoverImage = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(cover); }
                    catch { /* keep the glyph */ }
                }
            }

            StoreAcquiredCount = StoreAcquired.Count;
            StoreStatus = string.Empty;
        }
        catch (Exception ex)
        {
            StoreStatus = $"读取 Asset Store 资源失败: {ex.Message}";
        }
        finally
        {
            IsLoadingStore = false;
        }
    }

    /// <summary>Registers a cached .unitypackage in 用户软件包 with the Asset Store tag.</summary>
    [RelayCommand]
    public async Task AddStoreAcquiredToLibraryAsync(AssetStoreRow? row)
    {
        if (row?.LocalPath is null) return;

        try
        {
            await _libraryService.AddPackageFileAsync(row.Asset.LocalPath!, PackageOrigin.AssetStore);
            row.IsInLibrary = true;
            row.RowStatus = "已加入用户软件包";
            StoreStatus = $"已把 {row.Name} 加入用户软件包";
            await LoadPackagesAsync();
        }
        catch (Exception ex)
        {
            StoreStatus = $"入库失败: {ex.Message}";
        }
    }

    [RelayCommand]
    public Task LoadExternalAsync()
    {
        IsLoadingExternal = true;
        try
        {
            _allExternals = _externalService.GetAll()
                .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.AddedAt)
                .ToList();

            ExternalCount = _allExternals.Count;
            ApplyExternalFilter();
            ExternalStatus = string.Empty;
        }
        catch (Exception ex)
        {
            ExternalStatus = $"读取外部包失败: {ex.Message}";
        }
        finally
        {
            IsLoadingExternal = false;
        }

        return Task.CompletedTask;
    }

    private void ApplyExternalFilter()
    {
        DetachExternalHandlers();

        IEnumerable<ExternalPackage> query = _allExternals;

        var origin = OriginAt(ExternalOriginFilter);
        if (origin is not null)
            query = query.Where(p => p.Origin == origin);

        ExternalPackages = new ObservableCollection<ExternalPackageRow>(
            query.Select(p => new ExternalPackageRow(p)));

        foreach (var row in ExternalPackages)
            row.PropertyChanged += OnExternalRowChanged;

        RefreshExternalSelection();
    }

    [RelayCommand]
    public async Task AddLocalPackageAsync(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            ExternalStatus = "请选择包含 package.json 的包目录";
            return;
        }

        try
        {
            var package = await _externalService.AddLocalFolderAsync(folderPath);
            await LoadExternalAsync();
            ExternalStatus = $"已加入本地包 {package.DisplayName}（{package.Name}）";
        }
        catch (Exception ex)
        {
            ExternalStatus = $"添加失败: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task AddGitPackageAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            ExternalStatus = "请输入仓库地址（GitHub / GitLab / Gitee 均可，平台按地址自动识别）";
            return;
        }

        try
        {
            var package = await _externalService.AddRepositoryAsync(url, null);
            await LoadExternalAsync();
            ExternalStatus = string.IsNullOrEmpty(package.Name)
                ? $"已加入 {package.Kind} 仓库 {package.DisplayName}（{package.Location}）· 包名将在首次安装时从 package.json 读取"
                : $"已加入 {package.Kind} 包 {package.DisplayName}（{package.Name} · {package.Reference}）";
        }
        catch (Exception ex)
        {
            ExternalStatus = $"添加失败: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task RemoveExternalAsync(ExternalPackageRow? row)
    {
        if (row == null) return;

        DetachExternalHandlers([row]);
        await _externalService.RemoveAsync(row.Package.Id);
        await LoadExternalAsync();
        ExternalStatus = $"已移除外部包 {row.Name}（项目内的引用需自行卸载）";
    }

    [RelayCommand]
    public async Task RemoveSelectedExternalsAsync()
    {
        var selected = ExternalPackages.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0) return;

        foreach (var row in selected)
        {
            DetachExternalHandlers([row]);
            await _externalService.RemoveAsync(row.Package.Id);
        }

        await LoadExternalAsync();
        ExternalStatus = $"已移除 {selected.Count} 个外部包（项目内的引用保持不变）";
    }

    [RelayCommand]
    public void ToggleSelectAllExternals()
    {
        var selectAll = IsAllExternalSelected;
        foreach (var row in ExternalPackages)
            row.IsSelected = selectAll;

        RefreshExternalSelection();
    }

    [RelayCommand]
    public void ClearExternalSelection()
    {
        foreach (var row in ExternalPackages)
            row.IsSelected = false;

        RefreshExternalSelection();
    }

    private void DetachExternalHandlers(IEnumerable<ExternalPackageRow>? only = null)
    {
        foreach (var row in only ?? ExternalPackages)
            row.PropertyChanged -= OnExternalRowChanged;
    }

    private void OnExternalRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExternalPackageRow.IsSelected))
            RefreshExternalSelection();
    }

    private void RefreshExternalSelection()
    {
        SelectedExternalCount = ExternalPackages.Count(r => r.IsSelected);
        IsAllExternalSelected = ExternalPackages.Count > 0 && ExternalPackages.All(r => r.IsSelected);
    }

    /// <summary>Switches the page tabs from another page, e.g. the settings shortcut cards.</summary>
    public void OpenTab(int tabIndex)
    {
        SelectedTab = tabIndex;
    }
}

/// <summary>One VPM repository row: the URL is the identity, the name is resolved from the listing.</summary>
public partial class RepositoryRow : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OriginDisplay))]
    private string _name;

    public RepositoryRow(string url)
    {
        Url = url;
        _name = DerivedNameFor(url);
    }

    public string Url { get; }

    public string DerivedName => DerivedNameFor(Url);

    public string OriginDisplay => Name == DerivedName ? "按 URL 推导" : "仓库自报名称";

    private static string DerivedNameFor(string url)
    {
        if (string.Equals(url, "https://packages.vrchat.com/official", StringComparison.OrdinalIgnoreCase))
            return "官方";
        if (string.Equals(url, "https://packages.vrchat.com/curated", StringComparison.OrdinalIgnoreCase))
            return "精选";

        try
        {
            var uri = new Uri(url);
            var segments = uri.Segments;

            for (var i = segments.Length - 1; i >= 0; i--)
            {
                var candidate = Uri.UnescapeDataString(segments[i].Trim('/'));
                if (string.IsNullOrWhiteSpace(candidate) ||
                    candidate.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    continue;

                return candidate;
            }

            return uri.Host;
        }
        catch (UriFormatException)
        {
            return url;
        }
    }
}

/// <summary>One .unitypackage in the library, tagged with how it got there.</summary>
public partial class UserPackageRow : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon))]
    private Microsoft.UI.Xaml.Media.ImageSource? _icon;

    /// <summary>Set once the archive has been probed for a thumbnail and none was found.</summary>
    [ObservableProperty]
    private bool _iconFailed;

    public UserPackageRow(UnityPackageFileInfo file, bool isManual)
    {
        File = file;
        IsManual = isManual;
        SourceBrush = OriginBrush.For(file.Origin);
    }

    public UnityPackageFileInfo File { get; }
    public bool IsManual { get; }
    public SolidColorBrush SourceBrush { get; }

    public bool HasIcon => Icon is not null;

    /// <summary>Called from the icon resolver; a null result means the default glyph stays.</summary>
    public void SetIcon(ImageSource? icon)
    {
        if (icon is null) IconFailed = true;
        Icon = icon;
    }

    public string Name => string.IsNullOrWhiteSpace(File.DisplayName) ? File.PackageId : File.DisplayName;
    public string VersionDisplay => string.IsNullOrWhiteSpace(File.Version) ? "未标注版本" : File.Version;
    public string Meta => $"{File.FileName} · {File.SizeDisplay} · {File.LastModifiedDisplay}";

    /// <summary>Who supplied the file, which is what the 来源 column and the filter are about.</summary>
    public string SourceDisplay => File.OriginDisplay;

    /// <summary>How the file reached the library: picked by hand, or covered by a scanned folder.</summary>
    public string AddedViaDisplay => IsManual ? "手动添加" : "库目录";
}

/// <summary>One package attached outside of VPM.</summary>
public partial class ExternalPackageRow : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public ExternalPackageRow(ExternalPackage package)
    {
        Package = package;
        SourceBrush = OriginBrush.For(package.Origin);
    }

    public ExternalPackage Package { get; }
    public SolidColorBrush SourceBrush { get; }

    public bool IsGit => Package.Kind != ExternalPackageKind.LocalFolder;
    public string KindDisplay => Package.Kind switch
    {
        ExternalPackageKind.GitHub => "GitHub 仓库",
        ExternalPackageKind.GitLab => "GitLab 仓库",
        ExternalPackageKind.Gitee => "Gitee 仓库",
        _ => "本地包目录"
    };
    public string SourceDisplay => Package.OriginDisplay;
    public string Name => !string.IsNullOrWhiteSpace(Package.DisplayName) ? Package.DisplayName
                    : !string.IsNullOrWhiteSpace(Package.Name) ? Package.Name
                    : Package.Location;
    public string PackageIdDisplay => string.IsNullOrWhiteSpace(Package.Name) ? "包名待首次安装解析" : Package.Name;
    public string LocationDisplay => Package.Location;
    public string VersionDisplay => IsGit ? $"{Package.Version} · {Package.Reference}"
                                    : string.IsNullOrWhiteSpace(Package.Version) ? "未标注版本" : Package.Version;
    public string Meta => $"{KindDisplay} · 添加于 {Package.AddedAt:yyyy/MM/dd HH:mm}";
}

/// <summary>
/// One colour per source so a Booth or Asset Store import is recognisable in a table column.
/// The values sit between the dark and light text palettes rather than following the theme.
/// </summary>
internal static class OriginBrush
{
    public static SolidColorBrush For(PackageOrigin origin) => origin switch
    {
        PackageOrigin.Booth => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xD9, 0x8A, 0x00)),
        PackageOrigin.AssetStore => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x0E, 0x8C, 0x7F)),
        _ => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x8A, 0x8A, 0x8A))
    };
}
