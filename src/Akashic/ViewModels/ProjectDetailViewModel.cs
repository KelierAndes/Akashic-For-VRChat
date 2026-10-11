using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Akashic.Core.Models;
using Akashic.Core.Services;

namespace Akashic.ViewModels;

/// <summary>
/// ViewModel for the ALCOM-style flat package management view.
/// Displays a unified table of VPM packages and local .unitypackage assets.
/// </summary>
public partial class ProjectDetailViewModel : ObservableObject
{
    private readonly IPackageInstaller _installer;
    private readonly IVpmRegistryService _registryService;
    private readonly IProjectService _projectService;
    private readonly IUnityPackageExtractor _extractor;
    private readonly IUnityPackageLibraryService _libraryService;
    private readonly IExternalPackageService _externalService;

    private UnityPackageArchive? _pendingArchive;
    private UnifiedPackageItem? _pendingItem;

    [ObservableProperty]
    private UnityProject? _project;

    [ObservableProperty]
    private ObservableCollection<UnifiedPackageItem> _allPackages = [];

    [ObservableProperty]
    private ObservableCollection<UnifiedPackageItem> _filteredPackages = [];

    [ObservableProperty]
    private ObservableCollection<UnityPackageEntry> _importEntries = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private double _installProgress;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private int _upgradeCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectionActive))]
    [NotifyPropertyChangedFor(nameof(SelectionSummary))]
    [NotifyPropertyChangedFor(nameof(ClearSelectionLabel))]
    private int _selectedCount;

    [ObservableProperty]
    private bool _isAllSelected;

    [ObservableProperty]
    private int _activeFilterCount;

    // Filter conditions: within a group the conditions are OR-ed, groups are AND-ed.
    [ObservableProperty]
    private bool _filterInstalled;

    [ObservableProperty]
    private bool _filterNotInstalled;

    [ObservableProperty]
    private bool _filterUpgradable;

    [ObservableProperty]
    private bool _filterVpm;

    [ObservableProperty]
    private bool _filterUnityPackage;

    [ObservableProperty]
    private bool _filterExternal;

    [ObservableProperty]
    private bool _filterBooth;

    [ObservableProperty]
    private bool _filterAssetStore;

    public bool IsSelectionActive => SelectedCount > 0;
    public string SelectionSummary => $"已选择 {SelectedCount} 个软件包";
    public string ClearSelectionLabel => $"清空选择 ({SelectionSummary})";

    /// <summary>Clears every ticked filter condition; called from the filter flyout.</summary>
    public void ResetFilters()
    {
        FilterInstalled = false;
        FilterNotInstalled = false;
        FilterUpgradable = false;
        FilterVpm = false;
        FilterUnityPackage = false;
        FilterExternal = false;
        FilterBooth = false;
        FilterAssetStore = false;
    }

    public ProjectDetailViewModel(IPackageInstaller installer, IVpmRegistryService registryService,
        IProjectService projectService, IUnityPackageExtractor extractor, IUnityPackageLibraryService libraryService,
        IExternalPackageService externalService)
    {
        _installer = installer;
        _registryService = registryService;
        _projectService = projectService;
        _extractor = extractor;
        _libraryService = libraryService;
        _externalService = externalService;
    }

    public void SetProject(UnityProject project)
    {
        Project = project;
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();
    partial void OnFilterInstalledChanged(bool value) => OnFilterConditionChanged();
    partial void OnFilterNotInstalledChanged(bool value) => OnFilterConditionChanged();
    partial void OnFilterUpgradableChanged(bool value) => OnFilterConditionChanged();
    partial void OnFilterVpmChanged(bool value) => OnFilterConditionChanged();
    partial void OnFilterUnityPackageChanged(bool value) => OnFilterConditionChanged();
    partial void OnFilterExternalChanged(bool value) => OnFilterConditionChanged();
    partial void OnFilterBoothChanged(bool value) => OnFilterConditionChanged();
    partial void OnFilterAssetStoreChanged(bool value) => OnFilterConditionChanged();

    private void OnFilterConditionChanged()
    {
        ApplyFilter();
        RefreshSelectionState();
    }

    [RelayCommand]
    public async Task LoadPackagesAsync()
    {
        if (Project == null) return;

        IsLoading = true;
        StatusMessage = "正在同步软件包列表...";

        try
        {
            // 1. Refresh project to get latest installed packages
            Project = await _projectService.RefreshProjectAsync(Project);

            // 2. Fetch all registry VPM packages
            var vpmRegistryPackages = await _registryService.GetAllPackagesAsync();

            // 3. Build unified flat list
            var list = new List<UnifiedPackageItem>();
            var installedMap = Project.InstalledPackages.ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);

            // A. Process VPM registry packages
            foreach (var vpmPkg in vpmRegistryPackages)
            {
                var isInstalled = installedMap.TryGetValue(vpmPkg.Name, out var installed);
                var installedVer = isInstalled ? installed!.Version : "无";
                var canUpgrade = isInstalled && CompareVersions(vpmPkg.Version, installedVer) > 0;

                var versions = vpmPkg.Versions.Keys.OrderByDescending(v => ParseVersion(v)).ToList();
                if (isInstalled && !versions.Contains(installedVer))
                    versions.Insert(0, installedVer);
                if (!versions.Contains(vpmPkg.Version))
                    versions.Insert(0, vpmPkg.Version);

                var item = new UnifiedPackageItem
                {
                    Id = vpmPkg.Name,
                    DisplayName = string.IsNullOrWhiteSpace(vpmPkg.DisplayName) ? vpmPkg.Name : vpmPkg.DisplayName,
                    Description = vpmPkg.Description ?? string.Empty,
                    Category = PackageCategory.Vpm,
                    SourceName = vpmPkg.SourceRegistry,
                    IsInstalled = isInstalled,
                    InstalledVersion = installedVer,
                    SelectedVersion = isInstalled ? installedVer : vpmPkg.Version,
                    LatestVersion = vpmPkg.Version,
                    CanUpgrade = canUpgrade,
                    AvailableVersions = versions,
                    VpmPackageRef = vpmPkg,
                    InstalledPackageRef = installed
                };

                list.Add(item);

                if (isInstalled)
                    installedMap.Remove(vpmPkg.Name);
            }

            // B. Packages attached outside VPM: local UPM folders and GitHub repositories
            var externalCount = 0;
            foreach (var external in _externalService.GetAll())
            {
                // The name of a GitHub package is only known once its package.json has been read.
                var isInstalled = installedMap.TryGetValue(external.Name, out var installedExternal) &&
                                  !string.IsNullOrEmpty(external.Name);
                if (isInstalled) installedMap.Remove(external.Name);
                externalCount++;

                var externalVersion = installedExternal?.Version ?? "无";

                list.Add(new UnifiedPackageItem
                {
                    Id = string.IsNullOrEmpty(external.Name) ? external.Location : external.Name,
                    DisplayName = string.IsNullOrWhiteSpace(external.DisplayName) ? external.Name : external.DisplayName,
                    Description = external.Kind != ExternalPackageKind.LocalFolder
                        ? $"{external.Kind} 仓库 · {external.Location} · {external.Reference}"
                        : $"本地包目录 · {external.Location}",
                    Category = PackageCategory.External,
                    Origin = external.Origin,
                    SourceName = external.Origin == PackageOrigin.Local
                        ? external.Kind != ExternalPackageKind.LocalFolder ? external.Kind.ToString() : "本地包"
                        : external.OriginDisplay,
                    IsInstalled = isInstalled,
                    InstalledVersion = externalVersion,
                    LatestVersion = string.IsNullOrWhiteSpace(external.Version) ? "默认分支" : external.Version,
                    SelectedVersion = isInstalled ? externalVersion : external.Version,
                    CanUpgrade = false,
                    ExternalRef = external,
                    InstalledPackageRef = installedExternal
                });
            }

            // C. Installed packages no configured repository provides: VCC installs, local folders, git links
            var foreignCount = 0;
            foreach (var installed in installedMap.Values)
            {
                // A package VCC installed from a repository this app does not list still has an owner.
                var owner = _registryService.FindRepositoryFor(installed.Name);
                if (owner != null) foreignCount++;

                list.Add(new UnifiedPackageItem
                {
                    Id = installed.Name,
                    DisplayName = installed.DisplayName,
                    Description = owner != null
                        ? $"该包由存储库 {owner.Name} 提供，但未加入本应用列表 · {owner.Url}"
                        : "用户本地包",
                    Category = PackageCategory.Vpm,
                    ForeignRepository = owner != null,
                    SourceName = owner?.Name ?? (installed.Source == PackageSource.Local ? "Local" : "User"),
                    IsInstalled = true,
                    InstalledVersion = installed.Version,
                    LatestVersion = installed.Version,
                    CanUpgrade = false,
                    InstalledPackageRef = installed
                });
            }

            // D. Local .unitypackage library, matched against this project's import ledger
            var libraryFiles = await _libraryService.ScanAsync();
            var records = await _libraryService.GetRecordsAsync(Project.Path);
            var recordMap = new Dictionary<string, UnityPackageInstallRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (var record in records)
                recordMap[record.PackageId] = record;

            foreach (var file in libraryFiles)
            {
                var isInstalled = recordMap.TryGetValue(file.PackageId, out var record);
                var installedVer = isInstalled ? record!.Version : "无";
                var canUpgrade = isInstalled && !string.IsNullOrEmpty(file.Version)
                                 && !string.IsNullOrEmpty(record!.Version)
                                 && CompareVersions(file.Version, record.Version) > 0;

                list.Add(new UnifiedPackageItem
                {
                    Id = file.PackageId,
                    DisplayName = string.IsNullOrWhiteSpace(file.DisplayName) ? file.PackageId : file.DisplayName,
                    Description = isInstalled
                        ? $"{file.OriginDisplay} .unitypackage · 已导入 {record!.FileCount} 个文件 · {record.InstalledAt:yyyy/MM/dd}"
                        : $"{file.OriginDisplay} .unitypackage · {file.SizeDisplay} · {file.LastModifiedDisplay}",
                    Category = PackageCategory.UnityPackage,
                    Origin = file.Origin,
                    SourceName = file.Origin == PackageOrigin.Local ? "UnityPackage" : file.OriginDisplay,
                    IsInstalled = isInstalled,
                    InstalledVersion = installedVer,
                    LatestVersion = string.IsNullOrEmpty(file.Version) ? "本地资产" : file.Version,
                    SelectedVersion = installedVer,
                    CanUpgrade = canUpgrade,
                    AvailableVersions = string.IsNullOrEmpty(file.Version) ? [] : [file.Version],
                    UnityPackageRef = file,
                    InstallRecordRef = record
                });
            }

            // Sort: Installed first, then alphabetically
            var sorted = list.OrderByDescending(p => p.IsInstalled)
                             .ThenByDescending(p => p.CanUpgrade)
                             .ThenBy(p => p.DisplayName)
                             .ToList();

            DetachSelectionHandlers();
            AllPackages = new ObservableCollection<UnifiedPackageItem>(sorted);
            foreach (var item in AllPackages)
                item.PropertyChanged += OnPackageItemPropertyChanged;

            UpgradeCount = AllPackages.Count(p => p.CanUpgrade);
            ApplyFilter();
            RefreshSelectionState();

            StatusMessage = $"已加载 {AllPackages.Count} 个软件包 ({UpgradeCount} 个可升级)";

            if (foreignCount > 0)
                StatusMessage += $" · {foreignCount} 个包来自未加入列表的存储库，可在 资源 > 存储库 从 VCC 同步";

            if (externalCount > 0)
                StatusMessage += $" · {externalCount} 个外部包可在 资源 > 外部包 管理";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void DetachSelectionHandlers()
    {
        foreach (var item in AllPackages)
            item.PropertyChanged -= OnPackageItemPropertyChanged;
    }

    private void OnPackageItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UnifiedPackageItem.IsSelected))
            RefreshSelectionState();
    }

    private void RefreshSelectionState()
    {
        SelectedCount = AllPackages.Count(p => p.IsSelected);
        IsAllSelected = FilteredPackages.Count > 0 && FilteredPackages.All(p => p.IsSelected);
    }

    [RelayCommand]
    public async Task InstallPackageAsync(UnifiedPackageItem item)
    {
        if (Project == null) return;

        if (item.Category == PackageCategory.External)
        {
            await InstallExternalPackageAsync(item);
            return;
        }

        if (item.VpmPackageRef == null) return;
        IsLoading = true;
        InstallProgress = 0;
        StatusMessage = $"正在安装 {item.DisplayName}...";

        try
        {
            var progress = new Progress<double>(p => InstallProgress = p * 100);
            await _installer.InstallVpmPackageAsync(Project, item.VpmPackageRef, progress);

            await LoadPackagesAsync();
            StatusMessage = $"已安装 {item.DisplayName} v{item.VpmPackageRef.Version}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"安装失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            InstallProgress = 0;
        }
    }

    /// <summary>Attaches an external package to the project: a file: reference or an embedded GitHub copy.</summary>
    private async Task InstallExternalPackageAsync(UnifiedPackageItem item)
    {
        var external = item.ExternalRef;
        if (Project == null || external == null) return;

        IsLoading = true;
        InstallProgress = 0;
        StatusMessage = $"正在安装 {item.DisplayName}...";

        try
        {
            var progress = new Progress<double>(p => InstallProgress = p * 100);
            await _externalService.InstallAsync(Project, external, progress);

            // LoadPackagesAsync overwrites StatusMessage with its own summary, so report after the reload.
            var result = external.Kind != ExternalPackageKind.LocalFolder
                ? $"已安装 {item.DisplayName}（{external.Kind} {external.Reference}）"
                : $"已链接本地包 {item.DisplayName}";
            await LoadPackagesAsync();
            StatusMessage = result;
        }
        catch (Exception ex)
        {
            StatusMessage = $"安装失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            InstallProgress = 0;
        }
    }

    [RelayCommand]
    public async Task UpgradePackageAsync(UnifiedPackageItem item)
    {
        if (Project == null || item.VpmPackageRef == null) return;
        await InstallPackageAsync(item);
    }

    /// <summary>What the batch button would do for the current selection, used by its confirmation dialog.</summary>
    public (int VpmCount, int UnityPackageCount, int ExternalCount, int SkippedCount) DescribeSelection()
    {
        var selected = AllPackages.Where(p => p.IsSelected).ToList();
        var vpm = selected.Count(p => p.Category == PackageCategory.Vpm && p.VpmPackageRef != null && (!p.IsInstalled || p.CanUpgrade));
        var up = selected.Count(p => p.Category == PackageCategory.UnityPackage && p.UnityPackageRef != null && !p.IsInstalled);
        var external = selected.Count(p => p.Category == PackageCategory.External && p.ExternalRef != null && !p.IsInstalled);
        return (vpm, up, external, selected.Count - vpm - up - external);
    }

    /// <summary>
    /// Batch action for ticked rows: installs/upgrades VPM packages and imports every asset of the
    /// ticked .unitypackage files. The result is reported through StatusMessage.
    /// </summary>
    [RelayCommand]
    public async Task InstallSelectedPackagesAsync()
    {
        if (Project == null)
        {
            StatusMessage = "未选择项目";
            return;
        }

        var selected = AllPackages.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "未勾选任何软件包";
            return;
        }

        IsLoading = true;
        InstallProgress = 0;

        var installed = 0;
        var imported = 0;
        var attached = 0;
        var failed = 0;
        var done = 0;

        try
        {
            foreach (var item in selected)
            {
                done++;
                InstallProgress = (double)done / selected.Count * 100;

                try
                {
                    if (item.Category == PackageCategory.Vpm && item.VpmPackageRef != null)
                    {
                        if (item.IsInstalled && !item.CanUpgrade) continue;

                        var pkg = item.VpmPackageRef;
                        var target = !string.IsNullOrEmpty(item.SelectedVersion) &&
                                     item.SelectedVersion != "无" &&
                                     pkg.Versions.TryGetValue(item.SelectedVersion, out var exact)
                            ? exact
                            : pkg;

                        StatusMessage = $"正在安装 ({done}/{selected.Count}) {item.DisplayName}...";
                        await _installer.InstallVpmPackageAsync(Project, target);
                        installed++;
                    }
                    else if (item.Category == PackageCategory.UnityPackage && item.UnityPackageRef != null)
                    {
                        if (item.IsInstalled) continue;

                        StatusMessage = $"正在导入 ({done}/{selected.Count}) {item.DisplayName}...";
                        await ImportUnityPackageSilentlyAsync(item);
                        imported++;
                    }
                    else if (item.Category == PackageCategory.External && item.ExternalRef != null)
                    {
                        if (item.IsInstalled) continue;

                        StatusMessage = $"正在附加 ({done}/{selected.Count}) {item.DisplayName}...";
                        await _externalService.InstallAsync(Project, item.ExternalRef);
                        attached++;
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    System.Diagnostics.Debug.WriteLine($"Batch install {item.Id} failed: {ex.Message}");
                }
            }

            await LoadPackagesAsync();
        }
        finally
        {
            IsLoading = false;
            InstallProgress = 0;
        }

        var parts = new List<string>();
        if (installed > 0) parts.Add($"安装/升级 {installed} 个 VPM 包");
        if (imported > 0) parts.Add($"导入 {imported} 个 .unitypackage");
        if (attached > 0) parts.Add($"附加 {attached} 个外部包");
        if (failed > 0) parts.Add($"失败 {failed} 个");
        if (parts.Count == 0) parts.Add("勾选项均已安装，无需操作");

        StatusMessage = string.Join(" · ", parts);
    }

    /// <summary>Batch path: writes every asset of the .unitypackage without the per-asset dialog.</summary>
    private async Task ImportUnityPackageSilentlyAsync(UnifiedPackageItem item)
    {
        var file = item.UnityPackageRef!;
        var archive = await _extractor.ParseAsync(file.FilePath);
        foreach (var entry in archive.Entries) entry.IsSelected = true;

        var installedPaths = await _installer.InstallUnityPackageAsync(Project!, archive);

        await _libraryService.SaveRecordAsync(Project!.Path, new UnityPackageInstallRecord
        {
            PackageId = file.PackageId,
            FilePath = file.FilePath,
            DisplayName = file.DisplayName,
            Version = file.Version,
            InstalledAt = DateTime.Now,
            FileCount = archive.Entries.Count(e => e.IsSelected),
            InstalledPaths = installedPaths.ToList()
        });
    }

    [RelayCommand]
    public void ClearSelection()
    {
        foreach (var item in AllPackages) item.IsSelected = false;
        RefreshSelectionState();
    }

    /// <summary>
    /// Parses a library .unitypackage and exposes its entries for the asset-selection dialog.
    /// Returns false when parsing failed; the dialog is only shown when this returns true.
    /// </summary>
    public async Task<bool> PrepareUnityPackageImportAsync(UnifiedPackageItem item)
    {
        if (Project == null || item.UnityPackageRef == null) return false;

        IsLoading = true;
        StatusMessage = $"正在解析 {item.UnityPackageRef.FileName}...";

        try
        {
            var archive = await _extractor.ParseAsync(item.UnityPackageRef.FilePath);
            foreach (var entry in archive.Entries) entry.IsSelected = true;

            _pendingArchive = archive;
            _pendingItem = item;
            ImportEntries = new ObservableCollection<UnityPackageEntry>(archive.Entries);
            StatusMessage = $"已解析 {archive.FileCount} 个文件 / {archive.DirectoryCount} 个文件夹";
            return true;
        }
        catch (Exception ex)
        {
            _pendingArchive = null;
            _pendingItem = null;
            StatusMessage = $"解析失败: {ex.Message}";
            return false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Installs the entries ticked in the dialog and writes the project import ledger.</summary>
    public async Task<bool> CommitUnityPackageImportAsync()
    {
        var archive = _pendingArchive;
        if (Project == null || archive == null) return false;

        IsLoading = true;
        InstallProgress = 0;
        StatusMessage = $"正在导入 {archive.Name} 到 {Project.Name}...";

        try
        {
            var progress = new Progress<double>(p => InstallProgress = p * 100);
            var installedPaths = await _installer.InstallUnityPackageAsync(Project, archive, progress);

            var source = _pendingItem?.UnityPackageRef;
            var selected = archive.Entries.Count(e => e.IsSelected);
            await _libraryService.SaveRecordAsync(Project.Path, new UnityPackageInstallRecord
            {
                PackageId = source?.PackageId ?? archive.Name,
                FilePath = source?.FilePath ?? archive.FilePath,
                DisplayName = source?.DisplayName ?? archive.Name,
                Version = source?.Version ?? string.Empty,
                InstalledAt = DateTime.Now,
                FileCount = selected,
                InstalledPaths = installedPaths.ToList()
            });

            // LoadPackagesAsync overwrites StatusMessage with its own summary, so report after the reload.
            var importMessage = $"已导入 {selected} 项资源到 {Project.Name}";
            await LoadPackagesAsync();
            StatusMessage = importMessage;
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"导入失败: {ex.Message}";
            return false;
        }
        finally
        {
            _pendingArchive = null;
            _pendingItem = null;
            ImportEntries = [];
            IsLoading = false;
            InstallProgress = 0;
        }
    }

    public void CancelUnityPackageImport()
    {
        _pendingArchive = null;
        _pendingItem = null;
        ImportEntries = [];
    }

    [RelayCommand]
    public async Task ChangePackageVersionAsync((UnifiedPackageItem Item, string NewVersion) param)
    {
        var item = param.Item;
        var newVersion = param.NewVersion;

        if (Project == null || item.VpmPackageRef == null) return;
        if (item.InstalledVersion == newVersion) return;

        IsLoading = true;
        InstallProgress = 0;
        StatusMessage = $"正在将 {item.DisplayName} 切换至 v{newVersion}...";

        try
        {
            VpmPackage targetPkg;
            if (item.VpmPackageRef.Versions.TryGetValue(newVersion, out var exact))
            {
                targetPkg = exact;
            }
            else
            {
                targetPkg = item.VpmPackageRef;
                targetPkg.Version = newVersion;
            }

            var progress = new Progress<double>(p => InstallProgress = p * 100);
            await _installer.InstallVpmPackageAsync(Project, targetPkg, progress);

            await LoadPackagesAsync();
            StatusMessage = $"已将 {item.DisplayName} 切换至 v{newVersion}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"切换版本失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            InstallProgress = 0;
        }
    }

    [RelayCommand]
    public async Task UpgradeAllPackagesAsync()
    {
        if (Project == null) return;

        var upgradable = AllPackages.Where(p => p.CanUpgrade && p.VpmPackageRef != null).ToList();
        if (upgradable.Count == 0) return;

        IsLoading = true;
        InstallProgress = 0;

        for (int i = 0; i < upgradable.Count; i++)
        {
            var item = upgradable[i];
            StatusMessage = $"正在升级 ({i + 1}/{upgradable.Count}) {item.DisplayName}...";
            try
            {
                await _installer.InstallVpmPackageAsync(Project, item.VpmPackageRef!);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Upgrade {item.Id} failed: {ex.Message}");
            }
            InstallProgress = (double)(i + 1) / upgradable.Count * 100;
        }

        await LoadPackagesAsync();
        StatusMessage = "所有软件包已升级至最新版本！";
        IsLoading = false;
        InstallProgress = 0;
    }

    [RelayCommand]
    public async Task RemovePackageAsync(UnifiedPackageItem item)
    {
        if (Project == null) return;

        IsLoading = true;
        StatusMessage = $"正在移除 {item.DisplayName}...";

        try
        {
            if (item.Category == PackageCategory.Vpm)
            {
                await _installer.RemoveVpmPackageAsync(Project, item.Id);
            }
            else if (item.Category == PackageCategory.External)
            {
                if (item.ExternalRef == null)
                {
                    StatusMessage = $"{item.DisplayName} 缺少外部包记录，无法自动撤销";
                    return;
                }

                await _externalService.RemoveFromProjectAsync(Project, item.ExternalRef);
            }
            else if (item.Category == PackageCategory.UnityPackage)
            {
                if (item.InstallRecordRef == null)
                {
                    StatusMessage = $"{item.DisplayName} 没有导入记录，无法撤销";
                    return;
                }

                await _installer.RemoveUnityPackageAsync(Project, item.InstallRecordRef.InstalledPaths);
                await _libraryService.RemoveRecordAsync(Project.Path, item.InstallRecordRef.PackageId);
            }

            await LoadPackagesAsync();
            StatusMessage = $"已移除 {item.DisplayName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"移除失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void ToggleSelectAll()
    {
        var selectAll = IsAllSelected;
        foreach (var p in FilteredPackages)
        {
            p.IsSelected = selectAll;
        }

        RefreshSelectionState();
    }

    private void ApplyFilter()
    {
        IEnumerable<UnifiedPackageItem> query = AllPackages;

        // Status conditions OR together; an empty group filters nothing.
        var statusActive = FilterInstalled || FilterNotInstalled || FilterUpgradable;
        if (statusActive)
        {
            query = query.Where(p =>
                (FilterInstalled && p.IsInstalled) ||
                (FilterNotInstalled && !p.IsInstalled) ||
                (FilterUpgradable && p.CanUpgrade));
        }

        var typeActive = FilterVpm || FilterUnityPackage || FilterExternal;
        if (typeActive)
        {
            query = query.Where(p =>
                (FilterVpm && p.Category == PackageCategory.Vpm) ||
                (FilterUnityPackage && p.Category == PackageCategory.UnityPackage) ||
                (FilterExternal && p.Category == PackageCategory.External));
        }

        // Local packages carry a supplier tag, so a Booth import is its own selectable class.
        var originActive = FilterBooth || FilterAssetStore;
        if (originActive)
        {
            query = query.Where(p =>
                (FilterBooth && p.Origin == PackageOrigin.Booth) ||
                (FilterAssetStore && p.Origin == PackageOrigin.AssetStore));
        }

        ActiveFilterCount = (FilterInstalled ? 1 : 0) + (FilterNotInstalled ? 1 : 0) + (FilterUpgradable ? 1 : 0)
                            + (FilterVpm ? 1 : 0) + (FilterUnityPackage ? 1 : 0) + (FilterExternal ? 1 : 0)
                            + (FilterBooth ? 1 : 0) + (FilterAssetStore ? 1 : 0);

        // Apply search query
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var q = SearchQuery.ToLowerInvariant();
            query = query.Where(p =>
                p.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.Id.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.SourceName.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        FilteredPackages = new ObservableCollection<UnifiedPackageItem>(query);
    }

    private static Version ParseVersion(string version)
    {
        var parts = version.Split('-')[0];
        return Version.TryParse(parts, out var v) ? v : new Version(0, 0, 0);
    }

    private static int CompareVersions(string a, string b)
    {
        var va = ParseVersion(a);
        var vb = ParseVersion(b);
        return va.CompareTo(vb);
    }
}
