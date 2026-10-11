using System.Net.Http.Json;
using System.Text.Json;
using Akashic.Core.Models;

namespace Akashic.Core.Services;

/// <summary>
/// Reads the Unity Asset Store from two sides: the .unitypackage files Unity already dropped into
/// a local cache folder, and the signed-in account's 我的资产 list. The session comes from the
/// WebView2 login and is kept in memory only, exactly like Booth's.
/// </summary>
public class AssetStoreService : IAssetStoreService
{
    private const string StoreHost = "assetstore.unity.com";


    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _apiClient;
    private readonly HttpClient _fileClient;
    private readonly IUnityPackageLibraryService _library;
    private readonly string _settingsPath;
    private Dictionary<string, string> _cookies = new(StringComparer.Ordinal);

    // Product covers remembered by name, so cache-only rows and list rows can share them
    // without hitting the storefront again. Filled from 我的资产 reads and search lookups,
    // and persisted to disk so they survive restarts.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _coverCache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string _coverCachePath;

    public AssetStoreService(IUnityPackageLibraryService library, string? settingsPath = null)
    {
        // Private clients: the shared HttpClient in the container also serves the registry
        // services, and a Unity session header must never travel with those requests.
        _apiClient = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false });
        _fileClient = new HttpClient(new HttpClientHandler { UseCookies = false });
        _library = library;

        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Akashic", "settings.json");

        var dataDir = Path.GetDirectoryName(_settingsPath) ?? ".";
        _coverCachePath = Path.Combine(dataDir, "cache", "assetstore-covers.json");
        LoadCoverCache();
    }

    private void LoadCoverCache()
    {
        try
        {
            if (!File.Exists(_coverCachePath)) return;

            var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_coverCachePath), SerializerOptions);
            if (entries is null) return;

            foreach (var (key, url) in entries)
                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(url))
                    _coverCache[key] = url;
        }
        catch (JsonException)
        {
        }
    }

    private void SaveCoverCache()
    {
        try
        {
            var directory = Path.GetDirectoryName(_coverCachePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            File.WriteAllText(_coverCachePath,
                JsonSerializer.Serialize(_coverCache.ToArray(), SerializerOptions));
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Loads the disk snapshot of the last 我的资产 read, if any.</summary>
    public AssetStoreAccountCache LoadAccountCache()
    {
        try
        {
            var path = AccountCachePath;
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AssetStoreAccountCache>(File.ReadAllText(path), SerializerOptions)
                       ?? new AssetStoreAccountCache();
        }
        catch (JsonException)
        {
        }

        return new AssetStoreAccountCache();
    }

    /// <summary>Overwrites the disk snapshot with the given account read.</summary>
    public async Task SaveAccountCacheAsync(IReadOnlyList<AssetStoreAsset> items, CancellationToken ct = default)
    {
        var path = AccountCachePath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var snapshot = new AssetStoreAccountCache { SavedAt = DateTime.Now, Items = [.. items] };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(snapshot, SerializerOptions), ct);
    }

    private string AccountCachePath =>
        Path.Combine(Path.GetDirectoryName(_settingsPath) ?? ".", "cache", "assetstore-account.json");

    public IReadOnlyDictionary<string, string>? SessionCookies
    {
        get => _cookies.Count == 0 ? null : _cookies;
        set
        {
            var fresh = new Dictionary<string, string>(StringComparer.Ordinal);
            if (value is not null)
            {
                foreach (var cookie in value)
                    if (!string.IsNullOrWhiteSpace(cookie.Key))
                        fresh[cookie.Key.Trim()] = cookie.Value?.Trim() ?? string.Empty;
            }

            _cookies = fresh;
        }
    }

    public bool HasSession => _cookies.Count > 0;

    public string MyAssetsUrl => $"https://{StoreHost}/account/assets";

    public string GetSearchUrl(string term) =>
        $"https://{StoreHost}/search?q={Uri.EscapeDataString(term.Trim())}";

    public string GetCacheDirectory() => Configured(
        LoadSettings().AssetStoreCacheDirectory,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Unity", "Asset Store-5.x"));

    public string GetDownloadDirectory() => Configured(
        LoadSettings().AssetStoreDownloadDirectory,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Akashic", "AssetStore"));

    public async Task SetCacheDirectoryAsync(string directory, CancellationToken ct = default)
    {
        var settings = LoadSettings();
        settings.AssetStoreCacheDirectory = Path.GetFullPath(directory.Trim().Trim('"'));
        await SaveSettingsAsync(settings, ct);
    }

    public async Task SetDownloadDirectoryAsync(string directory, CancellationToken ct = default)
    {
        var full = Path.GetFullPath(directory.Trim().Trim('"'));
        Directory.CreateDirectory(full);

        var settings = LoadSettings();
        settings.AssetStoreDownloadDirectory = full;
        await SaveSettingsAsync(settings, ct);
    }

    private static string Configured(string configured, string fallback) =>
        string.IsNullOrWhiteSpace(configured) ? fallback : configured;

    /// <summary>
    /// Walks the cache and the download folder two levels deep, which is how Unity lays the store
    /// out: publisher, then category, then the .unitypackage itself.
    /// </summary>
    public async Task<List<AssetStoreAsset>> ScanCacheAsync(CancellationToken ct = default)
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in await _library.ScanAsync(ct))
            known.Add(file.FilePath);

        var result = new List<AssetStoreAsset>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in new[] { GetCacheDirectory(), GetDownloadDirectory() })
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(root)) continue;

            foreach (var file in EnumeratePackages(root))
            {
                if (!seen.Add(file)) continue;

                UnityPackageFileInfo info;
                try
                {
                    info = _library.DescribePackage(file);
                }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }

                var (publisher, category) = Relate(root, file);

                result.Add(new AssetStoreAsset
                {
                    Name = string.IsNullOrWhiteSpace(info.DisplayName) ? info.PackageId : info.DisplayName,
                    Publisher = publisher,
                    Category = category,
                    Version = info.Version,
                    LocalPath = file,
                    Size = info.Size,
                    LastModified = info.LastModified,
                    IsInLibrary = known.Contains(file)
                });
            }
        }

        return result
            .OrderBy(a => a.Publisher, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> EnumeratePackages(string root)
    {
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));

        while (pending.Count > 0)
        {
            var (current, depth) = pending.Pop();

            // An iterator cannot yield inside a try with a catch, so each directory is read into
            // a local list first and handed out after the guarded block.
            var files = new List<string>();
            var children = new List<(string Path, int Depth)>();

            try
            {
                files.AddRange(Directory.GetFiles(current, "*.unitypackage"));

                if (depth < 3)
                {
                    foreach (var sub in Directory.GetDirectories(current))
                        children.Add((sub, depth + 1));
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            foreach (var file in files) yield return file;
            foreach (var child in children) pending.Push(child);
        }
    }

    private static (string Publisher, string Category) Relate(string root, string file)
    {
        var relative = Path.GetRelativePath(root, file);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // parts = [publisher, category, name.unitypackage] for the store's own layout; anything
        // shallower is a file the user dropped in themselves, so it has no publisher.
        var publisher = parts.Length >= 3 ? parts[0] : string.Empty;
        var category = parts.Length >= 3 ? parts[1] : string.Empty;

        return (publisher, category);
    }

    /// <summary>
    /// Attaches a downloaded file to the account row that owns it. Names are matched loosely because
    /// the cache carries only the file name, while the store reports a display name.
    /// </summary>
    public void MergeCachedFiles(IEnumerable<AssetStoreAsset> accountItems, IEnumerable<AssetStoreAsset> cached)
    {
        var byKey = new Dictionary<string, AssetStoreAsset>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in cached)
        {
            var key = MatchKey(item.Name);
            if (key.Length > 0) byKey.TryAdd(key, item);
        }

        foreach (var asset in accountItems)
        {
            if (asset.IsCached) continue;
            if (!byKey.TryGetValue(MatchKey(asset.Name), out var hit)) continue;

            asset.LocalPath = hit.LocalPath;
            asset.Size = hit.Size;
            asset.LastModified = hit.LastModified;
            asset.IsInLibrary = hit.IsInLibrary;
            if (string.IsNullOrWhiteSpace(asset.Category)) asset.Category = hit.Category;

            // The account row's cover also answers for the cache file it now owns.
            if (!string.IsNullOrWhiteSpace(asset.ImageUrl)) CacheCoverUrl(asset.Name, asset.ImageUrl);
        }
    }

    public void CacheCoverUrl(string name, string url)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url)) return;
        _coverCache[MatchKey(name)] = url;
        SaveCoverCache();
    }

    public bool TryGetCoverUrl(string name, out string url)
    {
        url = string.IsNullOrWhiteSpace(name) || !_coverCache.TryGetValue(MatchKey(name), out var hit)
            ? string.Empty
            : hit;
        return url.Length > 0;
    }

    public string MatchKey(string raw) =>
        string.Concat((raw ?? string.Empty).Where(char.IsLetterOrDigit)).ToLowerInvariant();

    private string CookieHeader() =>
        string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}"));

    public AssetStorePurchasePage ParseMyAssets(string extractorResultJson)
    {
        if (string.IsNullOrWhiteSpace(extractorResultJson)) return new AssetStorePurchasePage();

        try
        {
            // WebView2 hands script results back JSON-encoded, so a script that returns a string
            // arrives double-wrapped. The page reader may also hand back an array, one entry per
            // rendered 我的资源 page.
            var payload = extractorResultJson.Trim();
            if (payload.StartsWith('"'))
                payload = JsonSerializer.Deserialize<string>(payload) ?? string.Empty;

            var pages = payload.StartsWith('[')
                ? JsonSerializer.Deserialize<List<ScrapedAssets>>(payload, SerializerOptions) ?? []
                : [JsonSerializer.Deserialize<ScrapedAssets>(payload, SerializerOptions) ?? new ScrapedAssets()];

            var result = new AssetStorePurchasePage
            {
                TotalPages = Math.Max(1, pages.Count),
                ReadDiagnostics = string.Join(" | ", pages.Select(p => p.Probe).Where(p => !string.IsNullOrWhiteSpace(p)))
            };

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var scraped in pages)
            {
                result.LoginRequired |= scraped.LoginRequired;

                foreach (var item in scraped.Items)
                {
                    if (string.IsNullOrWhiteSpace(item.Name)) continue;
                    if (!seen.Add(string.IsNullOrWhiteSpace(item.ProductId) ? item.Name : item.ProductId)) continue;

                    var image = item.Image?.Trim() ?? string.Empty;
                    if (image.Length > 0)
                        CacheCoverUrl(item.Name, image.StartsWith("//") ? "https:" + image : image);

                    result.Items.Add(new AssetStoreAsset
                    {
                        Name = item.Name.Trim(),
                        Publisher = item.Publisher?.Trim() ?? string.Empty,
                        Version = item.Version?.Trim() ?? string.Empty,
                        ProductId = string.IsNullOrWhiteSpace(item.ProductId) ? null : item.ProductId.Trim(),
                        ImageUrl = image.StartsWith("//") ? "https:" + image : image,
                        IsFromAccount = true
                    });
                }
            }

            result.Total = result.Items.Count;
            return result;
        }
        catch (JsonException)
        {
            return new AssetStorePurchasePage();
        }
    }

    /// <summary>
    /// Reads one page of 我的资源 the way the storefront actually renders it: every row is a
    /// [data-test="asset-item-&lt;id&gt;"] div whose product name is plain text, not a link — the
    /// only /packages/ anchor on that page belongs to a footer banner. The script stays
    /// synchronous because WebView2's ExecuteScriptAsync does not wait for promises; walking the
    /// 下一页 pagination is done from C#. Multiple page results arrive as a JSON array.
    /// </summary>
    public string MyAssetsExtractorScript => """
        (() => {
          try {
            const rowSelector = '[data-test^="asset-item-"]';
            // The cover can sit in an <img> (sometimes lazy-loaded via data-src) or be painted
            // as a CSS background-image on a child div, so both shapes are checked.
            const coverOf = (row) => {
              const absolutize = (u) => u.startsWith('//') ? location.protocol + u : u;
              const img = row.querySelector('img');
              if (img) {
                const s = img.currentSrc || img.getAttribute('src') || img.getAttribute('data-src') || '';
                if (s) return absolutize(s);
              }
              const nodes = row.querySelectorAll('*');
              for (let i = 0; i < nodes.length && i < 40; i++) {
                const bg = nodes[i].style && nodes[i].style.backgroundImage;
                const m = bg && /url\(["']?([^"')]+)/.exec(bg);
                if (m) return absolutize(m[1]);
              }
              return '';
            };
            const items = [];
            const seen = new Set();
            document.querySelectorAll(rowSelector).forEach((row) => {
              const m = /asset-item-([0-9]+)/.exec(row.getAttribute('data-test') || '');
              if (!m) return;
              const name = (row.querySelector('[data-test="package-name"]')?.textContent || '').trim();
              if (!name || seen.has(m[1])) return;
              seen.add(m[1]);
              const publisher = (row.querySelector('a[href^="/publishers/"]')?.textContent || '').trim();
              const meta = row.querySelector('._4ROtP')?.textContent || '';
              const v = /版本[:：]\s*([0-9][0-9A-Za-z.\-+]*)/.exec(meta);
              items.push({ productId: m[1], name: name, publisher: publisher, version: v ? v[1] : '',
                           image: coverOf(row) });
            });
            const next = document.querySelector('button[label="下一页"], button[aria-label="下一页"]');
            const signing = /login|passport|sign-in/i.test(location.href) && items.length === 0;
            return JSON.stringify({
              url: location.href,
              loginRequired: signing,
              probe: location.pathname + ' · 行 ' + document.querySelectorAll(rowSelector).length + ' · 抓到 ' + items.length,
              items: items
            });
          } catch (e) {
            return JSON.stringify({ url: String(location.href), loginRequired: true, probe: '页面脚本执行失败', items: [] });
          }
        })()
        """;

    public async Task<string> DownloadAsync(AssetStoreAsset asset, IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (!HasSession) throw new InvalidOperationException("还没有登录 Unity 账号。");
        if (string.IsNullOrWhiteSpace(asset.ProductId))
            throw new InvalidOperationException($"{asset.Name} 缺少商品编号，无法下载。");

        var directory = GetDownloadDirectory();
        Directory.CreateDirectory(directory);

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://{StoreHost}/api/downloads/{asset.ProductId}");
        request.Headers.TryAddWithoutValidation("cookie", CookieHeader());

        using var response = await _apiClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        // A redirect to the sign-in flow is how the store answers an expired session.
        if (response.StatusCode is System.Net.HttpStatusCode.Found or System.Net.HttpStatusCode.MovedPermanently
            or System.Net.HttpStatusCode.SeeOther or System.Net.HttpStatusCode.Unauthorized
            or System.Net.HttpStatusCode.Forbidden)
            throw new InvalidOperationException("Unity 登录已失效，请重新登录。");

        response.EnsureSuccessStatusCode();

        var fileName = FileNameFrom(response, asset);
        var target = Path.Combine(directory, fileName);

        await using (var file = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        {
            var total = response.Content.Headers.ContentLength ?? -1L;
            await using var source = await response.Content.ReadAsStreamAsync(ct);

            var buffer = new byte[81920];
            long written = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                written += read;
                if (total > 0) progress?.Report(written * 100.0 / total);
            }
        }

        progress?.Report(100);
        asset.LocalPath = target;
        asset.Size = new FileInfo(target).Length;
        asset.LastModified = File.GetLastWriteTime(target);
        return target;
    }

    /// <summary>
    /// The store names the file through Content-Disposition; a purchase without one keeps the
    /// product name plus the extension Unity expects.
    /// </summary>
    private static string FileNameFrom(HttpResponseMessage response, AssetStoreAsset asset)
    {
        var disposition = response.Content.Headers.ContentDisposition;
        var raw = !string.IsNullOrWhiteSpace(disposition?.FileNameStar) ? disposition.FileNameStar
                : !string.IsNullOrWhiteSpace(disposition?.FileName) ? disposition.FileName.Trim('"')
                : null;

        if (!string.IsNullOrWhiteSpace(raw))
        {
            var clean = Path.GetFileName(raw.Trim());
            if (clean.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase)) return clean;
        }

        var safe = string.Join("_", asset.Name.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
        return (string.IsNullOrWhiteSpace(safe) ? asset.ProductId ?? "asset" : safe) + ".unitypackage";
    }

    private sealed class ScrapedAssets
    {
        public string Url { get; set; } = string.Empty;
        public bool LoginRequired { get; set; }
        public string Probe { get; set; } = string.Empty;
        public List<ScrapedAsset> Items { get; set; } = [];
    }

    private sealed class ScrapedAsset
    {
        public string ProductId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Publisher { get; set; }
        public string? Version { get; set; }
        public string? Image { get; set; }
    }

    public string SearchCoverExtractorScript => SearchCoverScript;

    /// <summary>
    /// Reads the rendered search result cards. Covers there are plain &lt;img&gt; key-images and
    /// every product card is an article[data-test=search-results-product-card-&lt;id&gt;].
    /// </summary>
    public string SearchCoverScript => """
        (() => {
          try {
            const out = [];
            document.querySelectorAll('article[data-test^="search-results-product-card-"]').forEach((card) => {
              const img = card.querySelector('img');
              let src = img ? (img.currentSrc || img.getAttribute('src') || '') : '';
              if (src.startsWith('//')) src = location.protocol + src;
              let name = '';
              card.querySelectorAll('a, h2, h3').forEach((n) => {
                const t = (n.textContent || '').replace(/\s+/g, ' ').trim();
                if (t.length > name.length && t.length < 120) name = t;
              });
              if (src && name) out.push({ name: name, image: src });
            });
            return JSON.stringify({ url: location.href, items: out });
          } catch (e) {
            return JSON.stringify({ url: String(location.href), items: [] });
          }
        })()
        """;

    public List<AssetStoreSearchCover> ParseSearchCovers(string extractorResultJson)
    {
        if (string.IsNullOrWhiteSpace(extractorResultJson)) return [];

        try
        {
            var payload = extractorResultJson.Trim();
            if (payload.StartsWith('"'))
                payload = JsonSerializer.Deserialize<string>(payload) ?? string.Empty;

            var parsed = JsonSerializer.Deserialize<SearchCoverPage>(payload, SerializerOptions);
            return parsed?.Items ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed class SearchCoverPage
    {
        public string Url { get; set; } = string.Empty;
        public List<AssetStoreSearchCover> Items { get; set; } = [];
    }

    private AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath), SerializerOptions)
                       ?? new AppSettings();
        }
        catch (JsonException)
        {
        }

        return new AppSettings();
    }

    private async Task SaveSettingsAsync(AppSettings settings, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        // Re-read so edits made by the other services sharing settings.json are not clobbered.
        var current = LoadSettings();
        current.AssetStoreCacheDirectory = settings.AssetStoreCacheDirectory;
        current.AssetStoreDownloadDirectory = settings.AssetStoreDownloadDirectory;

        await File.WriteAllTextAsync(_settingsPath,
            JsonSerializer.Serialize(current, SerializerOptions), ct);
    }
}
