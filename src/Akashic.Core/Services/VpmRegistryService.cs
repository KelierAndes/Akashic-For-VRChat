using System.Net.Http.Json;
using System.Text.Json;
using Akashic.Core.Data;
using Akashic.Core.Models;

namespace Akashic.Core.Services;

public class VpmRegistryService : IVpmRegistryService
{
    private static readonly TimeSpan RegistryTtl = TimeSpan.FromMinutes(30);

    private readonly HttpClient _httpClient;
    private readonly string _settingsPath;
    private readonly string _vccSettingsPath;
    private readonly string _cacheDir;
    private readonly List<string> _registryUrls;
    private readonly Dictionary<string, List<VpmPackage>> _packageCache = [];
    private readonly AkashicDatabase _db;

    private List<VccRepository>? _vccRepos;
    private Dictionary<string, VccRepository>? _packageOwnerIndex;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public VpmRegistryService(HttpClient? httpClient = null, string? settingsPath = null,
        AkashicDatabase? database = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Akashic/1.0");
        }

        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Akashic", "settings.json");
        _db = database ?? new AkashicDatabase(Path.GetDirectoryName(_settingsPath));

        var settings = LoadSettings();
        _vccSettingsPath = settings.VccSettingsPath;
        _registryUrls = settings.RegistryUrls;
        _cacheDir = settings.CacheDirectory;

        if (!Directory.Exists(_cacheDir))
            Directory.CreateDirectory(_cacheDir);
    }

    public async Task<List<VpmPackage>> GetAllPackagesAsync(bool refresh = false, CancellationToken ct = default)
    {
        if (refresh)
        {
            lock (_packageCache) _packageCache.Clear();
        }

        var packageMap = new Dictionary<string, VpmPackage>(StringComparer.OrdinalIgnoreCase);

        foreach (var url in _registryUrls)
        {
            try
            {
                foreach (var package in await ResolveRegistryPackagesAsync(url, ct, refresh))
                    packageMap.TryAdd(package.Name, package);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to fetch from {url}: {ex.Message}");
            }
        }

        return packageMap.Values.ToList();
    }

    public async Task<List<VpmPackage>> GetPackagesFromRegistryAsync(string registryUrl, CancellationToken ct = default)
        => await ResolveRegistryPackagesAsync(registryUrl, ct, forceRefresh: true);

    private async Task<List<VpmPackage>> ResolveRegistryPackagesAsync(string registryUrl, CancellationToken ct,
        bool forceRefresh = false)
    {
        if (!forceRefresh && _packageCache.TryGetValue(registryUrl, out var cached))
            return cached;

        var fetchedAt = _db.GetVpmFetchedAt(registryUrl);
        if (!forceRefresh && fetchedAt is not null && DateTime.Now - fetchedAt < RegistryTtl)
        {
            var stored = _db.LoadVpmPackages(registryUrl);
            if (stored.Count > 0)
            {
                _packageCache[registryUrl] = stored;
                return stored;
            }
        }

        List<VpmPackage>? fallback = null;

        try
        {
            var packages = await TryReadVccCacheAsync(registryUrl, ct)
                           ?? await FetchRegistryPackagesAsync(registryUrl, ct);
            var sourceName = packages.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.SourceRegistry))?.SourceRegistry
                             ?? FindVccRepository(registryUrl)?.Name;
            _db.ReplaceVpmPackages(registryUrl, sourceName, packages);
            _packageCache[registryUrl] = packages;
            return packages;
        }
        catch
        {
            fallback = _db.LoadVpmPackages(registryUrl);
            if (fallback.Count > 0)
            {
                _packageCache[registryUrl] = fallback;
                return fallback;
            }

            throw;
        }
    }

    private async Task<List<VpmPackage>?> TryReadVccCacheAsync(string registryUrl, CancellationToken ct)
    {
        var repo = FindVccRepository(registryUrl);
        if (repo == null || !File.Exists(repo.LocalPath)) return null;

        try
        {
            var json = await File.ReadAllTextAsync(repo.LocalPath, ct);
            return ParseRegistryListingJson(json, repo.Name, registryUrl);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to read cached listing {repo.LocalPath}: {ex.Message}");
            return null;
        }
    }

    private async Task<List<VpmPackage>> FetchRegistryPackagesAsync(string registryUrl, CancellationToken ct)
    {
        var response = await _httpClient.GetAsync(registryUrl, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        return ParseRegistryListingJson(json, DeriveRepositoryName(registryUrl), registryUrl);
    }

    private static List<VpmPackage> ParseRegistryListingJson(string json, string fallbackName, string registryUrl)
    {
        var packages = new List<VpmPackage>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            JsonElement pkgsElem = default;

            string repoName = fallbackName;
            if (doc.RootElement.TryGetProperty("repo", out var repoObj))
            {
                if (repoObj.TryGetProperty("name", out var n) && !string.IsNullOrWhiteSpace(n.GetString()))
                    repoName = n.GetString()!.Trim();
                if (repoObj.TryGetProperty("packages", out var p))
                    pkgsElem = p;
            }
            else if (doc.RootElement.TryGetProperty("packages", out var p2))
            {
                if (doc.RootElement.TryGetProperty("name", out var n2) && !string.IsNullOrWhiteSpace(n2.GetString()))
                    repoName = n2.GetString()!.Trim();
                pkgsElem = p2;
            }

            if (pkgsElem.ValueKind != JsonValueKind.Object)
                return packages;

            foreach (var pkgProp in pkgsElem.EnumerateObject())
            {
                if (!pkgProp.Value.TryGetProperty("versions", out var versionsElem) ||
                    versionsElem.ValueKind != JsonValueKind.Object)
                    continue;

                var versionsDict = new Dictionary<string, VpmPackage>();
                foreach (var verProp in versionsElem.EnumerateObject())
                {
                    try
                    {
                        var pkg = JsonSerializer.Deserialize<VpmPackage>(verProp.Value.GetRawText(), JsonOptions);
                        if (pkg != null)
                        {
                            pkg.SourceRegistry = repoName;
                            pkg.SourceRegistryUrl = registryUrl;
                            versionsDict[verProp.Name] = pkg;
                        }
                    }
                    catch { }
                }

                if (versionsDict.Count == 0) continue;

                var latest = versionsDict
                    .OrderByDescending(v => ParseVersion(v.Key))
                    .First().Value;

                latest.Versions = versionsDict;
                latest.SourceRegistry = repoName;
                latest.SourceRegistryUrl = registryUrl;
                packages.Add(latest);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to parse repo json ({registryUrl}): {ex.Message}");
        }

        return packages;
    }

    public async Task<VpmPackage?> GetPackageAsync(string packageName, CancellationToken ct = default)
    {
        var allPackages = await GetAllPackagesAsync(ct: ct);
        return allPackages.Find(p => p.Name == packageName);
    }

    public async Task<List<VpmPackage>> SearchPackagesAsync(string query, CancellationToken ct = default)
    {
        var allPackages = await GetAllPackagesAsync(ct: ct);
        if (string.IsNullOrWhiteSpace(query))
            return allPackages;

        var q = query.ToLowerInvariant();
        return allPackages.Where(p =>
            p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            p.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            (p.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (p.Keywords?.Any(k => k.Contains(q, StringComparison.OrdinalIgnoreCase)) ?? false)
        ).ToList();
    }

    public async Task<string> DownloadPackageAsync(VpmPackage package, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(package.Url))
            throw new InvalidOperationException($"Package {package.Name} has no download URL.");

        var fileName = $"{package.Name}-{package.Version}.zip";
        var cachePath = Path.Combine(_cacheDir, fileName);

        if (File.Exists(cachePath))
        {
            progress?.Report(1.0);
            return cachePath;
        }

        using var response = await _httpClient.GetAsync(package.Url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        var downloadedBytes = 0L;

        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(cachePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[8192];
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            downloadedBytes += bytesRead;

            if (totalBytes > 0)
                progress?.Report((double)downloadedBytes / totalBytes);
        }

        progress?.Report(1.0);
        return cachePath;
    }

    public List<string> GetRegistryUrls() => [.. _registryUrls];

    public async Task AddRegistryUrlAsync(string url)
    {
        if (!_registryUrls.Contains(url))
        {
            _registryUrls.Add(url);
            await SaveRegistryUrlsAsync();
        }
    }

    public async Task RemoveRegistryUrlAsync(string url)
    {
        _registryUrls.Remove(url);
        await SaveRegistryUrlsAsync();
    }

    public async Task SaveRegistryOrderAsync(IEnumerable<string> orderedUrls, CancellationToken ct = default)
    {
        var ordered = orderedUrls
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var missing in _registryUrls.Where(u => !ordered.Contains(u, StringComparer.OrdinalIgnoreCase)))
            ordered.Add(missing);

        if (ordered.SequenceEqual(_registryUrls)) return;

        _registryUrls.Clear();
        _registryUrls.AddRange(ordered);
        await SaveRegistryUrlsAsync();
    }

    public async Task<string?> FetchRepositoryNameAsync(string registryUrl, CancellationToken ct = default)
    {
        var known = FindVccRepository(registryUrl);
        if (known != null && !string.IsNullOrWhiteSpace(known.Name)) return known.Name;

        try
        {
            var response = await _httpClient.GetAsync(registryUrl, ct);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("repo", out var repo) &&
                repo.TryGetProperty("name", out var repoName))
                return Trim(repoName.GetString());

            if (doc.RootElement.TryGetProperty("name", out var rootName))
                return Trim(rootName.GetString());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to read repo name ({registryUrl}): {ex.Message}");
        }

        return null;
    }

    public IReadOnlyList<VccRepository> GetKnownRepositories() => GetVccRepositories();

    public async Task<int> ImportVccRepositoriesAsync()
    {
        var added = 0;

        foreach (var repo in GetVccRepositories().OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(repo.Url) || IsAlreadyConfigured(repo.Url)) continue;

            _registryUrls.Add(repo.Url.Trim());
            added++;
        }

        if (added > 0) await SaveRegistryUrlsAsync();
        return added;
    }

    public VccRepository? FindRepositoryFor(string packageName)
    {
        EnsurePackageOwnerIndex();
        return _packageOwnerIndex!.TryGetValue(packageName, out var repo) ? repo : null;
    }

    private void EnsurePackageOwnerIndex()
    {
        if (_packageOwnerIndex != null) return;

        var index = new Dictionary<string, VccRepository>(StringComparer.OrdinalIgnoreCase);

        foreach (var repo in GetVccRepositories())
        {
            if (!File.Exists(repo.LocalPath)) continue;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(repo.LocalPath));
                foreach (var name in EnumeratePackageNames(doc.RootElement))
                    index.TryAdd(name, repo);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to index {repo.LocalPath}: {ex.Message}");
            }
        }

        _packageOwnerIndex = index;
    }

    private static IEnumerable<string> EnumeratePackageNames(JsonElement root)
    {
        var pkgs = root.ValueKind == JsonValueKind.Object &&
                   root.TryGetProperty("repo", out var repo) && repo.TryGetProperty("packages", out var inRepo)
            ? inRepo
            : root.TryGetProperty("packages", out var top) ? top : default;

        if (pkgs.ValueKind != JsonValueKind.Object) return [];
        return pkgs.EnumerateObject().Select(p => p.Name).ToList();
    }

    private VccRepository? FindVccRepository(string registryUrl) =>
        GetVccRepositories().FirstOrDefault(r => IsSameUrl(r.Url, registryUrl));

    private bool IsAlreadyConfigured(string registryUrl) =>
        _registryUrls.Any(url => IsSameUrl(url, registryUrl));

    private static bool IsSameUrl(string a, string b) =>
        string.Equals(NormalizeUrl(a), NormalizeUrl(b), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeUrl(string url)
    {
        var trimmed = url.Trim().TrimEnd('/');
        var query = trimmed.IndexOf('?');
        return query > 0 ? trimmed[..query] : trimmed;
    }

    private List<VccRepository> GetVccRepositories()
    {
        if (_vccRepos != null) return _vccRepos;

        var repos = new Dictionary<string, VccRepository>(StringComparer.OrdinalIgnoreCase);

        var reposDir = Path.Combine(Path.GetDirectoryName(_vccSettingsPath) ?? string.Empty, "Repos");
        if (Directory.Exists(reposDir))
        {
            foreach (var file in Directory.EnumerateFiles(reposDir, "*.json"))
            {
                var repo = ReadRepositoryHeader(file);
                if (repo == null) continue;

                var key = repo.Id.Length > 0 ? repo.Id : repo.Url;
                if (repos.TryGetValue(key, out var known))
                {
                    if (File.GetLastWriteTimeUtc(file) > File.GetLastWriteTimeUtc(known.LocalPath))
                        repos[key] = repo;
                    continue;
                }

                repos[key] = repo;
            }
        }

        foreach (var user in ReadVccUserRepos())
        {
            var key = user.Id.Length > 0 ? user.Id : user.Url;
            if (repos.TryGetValue(key, out var known))
            {
                if (user.Name.Length > 0) known.Name = user.Name;
                if (File.Exists(user.LocalPath)) known.LocalPath = user.LocalPath;
                continue;
            }

            repos[key] = user;
        }

        _vccRepos = [.. repos.Values];
        return _vccRepos;
    }

    private static VccRepository? ReadRepositoryHeader(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            var source = root.TryGetProperty("repo", out var repo) && repo.ValueKind == JsonValueKind.Object
                ? repo
                : root;

            if (!source.TryGetProperty("packages", out var pkgs) || pkgs.ValueKind != JsonValueKind.Object)
                return null;

            var url = ReadString(source, "url");
            var name = ReadString(source, "name");
            if (url.Length == 0 && name.Length == 0) return null;

            return new VccRepository
            {
                Id = ReadString(source, "id"),
                Name = name.Length > 0 ? name : DeriveRepositoryName(url),
                Url = url,
                LocalPath = path
            };
        }
        catch
        {
            return null;
        }
    }

    private List<VccRepository> ReadVccUserRepos()
    {
        var repos = new List<VccRepository>();

        try
        {
            if (!File.Exists(_vccSettingsPath)) return repos;

            using var doc = JsonDocument.Parse(File.ReadAllText(_vccSettingsPath));
            if (!doc.RootElement.TryGetProperty("userRepos", out var userRepos) ||
                userRepos.ValueKind != JsonValueKind.Array)
                return repos;

            foreach (var entry in userRepos.EnumerateArray())
            {
                var url = ReadString(entry, "url");
                if (url.Length == 0) continue;

                repos.Add(new VccRepository
                {
                    Id = ReadString(entry, "id"),
                    Name = ReadString(entry, "name"),
                    Url = url,
                    LocalPath = ReadString(entry, "localPath")
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to read VCC settings ({_vccSettingsPath}): {ex.Message}");
        }

        return repos;
    }

    private static string ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    private static string DeriveRepositoryName(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;

        for (var i = uri.Segments.Length - 1; i >= 0; i--)
        {
            var candidate = Uri.UnescapeDataString(uri.Segments[i].Trim('/'));
            if (candidate.Length > 0 && !candidate.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return uri.Host;
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Version ParseVersion(string version)
    {
        var parts = version.Split('-')[0];
        return Version.TryParse(parts, out var v) ? v : new Version(0, 0, 0);
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
        catch { }
        return new AppSettings();
    }

    private async Task SaveRegistryUrlsAsync()
    {
        var settings = LoadSettings();
        settings.RegistryUrls = [.. _registryUrls];

        var dir = Path.GetDirectoryName(_settingsPath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await File.WriteAllTextAsync(_settingsPath, json);
    }
}
