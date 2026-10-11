using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json;
using Akashic.Core.Models;

namespace Akashic.Core.Services;

/// <summary>
/// Keeps the 外部包 library in settings.json and installs its entries into Unity projects:
/// local folders are referenced with a file: path, GitHub repositories are downloaded as
/// zipballs and embedded under Packages/.
/// </summary>
public class ExternalPackageService : IExternalPackageService
{
    private static readonly string[] GitHubPackageDirs = ["", "Package", "Packages", "upm", "src", "Unity"];

    private readonly HttpClient _httpClient;
    private readonly IPackageInstaller _installer;
    private readonly string _settingsPath;
    private readonly string _cacheDir;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ExternalPackageService(IPackageInstaller installer, HttpClient? httpClient = null, string? settingsPath = null)
    {
        _installer = installer;
        _httpClient = httpClient ?? new HttpClient();
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Akashic/1.0");

        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Akashic", "settings.json");

        _cacheDir = Path.Combine(Path.GetDirectoryName(_settingsPath) ?? ".", "Cache", "github");
    }

    public IReadOnlyList<ExternalPackage> GetAll() => LoadSettings().ExternalPackages;

    public ExternalPackage? FindByPackageName(string name) => LoadSettings().ExternalPackages
        .FirstOrDefault(p => !string.IsNullOrEmpty(p.Name) &&
                             string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public async Task<ExternalPackage> AddLocalFolderAsync(string folderPath, PackageOrigin origin = PackageOrigin.Local,
        CancellationToken ct = default)
    {
        var full = Path.GetFullPath(folderPath.Trim().Trim('"'));
        if (!Directory.Exists(full))
            throw new InvalidOperationException($"目录不存在: {full}");

        var meta = ReadPackageJson(Path.Combine(full, "package.json"))
                   ?? throw new InvalidOperationException("所选目录根下没有 package.json，请选中包含 package.json 的包文件夹");

        if (string.IsNullOrWhiteSpace(meta.Name))
            throw new InvalidOperationException("package.json 缺少 name 字段，Unity 无法识别该包");

        var settings = LoadSettings();
        var id = BuildId(ExternalPackageKind.LocalFolder, full, null);
        var existing = settings.ExternalPackages.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            // Re-adding a folder through Booth or Asset Store labels it, but never clears a label.
            if (existing.Origin == PackageOrigin.Local && origin != PackageOrigin.Local)
            {
                existing.Origin = origin;
                await SaveSettingsAsync(settings, ct);
            }

            return existing;
        }

        RequireUniqueName(settings, meta.Name!, id);

        var package = new ExternalPackage
        {
            Id = id,
            Name = meta.Name!.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(meta.DisplayName) ? meta.Name!.Trim() : meta.DisplayName.Trim(),
            Version = meta.Version?.Trim() ?? string.Empty,
            Kind = ExternalPackageKind.LocalFolder,
            Location = full,
            Origin = origin,
            AddedAt = DateTime.Now
        };

        settings.ExternalPackages.Add(package);
        await SaveSettingsAsync(settings, ct);
        return package;
    }

    public async Task<ExternalPackage> AddRepositoryAsync(string url, string? reference, CancellationToken ct = default)
    {
        var repo = ParseGitRepo(url)
                   ?? throw new InvalidOperationException(
                       "无法解析仓库地址，应形如 https://github.com|gitlab.com|gitee.com/所有者/仓库");

        var location = $"https://{repo.Host}/{repo.Owner}/{repo.Repository}";
        var settings = LoadSettings();
        var id = BuildId(repo.Kind, location, repo.Reference);
        var existing = settings.ExternalPackages.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;

        // A branch name is only needed for the package.json probe; archive downloads accept HEAD.
        var refName = reference?.Trim();
        if (string.IsNullOrEmpty(refName)) refName = await FetchDefaultBranchAsync(repo, ct);

        PackageJson? meta = null;
        if (!string.IsNullOrEmpty(refName))
            meta = await ProbePackageJsonAsync(repo, refName, ct);

        if (!string.IsNullOrWhiteSpace(meta?.Name))
            RequireUniqueName(settings, meta!.Name!, id);

        var package = new ExternalPackage
        {
            Id = id,
            Name = meta?.Name?.Trim() ?? string.Empty,
            DisplayName = !string.IsNullOrWhiteSpace(meta?.DisplayName) ? meta!.DisplayName!.Trim()
                        : !string.IsNullOrWhiteSpace(meta?.Name) ? meta.Name.Trim()
                        : repo.Repository,
            Version = !string.IsNullOrWhiteSpace(meta?.Version) ? meta!.Version!.Trim() : refName ?? "默认分支",
            Kind = repo.Kind,
            Location = location,
            Reference = refName ?? "HEAD",
            AddedAt = DateTime.Now
        };

        settings.ExternalPackages.Add(package);
        await SaveSettingsAsync(settings, ct);
        return package;
    }

    public async Task<bool> RemoveAsync(string id, CancellationToken ct = default)
    {
        var settings = LoadSettings();
        var removed = settings.ExternalPackages.RemoveAll(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (removed == 0) return false;

        await SaveSettingsAsync(settings, ct);
        return true;
    }

    public async Task InstallAsync(UnityProject project, ExternalPackage package,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (package.Kind == ExternalPackageKind.LocalFolder)
        {
            if (!Directory.Exists(package.Location))
                throw new InvalidOperationException($"包目录已不存在: {package.Location}");

            if (string.IsNullOrWhiteSpace(package.Name))
                throw new InvalidOperationException("该外部包缺少包名，无法安装");

            progress?.Report(0.5);
            var relative = Path.GetRelativePath(project.PackagesPath, package.Location).Replace('\\', '/');
            await _installer.AddUpmPackageAsync(project, package.Name, $"file:{relative}", ct);
            progress?.Report(1.0);
            return;
        }

        var repo = ParseGitRepo(package.Location)
                   ?? throw new InvalidOperationException($"无法解析仓库地址: {package.Location}");
        var refName = string.IsNullOrWhiteSpace(package.Reference) ? "HEAD" : package.Reference;

        progress?.Report(0.1);
        var staging = Path.Combine(_cacheDir, "tmp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);

        try
        {
            var zipPath = Path.Combine(_cacheDir, $"{Sanitize(repo.Owner)}.{Sanitize(repo.Repository)}.{Sanitize(refName)}.zip");
            await DownloadZipAsync(ArchiveUrl(repo, refName), zipPath, ct);

            progress?.Report(0.45);
            var extractDir = Path.Combine(staging, "content");
            ZipFile.ExtractToDirectory(zipPath, extractDir);

            var source = FindPackageRoot(extractDir)
                         ?? throw new InvalidOperationException("仓库里没有 package.json，它不是一个 UPM 包（.unitypackage 请到 导入页 添加）");

            var meta = ReadPackageJson(Path.Combine(source, "package.json"))
                       ?? throw new InvalidOperationException("package.json 无法解析");

            if (string.IsNullOrWhiteSpace(meta.Name))
                throw new InvalidOperationException("package.json 缺少 name 字段");

            var name = meta.Name!.Trim();
            progress?.Report(0.7);

            var target = Path.Combine(project.PackagesPath, name);
            if (Directory.Exists(target)) Directory.Delete(target, true);
            CopyDirectory(source, target);

            await _installer.AddUpmPackageAsync(project, name, $"file:{name}", ct);

            await AdoptResolvedNameAsync(package, name, meta, ct);
            progress?.Report(1.0);
        }
        finally
        {
            try { Directory.Delete(staging, true); } catch (IOException) { }
        }
    }

    public async Task RemoveFromProjectAsync(UnityProject project, ExternalPackage package, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(package.Name))
            throw new InvalidOperationException("该外部包尚未解析出包名，请在项目内卸载");

        await _installer.RemoveUpmPackageAsync(project, package.Name, ct);

        // Git installs embed a copy under Packages/, local folders are only referenced.
        if (package.Kind != ExternalPackageKind.LocalFolder)
        {
            var dir = Path.Combine(project.PackagesPath, package.Name);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    /// <summary>Stores the package.json values discovered during the first GitHub install.</summary>
    private async Task AdoptResolvedNameAsync(ExternalPackage package, string name, PackageJson meta, CancellationToken ct)
    {
        var settings = LoadSettings();
        var stored = settings.ExternalPackages.FirstOrDefault(p => p.Id.Equals(package.Id, StringComparison.OrdinalIgnoreCase));
        if (stored == null) return;

        var changed = false;
        if (string.IsNullOrEmpty(stored.Name) || !string.Equals(stored.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            stored.Name = name;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(meta.DisplayName))
        {
            stored.DisplayName = meta.DisplayName.Trim();
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(meta.Version) && stored.Version != meta.Version.Trim())
        {
            stored.Version = meta.Version.Trim();
            changed = true;
        }

        if (changed) await SaveSettingsAsync(settings, ct);
    }

    private async Task DownloadZipAsync(string url, string target, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var temp = target + ".part";
        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        await using (var file = File.Create(temp))
            await source.CopyToAsync(file, ct);

        if (File.Exists(target)) File.Delete(target);
        File.Move(temp, target);
    }

    private async Task<string?> FetchDefaultBranchAsync(GitRepo repo, CancellationToken ct)
    {
        var url = repo.Host switch
        {
            "github.com" => $"https://api.github.com/repos/{repo.Owner}/{repo.Repository}",
            "gitlab.com" => $"https://gitlab.com/api/v4/projects/{Uri.EscapeDataString($"{repo.Owner}/{repo.Repository}")}",
            "gitee.com" => $"https://gitee.com/api/v5/repos/{repo.Owner}/{repo.Repository}",
            _ => null
        };
        if (url is null) return null;

        try
        {
            using var doc = await _httpClient.GetFromJsonAsync<JsonDocument>(url, ct);

            if (doc?.RootElement.TryGetProperty("default_branch", out var branch) == true &&
                branch.ValueKind == JsonValueKind.String)
                return branch.GetString()?.Trim();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Default branch lookup failed for {repo.Owner}/{repo.Repository}: {ex.Message}");
        }

        return null;
    }

    private async Task<PackageJson?> ProbePackageJsonAsync(GitRepo repo, string reference,
        CancellationToken ct)
    {
        // Assets/<repo> covers the layout git-hosted UPM packages usually ship with.
        foreach (var dir in GitHubPackageDirs.Append($"Assets/{repo.Repository}"))
        {
            var path = string.IsNullOrEmpty(dir) ? "package.json" : $"{dir}/package.json";
            try
            {
                using var response = await _httpClient.GetAsync(RawUrl(repo, reference, path), ct);
                if (!response.IsSuccessStatusCode) continue;

                var json = await response.Content.ReadAsStringAsync(ct);
                var meta = JsonSerializer.Deserialize<PackageJson>(json, JsonOptions);
                if (meta != null && !string.IsNullOrWhiteSpace(meta.Name)) return meta;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"package.json probe failed for {path}: {ex.Message}");
            }
        }

        return null;
    }

    /// <summary>The raw-file URL each host serves package.json from.</summary>
    private static string RawUrl(GitRepo repo, string reference, string path) => repo.Host switch
    {
        "github.com" => $"https://raw.githubusercontent.com/{repo.Owner}/{repo.Repository}/{Uri.EscapeDataString(reference)}/{path}",
        "gitlab.com" => $"https://gitlab.com/{repo.Owner}/{repo.Repository}/-/raw/{Uri.EscapeDataString(reference)}/{path}",
        "gitee.com" => $"https://gitee.com/{repo.Owner}/{repo.Repository}/raw/{Uri.EscapeDataString(reference)}/{path}",
        _ => $"https://{repo.Host}/{repo.Owner}/{repo.Repository}/raw/{Uri.EscapeDataString(reference)}/{path}"
    };

    /// <summary>The archive download each host serves a branch snapshot from.</summary>
    private static string ArchiveUrl(GitRepo repo, string reference) => repo.Host switch
    {
        "github.com" => $"https://codeload.github.com/{repo.Owner}/{repo.Repository}/legacy.zip/{Uri.EscapeDataString(reference)}",
        "gitlab.com" => $"https://gitlab.com/api/v4/projects/{Uri.EscapeDataString($"{repo.Owner}/{repo.Repository}")}/repository/archive.zip?sha={Uri.EscapeDataString(reference)}",
        "gitee.com" => $"https://gitee.com/{repo.Owner}/{repo.Repository}/repository/archive/{Uri.EscapeDataString(reference)}.zip",
        _ => $"https://{repo.Host}/{repo.Owner}/{repo.Repository}/repository/archive/{Uri.EscapeDataString(reference)}.zip"
    };

    private static PackageJson? ReadPackageJson(string path)
    {
        if (!File.Exists(path)) return null;

        try
        {
            return JsonSerializer.Deserialize<PackageJson>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Finds the shallowest folder holding a package.json, following how GitHub repos nest packages.</summary>
    private static string? FindPackageRoot(string root)
    {
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();
            if (File.Exists(Path.Combine(current, "package.json"))) return current;
            if (depth >= 3) continue;

            try
            {
                foreach (var sub in Directory.GetDirectories(current))
                    queue.Enqueue((sub, depth + 1));
            }
            catch (UnauthorizedAccessException) { }
        }

        return null;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);

        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
    }

    private static void RequireUniqueName(AppSettings settings, string name, string id)
    {
        var clash = settings.ExternalPackages.FirstOrDefault(p =>
            !string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        if (clash != null)
            throw new InvalidOperationException($"包 {name} 已在外部包列表中（{clash.Location}），请先移除旧条目");
    }

    private static string BuildId(ExternalPackageKind kind, string location, string? reference)
    {
        var normalized = kind == ExternalPackageKind.LocalFolder
            ? Path.GetFullPath(location).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant()
            : location.TrimEnd('/').ToLowerInvariant();

        var suffix = string.IsNullOrWhiteSpace(reference) ? string.Empty : "@" + reference.Trim().ToLowerInvariant();
        return $"{kind.ToString().ToLowerInvariant()}:{normalized}{suffix}";
    }

    private sealed record GitRepo(ExternalPackageKind Kind, string Host, string Owner, string Repository, string? Reference);

    /// <summary>
    /// Recognises a git repository URL on any of the supported hosts (GitHub, GitLab, Gitee —
    /// plus self-hosted GitLab instances, which answer to the same URL shapes as gitlab.com).
    /// </summary>
    private static GitRepo? ParseGitRepo(string url)
    {
        var raw = url.Trim().Trim('"');
        if (raw.Length == 0) return null;

        // git@host:owner/repo(.git) style
        var scp = raw.Split(':');
        if (raw.Contains('@') && scp.Length == 2 && scp[1].Contains('/'))
        {
            var scpHost = scp[0][(scp[0].IndexOf('@') + 1)..];
            raw = $"https://{scpHost}/{scp[1].TrimStart('/')}";
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme is not ("http" or "https")) return null;

        var host = uri.Host.ToLowerInvariant();
        if (!IsGitHost(host)) return null;

        // Self-hosted GitLab keeps the gitlab.com URL shapes, so it rides the same kind.
        var kind = host == "github.com" ? ExternalPackageKind.GitHub
                 : host == "gitee.com" ? ExternalPackageKind.Gitee
                 : ExternalPackageKind.GitLab;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2) return null;

        var owner = segments[0];
        var repository = segments[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? segments[1][..^4]
            : segments[1];

        string? reference = null;
        // /tree/<ref> (GitHub, Gitee) and /- /tree/<ref> (GitLab) both name a branch or tag.
        var skip = segments.Length > 2 && segments[2] == "-" ? 1 : 0;
        if (segments.Length >= 4 + skip && segments[2 + skip].Equals("tree", StringComparison.OrdinalIgnoreCase))
            reference = Uri.UnescapeDataString(segments[3 + skip]);

        if (owner.Length == 0 || repository.Length == 0) return null;
        return new GitRepo(kind, host, owner, repository, reference);
    }

    /// <summary>Any host ending in one of the known domains, so gitlab.example.com counts too.</summary>
    private static bool IsGitHost(string host) =>
        host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("gitee.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".gitee.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("gitlab.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("gitlab", StringComparison.OrdinalIgnoreCase);

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(c => invalid.Contains(c) || c == '/' ? '_' : c).ToArray());
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
        catch { /* Fall back to defaults */ }

        return new AppSettings();
    }

    private async Task SaveSettingsAsync(AppSettings settings, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        // Re-read so edits made by the other services sharing settings.json are not clobbered.
        var current = LoadSettings();
        current.ExternalPackages = settings.ExternalPackages;

        var json = JsonSerializer.Serialize(current, JsonOptions);
        await File.WriteAllTextAsync(_settingsPath, json, ct);
    }
}
