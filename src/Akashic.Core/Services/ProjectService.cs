using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Akashic.Core.Models;

namespace Akashic.Core.Services;

/// <summary>
/// Manages Unity project discovery, registration, and metadata reading.
/// </summary>
public partial class ProjectService : IProjectService
{
    private readonly string _settingsPath;
    private AppSettings _settings;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ProjectService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Akashic", "settings.json");
        _settings = LoadSettings();
        TryAutoImportVccProjects();
    }

    public async Task<List<UnityProject>> GetProjectsAsync()
    {
        var projects = new List<UnityProject>();
        var pruned = false;
        foreach (var path in _settings.ProjectPaths.ToList())
        {
            if (!Directory.Exists(path))
            {
                _settings.ProjectPaths.Remove(path);
                pruned = true;
                continue;
            }
            var project = await ScanProjectAsync(path);
            if (project != null) projects.Add(project);
        }
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

        return Task.FromResult<UnityProject?>(project);
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

        // Read UPM manifest
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
                        // Unity's own modules track the editor version, so they are out of scope here.
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
            catch { /* Ignore parse errors */ }
        }

        // Read VPM manifest and override/add entries
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
            catch { /* Ignore parse errors */ }
        }

        // package.json inside an embedded package carries the friendly name and the real version.
        foreach (var pkg in packages)
            ApplyPackageMetadata(project, pkg);

        return packages;
    }

    private static void ApplyPackageMetadata(UnityProject project, InstalledPackage pkg)
    {
        var metaPath = Path.Combine(project.PackagesPath, pkg.Name, "package.json");

        // A file: reference keeps its files outside Packages/, so the metadata lives next to them.
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
        catch { /* Ignore parse errors */ }
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
        catch { /* Use defaults */ }
        return new AppSettings();
    }

    private async Task SaveSettingsAsync()
    {
        var dir = Path.GetDirectoryName(_settingsPath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        // This service only owns ProjectPaths; re-read so edits made by the registry
        // and UnityPackage library services in the same file are not clobbered.
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
        // 1. Check Unity Hub editors.json (AppData\Roaming\UnityHub\editors.json)
        try
        {
            var hubEditorsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "UnityHub", "editors.json");
            if (File.Exists(hubEditorsPath))
            {
                var json = File.ReadAllText(hubEditorsPath);
                using var doc = JsonDocument.Parse(json);
                string? firstFound = null;

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var edVersion = prop.Name;
                    if (prop.Value.TryGetProperty("location", out var locElem))
                    {
                        string? loc = null;
                        if (locElem.ValueKind == JsonValueKind.Array && locElem.GetArrayLength() > 0)
                            loc = locElem[0].GetString();
                        else if (locElem.ValueKind == JsonValueKind.String)
                            loc = locElem.GetString();

                        if (!string.IsNullOrEmpty(loc) && File.Exists(loc))
                        {
                            firstFound ??= loc;
                            if (!string.IsNullOrEmpty(version))
                            {
                                if (string.Equals(edVersion, version, StringComparison.OrdinalIgnoreCase))
                                    return loc;
                                if (version.Length >= 4 && edVersion.StartsWith(version[..4]))
                                    return loc;
                            }
                        }
                    }
                }

                if (!string.IsNullOrEmpty(firstFound) && string.IsNullOrEmpty(version))
                    return firstFound;
            }
        }
        catch { }

        // 2. Check VCC settings.json (preferredUnityEditors / unityEditors / pathToUnityExe)
        try
        {
            var vccSettingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VRChatCreatorCompanion", "settings.json");
            if (File.Exists(vccSettingsPath))
            {
                var json = File.ReadAllText(vccSettingsPath);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("preferredUnityEditors", out var prefElem))
                {
                    foreach (var prop in prefElem.EnumerateObject())
                    {
                        var path = prop.Value.GetString();
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        {
                            if (!string.IsNullOrEmpty(version) && (prop.Name == version || version.StartsWith(prop.Name)))
                                return path;
                            return path;
                        }
                    }
                }

                if (doc.RootElement.TryGetProperty("pathToUnityExe", out var exeElem))
                {
                    var exePath = exeElem.GetString();
                    if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                        return exePath;
                }

                if (doc.RootElement.TryGetProperty("unityEditors", out var edList) && edList.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in edList.EnumerateArray())
                    {
                        var p = item.GetString();
                        if (!string.IsNullOrEmpty(p) && File.Exists(p))
                            return p;
                    }
                }
            }
        }
        catch { }

        // 3. Scan drives for Unity installations
        var drives = DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed)
            .Select(d => d.RootDirectory.FullName)
            .ToList();

        if (!string.IsNullOrEmpty(version))
        {
            foreach (var drive in drives)
            {
                var candidate1 = Path.Combine(drive, "Unity", $"Unity {version}", "Editor", "Unity.exe");
                if (File.Exists(candidate1)) return candidate1;

                var candidate2 = Path.Combine(drive, "Unity", version, "Editor", "Unity.exe");
                if (File.Exists(candidate2)) return candidate2;

                var candidate3 = Path.Combine(drive, "Program Files", "Unity", "Hub", "Editor", version, "Editor", "Unity.exe");
                if (File.Exists(candidate3)) return candidate3;
            }
        }

        // 4. Fallback search
        foreach (var drive in drives)
        {
            var unityDir = Path.Combine(drive, "Unity");
            if (Directory.Exists(unityDir))
            {
                try
                {
                    foreach (var sub in Directory.GetDirectories(unityDir))
                    {
                        var exe = Path.Combine(sub, "Editor", "Unity.exe");
                        if (File.Exists(exe)) return exe;
                    }
                }
                catch { }
            }
        }

        return null;
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
        string backupDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Akashic", "Backups");

        try
        {
            var vccSettingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VRChatCreatorCompanion", "settings.json");
            if (File.Exists(vccSettingsPath))
            {
                var json = await File.ReadAllTextAsync(vccSettingsPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("projectBackupPath", out var bpElem))
                {
                    var p = bpElem.GetString();
                    if (!string.IsNullOrEmpty(p))
                        backupDir = p;
                }
            }
        }
        catch { }

        if (!Directory.Exists(backupDir))
            Directory.CreateDirectory(backupDir);

        var safeProjectName = string.Join("_", project.Name.Split(Path.GetInvalidFileNameChars()));
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupZip = Path.Combine(backupDir, $"{safeProjectName}_{timestamp}.zip");

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

                    var files = Directory.GetFiles(dirPath, "*", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        var rel = Path.GetRelativePath(project.Path, file);
                        zip.CreateEntryFromFile(file, rel, System.IO.Compression.CompressionLevel.Fastest);
                    }
                }
            });

            progress?.Report(1.0);
            return (true, $"备份已保存至: {backupZip}", backupZip);
        }
        catch (Exception ex)
        {
            return (false, $"备份失败: {ex.Message}", null);
        }
    }

    [GeneratedRegex(@"m_EditorVersion:\s*(.+)")]
    private static partial Regex UnityVersionRegex();
}
