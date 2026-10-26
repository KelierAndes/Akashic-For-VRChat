using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Akashic.Core.Data;
using Akashic.Core.Models;
using Akashic.Core.Services.Preview;

namespace Akashic.Core.Services;

public partial class ProjectService : IProjectService
{
    private readonly string _settingsPath;
    private readonly AkashicDatabase _db;
    private AppSettings _settings;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ProjectService(string? settingsPath = null, AkashicDatabase? database = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Akashic", "settings.json");
        _db = database ?? new AkashicDatabase(Path.GetDirectoryName(_settingsPath));
        _settings = LoadSettings();
        TryAutoImportVccProjects();
    }

    public async Task<List<UnityProject>> GetProjectsAsync()
    {
        var cached = _db.LoadProjectCache();
        var projects = new List<UnityProject>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pruned = false;

        foreach (var path in _settings.ProjectPaths.ToList())
        {
            if (!Directory.Exists(path))
            {
                _settings.ProjectPaths.Remove(path);
                _db.RemoveProject(path);
                pruned = true;
                continue;
            }

            seen.Add(path);
            var signature = ComputeSignature(path);
            if (cached.TryGetValue(path, out var entry) && entry.Signature == signature)
            {
                projects.Add(entry.Project);
                continue;
            }

            var project = await ScanProjectAsync(path);
            if (project != null) projects.Add(project);
        }

        _db.PruneProjects(seen);
        if (pruned) await SaveSettingsAsync();
        return projects;
    }

    public async Task<UnityProject?> AddProjectAsync(string path)
    {
        if (!IsValidUnityProject(path)) return null;

        var fullPath = Path.GetFullPath(path);
        if (!_settings.ProjectPaths.Contains(fullPath))
        {
            _settings.ProjectPaths.Add(fullPath);
            await SaveSettingsAsync();
        }
        return await ScanProjectAsync(fullPath);
    }

    public async Task RemoveProjectAsync(string path)
    {
        var fullPath = Path.GetFullPath(path);
        _settings.ProjectPaths.Remove(fullPath);
        _db.RemoveProject(fullPath);
        await SaveSettingsAsync();
    }

    public Task<UnityProject?> ScanProjectAsync(string path)
    {
        if (!IsValidUnityProject(path)) return Task.FromResult<UnityProject?>(null);

        var project = new UnityProject
        {
            Name = Path.GetFileName(path),
            Path = Path.GetFullPath(path),
            UnityVersion = ReadUnityVersion(path),
            LastModified = Directory.GetLastWriteTime(path)
        };

        project.HasVpmManifest = File.Exists(project.VpmManifestPath);
        project.InstalledPackages = ReadInstalledPackages(project);

        _db.UpsertProject(ComputeSignature(project.Path), project);
        return Task.FromResult<UnityProject?>(project);
    }

    private static string ComputeSignature(string projectPath)
    {
        var parts = new List<string>();
        foreach (var file in new[]
                 {
                     Path.Combine(projectPath, "ProjectSettings", "ProjectVersion.txt"),
                     Path.Combine(projectPath, "Packages", "manifest.json"),
                     Path.Combine(projectPath, "Packages", "vpm-manifest.json")
                 })
        {
            try
            {
                var info = new FileInfo(file);
                parts.Add(info.Exists
                    ? $"{info.LastWriteTimeUtc.Ticks}:{info.Length}"
                    : "-");
            }
            catch (IOException)
            {
                parts.Add("?");
            }
        }

        try
        {
            parts.Add(Directory.GetLastWriteTimeUtc(projectPath).Ticks.ToString());
        }
        catch (IOException)
        {
            parts.Add("?");
        }

        return string.Join("|", parts);
    }

    public async Task<UnityProject> RefreshProjectAsync(UnityProject project)
    {
        var refreshed = await ScanProjectAsync(project.Path);
        return refreshed ?? project;
    }

    public bool IsValidUnityProject(string path)
    {
        if (!Directory.Exists(path)) return false;
        var projectSettings = Path.Combine(path, "ProjectSettings");
        var assets = Path.Combine(path, "Assets");
        return Directory.Exists(projectSettings) && Directory.Exists(assets);
    }

    private string ReadUnityVersion(string projectPath)
    {
        var versionFile = Path.Combine(projectPath, "ProjectSettings", "ProjectVersion.txt");
        if (!File.Exists(versionFile)) return "Unknown";

        try
        {
            var content = File.ReadAllText(versionFile);
            var match = UnityVersionRegex().Match(content);
            return match.Success ? match.Groups[1].Value.Trim() : "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }

    private List<InstalledPackage> ReadInstalledPackages(UnityProject project)
    {
        var packages = new List<InstalledPackage>();

        if (File.Exists(project.ManifestPath))
        {
            try
            {
                var json = File.ReadAllText(project.ManifestPath);
                var manifest = JsonSerializer.Deserialize<UpmManifest>(json, JsonOptions);
                if (manifest?.Dependencies != null)
                {
                    foreach (var (name, version) in manifest.Dependencies)
                    {
                        if (IsUnityModule(name)) continue;

                        packages.Add(new InstalledPackage
                        {
                            Name = name,
                            Version = version,
                            DisplayName = name,
                            Source = DeterminePackageSource(name, version)
                        });
                    }
                }
            }
            catch {  }
        }

        if (project.HasVpmManifest && File.Exists(project.VpmManifestPath))
        {
            try
            {
                var json = File.ReadAllText(project.VpmManifestPath);
                var vpmManifest = JsonSerializer.Deserialize<VpmManifest>(json, JsonOptions);
                if (vpmManifest?.Locked != null)
                {
                    foreach (var (name, locked) in vpmManifest.Locked)
                    {
                        var existing = packages.Find(p => p.Name == name);
                        if (existing != null)
                        {
                            existing.Version = locked.Version;
                            existing.Source = PackageSource.Vpm;
                            existing.IsLocked = true;
                        }
                        else
                        {
                            packages.Add(new InstalledPackage
                            {
                                Name = name,
                                Version = locked.Version,
                                DisplayName = name,
                                Source = PackageSource.Vpm,
                                IsLocked = true
                            });
                        }
                    }
                }
            }
            catch {  }
        }

        foreach (var pkg in packages)
            ApplyPackageMetadata(project, pkg);

        return packages;
    }

    private static void ApplyPackageMetadata(UnityProject project, InstalledPackage pkg)
    {
        var metaPath = Path.Combine(project.PackagesPath, pkg.Name, "package.json");

        if (!File.Exists(metaPath) && pkg.Version.StartsWith("file:"))
            metaPath = Path.Combine(ResolveFilePath(project, pkg.Version), "package.json");

        if (!File.Exists(metaPath)) return;

        try
        {
            var meta = JsonSerializer.Deserialize<PackageJson>(File.ReadAllText(metaPath), JsonOptions);
            if (meta == null) return;

            if (!string.IsNullOrWhiteSpace(meta.DisplayName)) pkg.DisplayName = meta.DisplayName;
            if (!IsVersionLike(pkg.Version) && IsVersionLike(meta.Version)) pkg.Version = meta.Version!;
        }
        catch {  }
    }

    private static string ResolveFilePath(UnityProject project, string version)
    {
        var target = version["file:".Length..].Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(target)) return Path.GetFullPath(target);
        return Path.GetFullPath(Path.Combine(project.PackagesPath, target));
    }

    private static bool IsUnityModule(string name) =>
        name.StartsWith("com.unity.", StringComparison.OrdinalIgnoreCase);

    private static bool IsVersionLike(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains(':') || value.Contains('/')) return false;
        return Version.TryParse(value.Split('-')[0], out _);
    }

    private static PackageSource DeterminePackageSource(string name, string version)
    {
        if (name.StartsWith("com.vrchat.") || name.StartsWith("com.vrmc."))
            return PackageSource.Vpm;
        if (version.StartsWith("file:"))
            return PackageSource.Local;
        if (version.StartsWith("https://") || version.StartsWith("git+"))
            return PackageSource.Git;
        return PackageSource.Unknown;
    }

    private AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch {  }
        return new AppSettings();
    }

    private async Task SaveSettingsAsync()
    {
        var dir = Path.GetDirectoryName(_settingsPath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var current = LoadSettings();
        current.ProjectPaths = [.. _settings.ProjectPaths];

        var json = JsonSerializer.Serialize(current, JsonOptions);
        await File.WriteAllTextAsync(_settingsPath, json);
    }

    private void TryAutoImportVccProjects()
    {
        if (_settings.ProjectPaths.Count > 0) return;

        try
        {
            var vccPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VRChatCreatorCompanion", "settings.json");

            if (File.Exists(vccPath))
            {
                var json = File.ReadAllText(vccPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("userProjects", out var userProjects) &&
                    userProjects.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in userProjects.EnumerateArray())
                    {
                        var p = elem.GetString();
                        if (!string.IsNullOrEmpty(p) && Directory.Exists(p) && IsValidUnityProject(p))
                        {
                            var full = Path.GetFullPath(p);
                            if (!_settings.ProjectPaths.Contains(full))
                                _settings.ProjectPaths.Add(full);
                        }
                    }
                }
            }
        }
        catch { }
    }

    public string? FindUnityEditor(string? version = null)
    {
        var editors = DiscoverUnityEditors();
        if (editors.Count == 0) return null;

        if (string.IsNullOrEmpty(version)) return editors[0].Path;

        return editors.FirstOrDefault(e => VersionMatches(e.Version, version)
                                           || e.Path.Contains(version, StringComparison.OrdinalIgnoreCase))?.Path;
    }

    public string? FindUnityHub()
    {
        try
        {
            var vccSettingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VRChatCreatorCompanion", "settings.json");
            if (File.Exists(vccSettingsPath))
            {
                var json = File.ReadAllText(vccSettingsPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("pathToUnityHub", out var hubElem))
                {
                    var hubPath = hubElem.GetString();
                    if (!string.IsNullOrEmpty(hubPath) && File.Exists(hubPath))
                        return hubPath;
                }
            }
        }
        catch { }

        string[] commonHubPaths =
        [
            @"F:\Program Files\Unity Hub\Unity Hub.exe",
            @"C:\Program Files\Unity Hub\Unity Hub.exe",
            @"D:\Program Files\Unity Hub\Unity Hub.exe",
            @"E:\Program Files\Unity Hub\Unity Hub.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity Hub", "Unity Hub.exe")
        ];

        foreach (var p in commonHubPaths)
        {
            if (File.Exists(p)) return p;
        }

        return null;
    }

    private const string SourceManual = "手动添加";
    private const string SourceHub = "Unity Hub";
    private const string SourceVcc = "VCC";
    private const string SourceScanned = "扫描目录";

    private string VccSettingsPath() => LoadSettings().VccSettingsPath;

    public List<UnityEditorEntry> DiscoverUnityEditors()
    {
        var found = new List<UnityEditorEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? path, string? version, string source)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

            var full = Path.GetFullPath(path);
            if (!seen.Add(full)) return;

            found.Add(new UnityEditorEntry { Path = full, Version = ResolveVersion(full, version), Source = source });
        }

        var settings = LoadSettings();

        foreach (var entry in settings.UnityEditorEntries)
            Add(entry.Path, entry.Version, SourceManual);

        try
        {
            var hubEditorsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UnityHub", "editors.json");
            if (File.Exists(hubEditorsPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(hubEditorsPath));
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.TryGetProperty("location", out var location))
                        Add(ReadLocation(location), prop.Name, SourceHub);
                }
            }
        }
        catch (JsonException) { }
        catch (IOException) { }

        try
        {
            var vccPath = VccSettingsPath();
            if (File.Exists(vccPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(vccPath));
                var root = doc.RootElement;

                if (root.TryGetProperty("preferredUnityEditors", out var preferred) &&
                    preferred.ValueKind == JsonValueKind.Object)
                    foreach (var prop in preferred.EnumerateObject())
                        Add(prop.Value.GetString(), prop.Name, SourceVcc);

                if (root.TryGetProperty("unityEditors", out var list) && list.ValueKind == JsonValueKind.Array)
                    foreach (var item in list.EnumerateArray())
                        Add(item.GetString(), null, SourceVcc);

                if (root.TryGetProperty("pathToUnityExe", out var exe))
                    Add(exe.GetString(), null, SourceVcc);
            }
        }
        catch (JsonException) { }
        catch (IOException) { }

        foreach (var root in settings.UnityEditorPaths)
        {
            if (!Directory.Exists(root)) continue;

            var direct = Path.Combine(root, "Editor", "Unity.exe");
            if (File.Exists(direct))
            {
                Add(direct, null, SourceScanned);
                continue;
            }

            try
            {
                foreach (var sub in Directory.GetDirectories(root))
                    Add(Path.Combine(sub, "Editor", "Unity.exe"), null, SourceScanned);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        foreach (var drive in DriveInfo.GetDrives()
                     .Where(d => d.DriveType == DriveType.Fixed)
                     .Select(d => d.RootDirectory.FullName))
        {
            foreach (var root in new[]
                     {
                         Path.Combine(drive, "Unity"),
                         Path.Combine(drive, "Program Files", "Unity", "Hub", "Editor")
                     })
                ScanEditorRoot(root, Add);
        }

        return found.OrderByDescending(e => e.Version, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void ScanEditorRoot(
        string root, Action<string?, string?, string> add)
    {
        if (!Directory.Exists(root)) return;

        void Try(string dir) => add(Path.Combine(dir, "Editor", "Unity.exe"), null, SourceScanned);

        try
        {
            Try(root);

            foreach (var sub in Directory.GetDirectories(root))
            {
                Try(sub);

                foreach (var inner in Directory.GetDirectories(sub))
                    Try(inner);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public async Task<string> AddUnityEditorAsync(string pickedPath)
    {
        var full = ResolveEditorExecutable(pickedPath);
        if (full is null)
            return "所选位置里没有找到 Unity.exe，请选择编辑器目录或其 Editor 子目录。";

        var settings = LoadSettings();

        if (settings.UnityEditorEntries.Any(e => PathEquals(e.Path, full)))
            return $"{ResolveVersion(full, null)} 已经在手动列表里。";

        var entry = new UnityEditorEntry
        {
            Path = full,
            Version = ResolveVersion(full, null),
            Source = SourceManual
        };
        settings.UnityEditorEntries.Add(entry);
        await SaveOwnedFieldsAsync(settings);

        return $"已添加 {entry.Version}。";
    }

    private static string? ResolveEditorExecutable(string? picked)
    {
        if (string.IsNullOrWhiteSpace(picked)) return null;

        var full = Path.GetFullPath(picked.Trim().Trim('"'));

        if (File.Exists(full))
            return string.Equals(Path.GetFileName(full), "Unity.exe", StringComparison.OrdinalIgnoreCase) ? full : null;

        if (!Directory.Exists(full)) return null;

        foreach (var candidate in new[]
                 {
                     Path.Combine(full, "Unity.exe"),
                     Path.Combine(full, "Editor", "Unity.exe")
                 })
            if (File.Exists(candidate)) return candidate;

        return null;
    }

    public async Task<string> RemoveUnityEditorAsync(string exePath)
    {
        var settings = LoadSettings();
        var removed = settings.UnityEditorEntries.RemoveAll(e => PathEquals(e.Path, exePath));

        if (removed > 0) await SaveOwnedFieldsAsync(settings);

        return removed > 0 ? "已从手动列表移除。" : "只有手动添加的编辑器可以移除。";
    }

    public string GetBackupDirectory()
    {
        var configured = LoadSettings().ProjectBackupPath;
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        try
        {
            var vccPath = VccSettingsPath();
            if (File.Exists(vccPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(vccPath));
                if (doc.RootElement.TryGetProperty("projectBackupPath", out var bp) &&
                    bp.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(bp.GetString()))
                    return bp.GetString()!;
            }
        }
        catch (JsonException) { }
        catch (IOException) { }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Akashic", "Backups");
    }

    public async Task SetBackupDirectoryAsync(string directory)
    {
        var full = Path.GetFullPath(directory.Trim().Trim('"'));
        Directory.CreateDirectory(full);

        var settings = LoadSettings();
        settings.ProjectBackupPath = full;
        await SaveOwnedFieldsAsync(settings);
    }

    public string GetPreviewCacheDirectory() => PreviewCachePaths.Root;

    public async Task<string> SetPreviewCacheDirectoryAsync(string directory)
    {
        var full = string.IsNullOrWhiteSpace(directory)
            ? PreviewCachePaths.DefaultDirectory()
            : Path.GetFullPath(directory.Trim().Trim('"'));

        var applied = PreviewCachePaths.Configure(full);

        var settings = LoadSettings();
        settings.PreviewCacheDirectory = applied;
        await SaveOwnedFieldsAsync(settings);
        return applied;
    }

    public bool BackupExcludesPackages => LoadSettings().BackupExcludePackages;

    public async Task SetBackupExcludesPackagesAsync(bool exclude)
    {
        var settings = LoadSettings();
        settings.BackupExcludePackages = exclude;
        await SaveOwnedFieldsAsync(settings);
    }

    public string GetDefaultProjectPath()
    {
        var configured = LoadSettings().DefaultProjectPath;
        return string.IsNullOrWhiteSpace(configured) ? Path.GetFullPath(".") : configured;
    }

    public async Task SetDefaultProjectPathAsync(string directory)
    {
        var full = Path.GetFullPath(directory.Trim().Trim('"'));
        Directory.CreateDirectory(full);

        var settings = LoadSettings();
        settings.DefaultProjectPath = full;
        await SaveOwnedFieldsAsync(settings);
    }

    private async Task SaveOwnedFieldsAsync(AppSettings updated)
    {
        var dir = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var current = LoadSettings();
        current.UnityEditorEntries = updated.UnityEditorEntries;
        current.ProjectBackupPath = updated.ProjectBackupPath;
        current.BackupExcludePackages = updated.BackupExcludePackages;
        current.DefaultProjectPath = updated.DefaultProjectPath;
        current.ProjectTemplates = updated.ProjectTemplates;

        await File.WriteAllTextAsync(_settingsPath, JsonSerializer.Serialize(current, JsonOptions));
    }

    private static bool VersionMatches(string? known, string? wanted)
    {
        if (string.IsNullOrWhiteSpace(known) || string.IsNullOrWhiteSpace(wanted)) return false;

        return string.Equals(known, wanted, StringComparison.OrdinalIgnoreCase)
               || (wanted.Length >= 4 && known.StartsWith(wanted[..4], StringComparison.OrdinalIgnoreCase));
    }

    private static bool PathEquals(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;

        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string? ReadLocation(JsonElement location) => location.ValueKind switch
    {
        JsonValueKind.Array when location.GetArrayLength() > 0 => location[0].GetString(),
        JsonValueKind.String => location.GetString(),
        _ => null
    };

    private static string ResolveVersion(string exePath, string? known)
    {
        if (!string.IsNullOrWhiteSpace(known)) return known.Trim();

        try
        {
            var product = System.Diagnostics.FileVersionInfo.GetVersionInfo(exePath).ProductVersion ?? string.Empty;
            var match = UnityReleaseVersionRegex().Match(product);
            if (match.Success) return match.Value;
        }
        catch (Exception) { }

        var editorDir = Path.GetDirectoryName(exePath);
        var folderMatch = UnityReleaseVersionRegex().Match(Path.GetFileName(editorDir ?? string.Empty));
        if (folderMatch.Success) return folderMatch.Value;

        var parentMatch = UnityReleaseVersionRegex().Match(
            Path.GetFileName(Path.GetDirectoryName(editorDir) ?? string.Empty));

        return parentMatch.Success ? parentMatch.Value : "未知版本";
    }

    public (bool Success, string Message, string? PathUsed) LaunchUnity(UnityProject project)
    {
        var editorPath = FindUnityEditor(project.UnityVersion);
        if (!string.IsNullOrEmpty(editorPath) && File.Exists(editorPath))
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = editorPath,
                    Arguments = $"-projectPath \"{project.Path}\"",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
                return (true, $"已启动 Unity 编辑器 ({project.UnityVersion})", editorPath);
            }
            catch (Exception ex)
            {
                return (false, $"启动 Unity 异常: {ex.Message}", editorPath);
            }
        }

        var hubPath = FindUnityHub();
        if (!string.IsNullOrEmpty(hubPath) && File.Exists(hubPath))
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = hubPath,
                    Arguments = $"-- --project-path \"{project.Path}\"",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
                return (true, "已通过 Unity Hub 打开项目", hubPath);
            }
            catch (Exception ex)
            {
                return (false, $"通过 Unity Hub 打开失败: {ex.Message}", hubPath);
            }
        }

        return (false, $"未找到匹配 Unity 编辑器或 Unity Hub。项目所需版本: {project.UnityVersion}", null);
    }

    public async Task<(bool Success, string Message, string? BackupPath)> CreateProjectBackupAsync(UnityProject project, IProgress<double>? progress = null)
    {
        progress?.Report(0.1);
        string backupDir = GetBackupDirectory();

        if (!Directory.Exists(backupDir))
            Directory.CreateDirectory(backupDir);

        var safeProjectName = string.Join("_", project.Name.Split(Path.GetInvalidFileNameChars()));
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupZip = Path.Combine(backupDir, $"{safeProjectName}_{timestamp}.zip");

        var excludePackages = LoadSettings().BackupExcludePackages;

        progress?.Report(0.2);

        try
        {
            await Task.Run(() =>
            {
                using var zip = System.IO.Compression.ZipFile.Open(backupZip, System.IO.Compression.ZipArchiveMode.Create);
                string[] sourceFolders = ["Assets", "Packages", "ProjectSettings"];

                foreach (var folder in sourceFolders)
                {
                    var dirPath = Path.Combine(project.Path, folder);
                    if (!Directory.Exists(dirPath)) continue;

                    var files = folder == "Packages" && excludePackages
                        ? Directory.GetFiles(dirPath, "*.json")
                        : Directory.GetFiles(dirPath, "*", SearchOption.AllDirectories);

                    foreach (var file in files)
                    {
                        var rel = Path.GetRelativePath(project.Path, file);
                        zip.CreateEntryFromFile(file, rel, System.IO.Compression.CompressionLevel.Fastest);
                    }
                }
            });

            progress?.Report(1.0);
            return (true, $"备份已保存至: {backupZip}" + (excludePackages ? "（不含软件包）" : string.Empty), backupZip);
        }
        catch (Exception ex)
        {
            return (false, $"备份失败: {ex.Message}", null);
        }
    }

    private static readonly string[] BackupRoots = ["Assets", "Packages", "ProjectSettings"];

    public List<BackupArchive> ListBackupArchives()
    {
        var dir = GetBackupDirectory();
        if (!Directory.Exists(dir)) return [];

        var archives = new List<BackupArchive>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.zip", SearchOption.TopDirectoryOnly))
        {
            var archive = BackupArchive.TryParse(file);
            if (archive != null) archives.Add(archive);
        }

        return archives.OrderByDescending(a => a.CreatedAt).ToList();
    }

    public async Task<(bool Success, string Message)> RestoreBackupAsync(
        BackupArchive archive, string targetProjectPath, bool overwrite,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (archive == null) return (false, "没有选中备份。");
        if (string.IsNullOrWhiteSpace(targetProjectPath)) return (false, "没有填写目标工程目录。");
        if (!File.Exists(archive.FilePath)) return (false, $"备份文件已经不在了: {archive.FilePath}");
        if (!archive.Readable) return (false, $"备份读不出文件清单，可能没写完或已损坏: {archive.FileName}");

        var target = Path.GetFullPath(targetProjectPath.TrimEnd('"'));
        var existedBefore = Directory.Exists(target);
        if (!existedBefore)
        {
            try
            {
                Directory.CreateDirectory(target);
            }
            catch (Exception ex)
            {
                return (false, $"目标目录创建失败: {ex.Message}");
            }
        }

        var prefix = target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;

        progress?.Report(0.05);

        try
        {
            return await Task.Run(() =>
            {
                using var zip = ZipFile.Open(archive.FilePath, ZipArchiveMode.Read);
                var entries = zip.Entries;
                var total = entries.Count;

                var written = 0;
                var kept = 0;
                var ignored = 0;
                var failed = 0;

                for (var i = 0; i < total; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var entry = entries[i];

                    if (string.IsNullOrEmpty(entry.Name)) continue;

                    var rel = entry.FullName.Replace('\\', '/');
                    if (!BackupRoots.Contains(rel.Split('/')[0], StringComparer.Ordinal))
                    {
                        ignored++;
                        continue;
                    }

                    var dest = Path.GetFullPath(
                        Path.Combine(target, rel.Replace('/', Path.DirectorySeparatorChar)));
                    if (!dest.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        ignored++;
                        continue;
                    }

                    if (File.Exists(dest) && !overwrite)
                    {
                        kept++;
                        continue;
                    }

                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                        entry.ExtractToFile(dest, overwrite);
                        written++;
                    }
                    catch (Exception)
                    {
                        failed++;
                    }

                    if (i % 64 == 0 || i == total - 1) progress?.Report(0.05 + 0.9 * (i + 1) / Math.Max(1, total));
                }

                progress?.Report(1.0);

                var message = $"还原完成：写入 {written} 个文件，保留已存在 {kept} 个";
                if (ignored > 0) message += $"，忽略非工程条目 {ignored} 个";
                if (failed > 0) message += $"，{failed} 个文件写入失败";
                if (!existedBefore) message += $"，目标目录已新建: {target}";

                return (failed == 0, message);
            }, ct);
        }
        catch (OperationCanceledException)
        {
            return (false, "还原已取消，已写入的文件保留在目标目录。");
        }
        catch (Exception ex)
        {
            return (false, $"还原失败: {ex.Message}");
        }
    }


    private static string TemplatesRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Akashic", "Templates");

    public List<ProjectTemplate> GetTemplates()
    {
        var builtins = BuiltinTemplates.Discover().Select(t => new ProjectTemplate
        {
            Name = t.Name,
            Kind = t.Kind,
            UnityVersion = t.UnityVersion,
            Notes = string.IsNullOrWhiteSpace(t.Description) ? "官方模板" : t.Description,
            Directory = t.Directory,
            BuiltinKey = t.Key
        });

        var saved = LoadSettings().ProjectTemplates.OrderByDescending(t => t.CreatedAt);

        return builtins.Concat(saved).ToList();
    }

    public string GetTemplatesDirectory() => TemplatesRoot;

    public async Task<string> SaveTemplateFromProjectAsync(UnityProject project, string name, string notes)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return "模板需要一个名字。";
        if (!IsValidUnityProject(project.Path)) return "这不是一个可读的 Unity 工程。";

        var target = Path.Combine(TemplatesRoot,
            $"{Slug(trimmed)}_{DateTime.Now:yyyyMMdd_HHmmss}");

        try
        {
            Directory.CreateDirectory(target);
            CopyDirectory(Path.Combine(project.Path, "ProjectSettings"), Path.Combine(target, "ProjectSettings"));

            var packages = Path.Combine(target, "Packages");
            Directory.CreateDirectory(packages);

            var sourcePackages = Path.Combine(project.Path, "Packages");
            if (Directory.Exists(sourcePackages))
            {
                foreach (var json in Directory.GetFiles(sourcePackages, "*.json"))
                    File.Copy(json, Path.Combine(packages, Path.GetFileName(json)), true);
            }

            var settings = LoadSettings();
            settings.ProjectTemplates.Add(new ProjectTemplate
            {
                Name = trimmed,
                Kind = project.ProjectTypeDisplayName,
                UnityVersion = project.UnityVersion,
                Notes = notes.Trim(),
                Directory = target,
                SourcePath = project.Path,
                CreatedAt = DateTime.Now
            });

            await SaveOwnedFieldsAsync(settings);
            return $"已保存模板“{trimmed}”。";
        }
        catch (Exception ex)
        {
            TryDeleteTemplateFolder(target);
            return $"保存模板失败：{ex.Message}";
        }
    }

    public async Task<string> DeleteTemplateAsync(ProjectTemplate template)
    {
        if (template.IsBuiltIn) return $"{template.Name} 是应用自带的模板，不能删除。";

        var settings = LoadSettings();
        var kept = settings.ProjectTemplates
            .Where(t => !PathsMatch(t.Directory, template.Directory))
            .ToList();

        var removed = settings.ProjectTemplates.Count - kept.Count;
        settings.ProjectTemplates = kept;
        await SaveOwnedFieldsAsync(settings);

        TryDeleteTemplateFolder(template.Directory);

        return removed > 0 ? $"已删除模板“{template.Name}”。" : "这个模板已经不在列表里了。";
    }

    private static void TryDeleteTemplateFolder(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return;

        var root = Path.GetFullPath(TemplatesRoot);
        var full = Path.GetFullPath(directory);

        if (full.Length <= root.Length ||
            !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            if (Directory.Exists(full)) Directory.Delete(full, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static bool PathsMatch(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) &&
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    public async Task<(bool Success, string Message, string? ProjectPath)> CreateProjectAsync(
        string name, string? parentDirectory, ProjectTemplate? template, string unityVersion)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return (false, "请填写项目名称。", null);
        if (trimmed.Any(Path.GetInvalidFileNameChars().Contains)) return (false, "项目名称里有非法字符。", null);

        var parent = Path.GetFullPath(string.IsNullOrWhiteSpace(parentDirectory)
            ? GetDefaultProjectPath()
            : parentDirectory.Trim().Trim('"'));

        var target = Path.Combine(parent, trimmed);
        if (Directory.Exists(target)) return (false, $"{target} 已经存在，换一个名字或目录。", null);

        try
        {
            Directory.CreateDirectory(target);
            Directory.CreateDirectory(Path.Combine(target, "Assets"));

            var packages = Path.Combine(target, "Packages");
            Directory.CreateDirectory(packages);

            var version = string.IsNullOrWhiteSpace(unityVersion) ? template?.UnityVersion ?? "" : unityVersion.Trim();

            var builtin = BuiltinTemplates.Find(template?.BuiltinKey);

            if (builtin is not null && Directory.Exists(builtin.Directory))
            {
                foreach (var part in new[] { "Assets", "Packages", "ProjectSettings" })
                    CopyDirectory(Path.Combine(builtin.Directory, part), Path.Combine(target, part));

                CopyRelative(builtin.Directory, target, ".gitignore");
                CopyRelative(builtin.Directory, target, Path.Combine("Packages", ".gitignore"));

                if (string.IsNullOrWhiteSpace(version)) version = builtin.UnityVersion;
            }
            else if (builtin is not null)
            {
                Directory.CreateDirectory(Path.Combine(target, "ProjectSettings"));
                await File.WriteAllTextAsync(Path.Combine(packages, "manifest.json"),
                    BuiltinTemplates.ManifestJson);
                await File.WriteAllTextAsync(Path.Combine(packages, "vpm-manifest.json"),
                    "{\n  \"dependencies\": {}\n}\n");

                if (string.IsNullOrWhiteSpace(version)) version = builtin.UnityVersion;
            }
            else if (template is { IsAvailable: true })
            {
                CopyDirectory(Path.Combine(template.Directory, "ProjectSettings"),
                    Path.Combine(target, "ProjectSettings"));

                var from = Path.Combine(template.Directory, "Packages");
                if (Directory.Exists(from))
                {
                    foreach (var json in Directory.GetFiles(from, "*.json"))
                        File.Copy(json, Path.Combine(packages, Path.GetFileName(json)), true);
                }

                if (string.IsNullOrWhiteSpace(version)) version = ReadUnityVersion(template.Directory);
            }
            else
            {
                Directory.CreateDirectory(Path.Combine(target, "ProjectSettings"));
                await File.WriteAllTextAsync(Path.Combine(packages, "manifest.json"),
                    "{\n  \"dependencies\": {}\n}\n");
            }

            var release = UnityReleaseVersionRegex().Match(version);
            if (release.Success)
            {
                await File.WriteAllTextAsync(
                    Path.Combine(target, "ProjectSettings", "ProjectVersion.txt"),
                    $"m_EditorVersion: {release.Value}\n");
            }

            await AddProjectAsync(target);
            return (true, $"已创建项目 {target}", target);
        }
        catch (Exception ex)
        {
            return (false, $"创建项目失败：{ex.Message}", null);
        }
    }

    public async Task<(int Added, int Already, string Message)> DetectProjectsAsync(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return (0, 0, "目录不存在。");

        var root = Path.GetFullPath(directory);

        var candidates = IsValidUnityProject(root)
            ? [root]
            : Directory.EnumerateDirectories(root).Where(IsValidUnityProject).ToList();

        var known = new HashSet<string>(
            LoadSettings().ProjectPaths.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);

        var added = 0;
        var already = 0;

        foreach (var candidate in candidates)
        {
            if (known.Contains(candidate))
            {
                already++;
                continue;
            }

            if (await AddProjectAsync(candidate) is not null) added++;
        }

        return (added, already, $"在 {root} 下发现 {candidates.Count} 个工程 · 新登记 {added} 个 · 已登记 {already} 个");
    }

    private static void CopyRelative(string source, string target, string relative)
    {
        var from = Path.Combine(source, relative);
        if (!File.Exists(from)) return;

        var to = Path.Combine(target, relative);
        var parent = Path.GetDirectoryName(to);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

        File.Copy(from, to, true);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        if (!Directory.Exists(source)) return;

        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);

        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
    }

    private static string Slug(string text)
    {
        var cleaned = new string(text
            .Where(c => !Path.GetInvalidFileNameChars().Contains(c))
            .Select(c => char.IsWhiteSpace(c) ? '_' : c)
            .ToArray());

        return cleaned.Length == 0 ? "template" : cleaned;
    }

    [GeneratedRegex(@"m_EditorVersion:\s*(.+)")]
    private static partial Regex UnityVersionRegex();

    [GeneratedRegex(@"\d{4}\.\d+\.\d+f\d+")]
    private static partial Regex UnityReleaseVersionRegex();
}
