using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Akashic.Core.Models;
using Akashic.Core.Services;

namespace Akashic.ViewModels;

/// <summary>
/// ViewModel for the Projects list page. <see cref="Projects"/> is the full list and
/// <see cref="Visible"/> is what the page renders after the category, cloud and sort choices.
/// </summary>
public partial class ProjectsViewModel : ObservableObject
{
    public const string CategoryAll = "全部分类";
    public const string CloudAll = "全部云端状态";
    public const string SortName = "按名称";
    public const string SortNewest = "最近打开（新→旧）";
    public const string SortOldest = "最近打开（旧→新）";

    private readonly IProjectService _projectService;
    private readonly IVRChatBlueprintScanService _blueprintScan;
    private readonly IPackageInstaller _installer;
    private readonly IVpmRegistryService _registry;

    [ObservableProperty]
    private ObservableCollection<UnityProject> _projects = [];

    [ObservableProperty]
    private ObservableCollection<UnityProject> _visible = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CloudLinkHint))]
    private int _cloudIdCount;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _category = CategoryAll;

    [ObservableProperty]
    private string _cloudState = CloudAll;

    [ObservableProperty]
    private string _sortBy = SortName;

    /// <summary>What the background cloud match is doing, so a late-appearing link is not a mystery.</summary>
    [ObservableProperty]
    private string _cloudProgress = string.Empty;

    [ObservableProperty]
    private string _listSummary = string.Empty;

    public IReadOnlyList<string> CategoryOptions { get; } =
        [CategoryAll, "VRChat 项目", "世界", "虚拟形象", "非 VRChat 项目"];

    public IReadOnlyList<string> CloudOptions { get; } =
        [CloudAll, "已上传到云端", "未上传到云端"];

    public IReadOnlyList<string> SortOptions { get; } =
        [SortName, SortNewest, SortOldest];

    private ObservableCollection<UnityProject>? _tracked;

    private int _linkRunning;

    public ProjectsViewModel(
        IProjectService projectService,
        IVRChatBlueprintScanService blueprintScan,
        IPackageInstaller installer,
        IVpmRegistryService registry)
    {
        _projectService = projectService;
        _blueprintScan = blueprintScan;
        _installer = installer;
        _registry = registry;
    }

    /// <summary>Shown when nothing can be matched yet, so the missing step is not a mystery.</summary>
    public string CloudLinkHint => CloudIdCount > 0
        ? string.Empty
        : "在「VRChat 数据同步」页登录后读取云端清单，这里才会标出可跳转的对应关系";

    partial void OnProjectsChanged(ObservableCollection<UnityProject> value)
    {
        if (_tracked is not null) _tracked.CollectionChanged -= OnProjectsCollectionChanged;

        _tracked = value;
        value.CollectionChanged += OnProjectsCollectionChanged;
        Rebuild();
    }

    private void OnProjectsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    partial void OnCategoryChanged(string value) => Rebuild();

    partial void OnCloudStateChanged(string value) => Rebuild();

    partial void OnSortByChanged(string value) => Rebuild();

    /// <summary>Applies the category, cloud and sort choices to <see cref="Projects"/>.</summary>
    private void Rebuild()
    {
        IEnumerable<UnityProject> query = Projects;

        query = Category switch
        {
            "VRChat 项目" => query.Where(p => p.IsVrcProject),
            "世界" => query.Where(p => p.IsWorldProject),
            "虚拟形象" => query.Where(p => p.IsAvatarProject),
            "非 VRChat 项目" => query.Where(p => !p.IsVrcProject),
            _ => query
        };

        query = CloudState switch
        {
            "已上传到云端" => query.Where(p => p.HasCloudLink),
            "未上传到云端" => query.Where(p => !p.HasCloudLink),
            _ => query
        };

        query = SortBy switch
        {
            SortNewest => query.OrderByDescending(p => p.LastModified),
            SortOldest => query.OrderBy(p => p.LastModified),
            _ => query.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
        };

        Visible = new ObservableCollection<UnityProject>(query);

        ListSummary = Visible.Count == Projects.Count
            ? $"共 {Projects.Count} 个项目"
            : $"显示 {Visible.Count} / 共 {Projects.Count} 个项目";
    }

    [RelayCommand]
    public async Task LoadProjectsAsync()
    {
        IsLoading = true;
        StatusMessage = "正在加载项目...";

        RefreshCreationInputs();

        try
        {
            var projects = await _projectService.GetProjectsAsync();

            // Only the cloud list itself is read here — it is one cached file. Matching every project's
            // scenes against it is what takes time, so that waits for the background pass below.
            CloudIdCount = _blueprintScan.CloudIds.Count;

            Projects = new ObservableCollection<UnityProject>(projects);
            StatusMessage = $"已加载 {projects.Count} 个项目";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }

        StartCloudLink();
    }

    /// <summary>Starts a background match if one is not already running.</summary>
    private void StartCloudLink()
    {
        if (Interlocked.CompareExchange(ref _linkRunning, 1, 0) != 0) return;

        _ = LinkCloudAsync();
    }

    /// <summary>
    /// Fills in each card's cloud link after the list is already on screen. The scan is cached per
    /// file, so the first pass is the slow one and later visits fill in almost immediately.
    /// </summary>
    private async Task LinkCloudAsync()
    {
        try
        {
            var cloud = _blueprintScan.CloudIds;

            if (cloud.Count == 0) return;

            var wanted = new HashSet<string>(cloud, StringComparer.OrdinalIgnoreCase);
            var snapshot = Projects.ToList();
            var matched = 0;

            for (var i = 0; i < snapshot.Count; i++)
            {
                var project = snapshot[i];
                CloudProgress = $"正在后台匹配云端内容 {i + 1} / {snapshot.Count}…";

                IReadOnlyList<string> ids;

                try
                {
                    ids = await _blueprintScan.ScanAsync(project);
                }
                catch (Exception)
                {
                    // A project on a disconnected drive reads as "never uploaded", not as a failure.
                    ids = [];
                }

                var hits = ids.Where(wanted.Contains).ToList();
                project.CloudBlueprintIds = hits;

                if (hits.Count > 0) matched++;

                // A card only disappears from a filtered view once its real state is known.
                if (CloudState != CloudAll) Rebuild();

                // Let the cards repaint between scans instead of all at once at the end.
                await Task.Yield();
            }

            StatusMessage = matched > 0
                ? $"已加载 {Projects.Count} 个项目 · {matched} 个匹配到云端内容"
                : $"已加载 {Projects.Count} 个项目 · 没有项目和云端内容对上";
        }
        catch (Exception ex)
        {
            StatusMessage = $"云端匹配失败：{ex.Message}";
        }
        finally
        {
            CloudProgress = string.Empty;
            Volatile.Write(ref _linkRunning, 0);
        }
    }

    [RelayCommand]
    public async Task AddProjectAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        IsLoading = true;
        try
        {
            var project = await _projectService.AddProjectAsync(path);
            if (project != null)
            {
                Projects.Add(project);
                StatusMessage = $"已添加项目: {project.Name}";
            }
            else
            {
                StatusMessage = "无效的 Unity 项目路径";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"添加失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task RemoveProjectAsync(UnityProject project)
    {
        await _projectService.RemoveProjectAsync(project.Path);
        Projects.Remove(project);
        StatusMessage = $"已移除项目: {project.Name}";
    }

    [RelayCommand]
    public async Task RefreshProjectAsync(UnityProject project)
    {
        var index = Projects.IndexOf(project);
        if (index < 0) return;

        var refreshed = await _projectService.RefreshProjectAsync(project);
        Projects[index] = refreshed;
    }

    /// <summary>Editors on this machine, offered as the version a new project should be created with.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DefaultVersionIndex))]
    private ObservableCollection<string> _unityVersions = [];

    /// <summary>Templates the user saved, plus the built-in blank entry the dialog starts on.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DefaultVersionIndex))]
    private ObservableCollection<ProjectTemplate> _templates = [];

    [ObservableProperty]
    private string _defaultProjectPath = string.Empty;

    /// <summary>Where the new project dialog will put a project, so the page can show it live.</summary>
    public string DefaultParent => string.IsNullOrWhiteSpace(DefaultProjectPath)
        ? Path.GetFullPath(".")
        : DefaultProjectPath;

    /// <summary>
    /// Which version the new project dialog opens on: the newest one an official VRChat template
    /// supports, failing that the newest one any template supports, failing that simply the newest.
    /// Opening on a version with nothing to offer would show an empty template list for no reason.
    /// </summary>
    public int DefaultVersionIndex =>
        FirstVersionWith(v => v.IsBuiltIn) ?? FirstVersionWith(_ => true) ?? 0;

    private int? FirstVersionWith(Func<ProjectTemplate, bool> kind)
    {
        for (var i = 0; i < UnityVersions.Count; i++)
        {
            var editor = UnityVersions[i];
            if (Templates.Any(t => kind(t) && BuiltinTemplates.MatchesUnity(t.UnityVersion, editor))) return i;
        }

        return null;
    }

    /// <summary>Reads the editor list and templates alongside the projects, since the dialog needs all three.</summary>
    private void RefreshCreationInputs()
    {
        DefaultProjectPath = _projectService.GetDefaultProjectPath();
        Templates = new ObservableCollection<ProjectTemplate>(_projectService.GetTemplates());

        try
        {
            UnityVersions = new ObservableCollection<string>(
                _projectService.DiscoverUnityEditors()
                    .Select(e => e.Version)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    // Newest first by the version's own numbers, so Unity 6's 6000.x does not lose to
                    // 2022.x the way it does in a plain string compare.
                    .OrderByDescending(v => v, Comparer<string>.Create(BuiltinTemplates.CompareUnityVersions)));
        }
        catch (Exception)
        {
            UnityVersions = [];
        }
    }

    public async Task<string> DetectProjectsAsync(string directory)
    {
        IsLoading = true;
        try
        {
            var (added, _, message) = await _projectService.DetectProjectsAsync(directory);

            if (added > 0) await LoadProjectsAsync();
            StatusMessage = message;
            return message;
        }
        catch (Exception ex)
        {
            StatusMessage = $"检测项目失败：{ex.Message}";
            return StatusMessage;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public Task<(bool Success, string Message, string? ProjectPath)> CreateProjectAsync(
        string name, string? parent, ProjectTemplate? template, string unityVersion)
    {
        return CreateInternalAsync(name, parent, template, unityVersion);
    }

    private async Task<(bool Success, string Message, string? ProjectPath)> CreateInternalAsync(
        string name, string? parent, ProjectTemplate? template, string unityVersion)
    {
        IsLoading = true;
        try
        {
            var result = await _projectService.CreateProjectAsync(name, parent, template, unityVersion);

            if (result.Success) await LoadProjectsAsync();
            StatusMessage = result.Message;
            return result;
        }
        catch (Exception ex)
        {
            StatusMessage = $"创建项目失败：{ex.Message}";
            return (false, StatusMessage, null);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// A built-in template is only a skeleton until the SDK is in it, so the packages are installed
    /// here. Each one is taken at the newest advertised version the chosen editor can actually load,
    /// which is what keeps a 2019 project from being handed an SDK that requires 2022.3.
    /// </summary>
    public async Task<string> InstallTemplatePackagesAsync(
        UnityProject project, ProjectTemplate? template, string unityVersion)
    {
        var builtin = BuiltinTemplates.Find(template?.BuiltinKey);
        if (builtin is null) return string.Empty;

        IsLoading = true;
        StatusMessage = $"正在为 {project.Name} 安装 {builtin.Name} 所需的软件包…";

        var installed = new List<string>();
        var failed = new List<string>();

        try
        {
            foreach (var (name, range) in builtin.PackageRanges)
            {
                try
                {
                    var advertised = await _registry.GetPackageAsync(name);

                    if (advertised is null)
                    {
                        failed.Add($"{name}（仓库里没有这个包）");
                        continue;
                    }

                    // The template's own range is what decides the version, so a 2019 project gets the
                    // 3.4 line the official template asks for rather than the newest 3.10 build.
                    var wanted = BuiltinTemplates.ChooseVersion(advertised, range, unityVersion);

                    var package = wanted is not null && advertised.Versions.TryGetValue(wanted, out var exact)
                        ? exact
                        : advertised;

                    await _installer.InstallVpmPackageAsync(project, package);
                    installed.Add($"{name} {package.Version}");
                }
                catch (Exception ex)
                {
                    failed.Add($"{name}（{ex.Message}）");
                }

                StatusMessage = $"正在为 {project.Name} 安装软件包… 已完成 {installed.Count}/{builtin.PackageRanges.Count}";
            }
        }
        finally
        {
            IsLoading = false;
        }

        var message = installed.Count > 0
            ? $"已安装 {string.Join(" · ", installed)}"
            : "软件包没有装上";

        if (failed.Count > 0) message += $"；失败 {string.Join(" · ", failed)}";

        StatusMessage = message;
        return message;
    }

    /// <summary>Saves a project as a template and refreshes the list the dialog reads from.</summary>
    public async Task<string> SaveAsTemplateAsync(UnityProject project, string name, string notes)
    {
        try
        {
            var message = await _projectService.SaveTemplateFromProjectAsync(project, name, notes);
            RefreshCreationInputs();
            StatusMessage = message;
            return message;
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存模板失败：{ex.Message}";
            return StatusMessage;
        }
    }
}
