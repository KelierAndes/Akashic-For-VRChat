using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Akashic.Core.Models;

namespace Akashic.Core.Services;

public class BoothService : IBoothService
{
    private const string BoothHost = "booth.pm";
    private const string AccountHost = "accounts.booth.pm";

    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/124.0.0.0 Safari/537.36 Edg/124.0.0.0";

    private const int MaxRedirectHops = 5;

    private static readonly string[] SkipDirectories =
        ["library", "temp", "obj", "node_modules", ".git", "logs"];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _boothClient;
    private readonly HttpClient _fileClient;
    private readonly string _settingsPath;
    private readonly ISessionVault _vault;
    private Dictionary<string, string> _cookies = new(StringComparer.Ordinal);

    private List<StoredCookie> _entries = [];
    private bool _vaultChecked;

    private const string VaultKey = "booth";

    public BoothService(ISessionVault vault, string? settingsPath = null)
    {
        _vault = vault;

        _boothClient = new HttpClient(new HttpClientHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false
        });
        _fileClient = new HttpClient(new HttpClientHandler { UseCookies = false });

        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Akashic", "settings.json");
    }

    public IReadOnlyDictionary<string, string>? SessionCookies
    {
        get
        {
            RestoreFromVault();
            return _cookies.Count == 0 ? null : _cookies;
        }
    }

    public IReadOnlyList<StoredCookie>? SessionEntries
    {
        get
        {
            RestoreFromVault();
            return _entries.Count == 0 ? null : _entries;
        }
    }

    public void ImportSession(IReadOnlyList<StoredCookie> cookies)
    {
        var fresh = new List<StoredCookie>();

        if (cookies is not null)
        {
            foreach (var cookie in cookies)
                if (!string.IsNullOrWhiteSpace(cookie.Name) && !string.IsNullOrWhiteSpace(cookie.Domain))
                    fresh.Add(cookie);
        }

        if (fresh.Count == 0) return;

        _entries = fresh;
        _cookies = StoredCookies.ToNameValue(fresh);
        _vaultChecked = true;
        _vault.Save(VaultKey, fresh);
    }

    public bool HasSession
    {
        get
        {
            RestoreFromVault();
            return _cookies.Count > 0;
        }
    }

    private void RestoreFromVault()
    {
        if (_entries.Count > 0 || _vaultChecked) return;

        _vaultChecked = true;

        if (_vault.Load(VaultKey) is not { } stored) return;

        _entries = stored.Cookies.ToList();
        _cookies = StoredCookies.ToNameValue(_entries);
    }

    public async Task<bool?> ProbeSessionAsync(CancellationToken ct = default)
    {
        if (!HasSession) return null;

        var url = new Uri($"https://{AccountHost}/library");

        try
        {
            using var response = await SendAsync(
                _boothClient, url, HttpCompletionOption.ResponseHeadersRead, sendCookies: true, ct);

            if (IsRedirect(response, url, out var next))
                return next.AbsolutePath.Contains("sign_in", StringComparison.OrdinalIgnoreCase) ? false : null;

            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public string GetLibraryUrl(int page) =>
        page <= 1
            ? $"https://{AccountHost}/library"
            : $"https://{AccountHost}/library?page={page}";

    public string GetFreeLibraryUrl(int page) =>
        page <= 1
            ? $"https://{AccountHost}/library/free_downloads"
            : $"https://{AccountHost}/library/free_downloads?page={page}";

    public string GetGiftsUrl(int page) =>
        page <= 1
            ? $"https://{AccountHost}/library/gifts"
            : $"https://{AccountHost}/library/gifts?page={page}";

    public string PurchaseExtractorScript => ExtractorScript;

    public string ItemPageExtractorScript => ItemExtractorScript;

    public string FreeLibraryExtractorScript => FreeExtractorScript;

    public BoothLibraryPage ParseItemPage(string extractorResultJson)
    {
        if (string.IsNullOrWhiteSpace(extractorResultJson)) return new BoothLibraryPage();

        try
        {
            var payload = extractorResultJson.Trim();
            if (payload.StartsWith('"'))
                payload = JsonSerializer.Deserialize<string>(payload) ?? string.Empty;

            return JsonSerializer.Deserialize<BoothLibraryPage>(payload, SerializerOptions)
                   ?? new BoothLibraryPage();
        }
        catch (JsonException)
        {
            return new BoothLibraryPage();
        }
    }

    public BoothLibraryPage ParseFreeLibraryPage(string extractorResultJson) => ParseItemPage(extractorResultJson);

    public BoothLibraryCache LoadLibraryCache()
    {
        try
        {
            var path = LibraryCachePath;
            if (File.Exists(path))
                return JsonSerializer.Deserialize<BoothLibraryCache>(File.ReadAllText(path), SerializerOptions)
                       ?? new BoothLibraryCache();
        }
        catch (JsonException)
        {
        }

        return new BoothLibraryCache();
    }

    public async Task SaveLibraryCacheAsync(IReadOnlyList<BoothPurchaseItem> items, CancellationToken ct = default)
    {
        var path = LibraryCachePath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var snapshot = new BoothLibraryCache { SavedAt = DateTime.Now, Items = [.. items] };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(snapshot, SerializerOptions), ct);
    }

    private string LibraryCachePath =>
        Path.Combine(Path.GetDirectoryName(_settingsPath) ?? ".", "cache", "booth-library.json");

    public BoothLibraryPage ParseLibraryPage(string extractorResultJson)
    {
        if (string.IsNullOrWhiteSpace(extractorResultJson)) return new BoothLibraryPage();

        try
        {
            var payload = extractorResultJson.Trim();
            if (payload.StartsWith('"'))
                payload = JsonSerializer.Deserialize<string>(payload) ?? string.Empty;

            return JsonSerializer.Deserialize<BoothLibraryPage>(payload, SerializerOptions)
                   ?? new BoothLibraryPage();
        }
        catch (JsonException)
        {
            return new BoothLibraryPage();
        }
    }

    public string GetDownloadDirectory()
    {
        var configured = LoadSettings().BoothDownloadDirectory;
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Akashic", "Booth")
            : configured;
    }

    public async Task SetDownloadDirectoryAsync(string directory, CancellationToken ct = default)
    {
        var full = Path.GetFullPath(directory.Trim().Trim('"'));
        Directory.CreateDirectory(full);

        var settings = LoadSettings();
        settings.BoothDownloadDirectory = full;
        await SaveSettingsAsync(settings, ct);
    }

    public void ReconcileLocalArchives(IEnumerable<BoothPurchaseItem> items)
    {
        var directory = GetDownloadDirectory();
        if (!Directory.Exists(directory)) return;

        var pool = new List<string>(Directory.EnumerateFiles(directory, "*.zip"));
        if (pool.Count == 0) return;

        foreach (var item in items)
        {
            if (item.IsDownloaded) continue;

            var wanted = Key(item.FileNameWithoutExtension);
            var hit = pool.FindIndex(file =>
                Path.GetFileNameWithoutExtension(file).Equals(item.Number, StringComparison.OrdinalIgnoreCase) ||
                (wanted.Length > 0 && Key(Path.GetFileNameWithoutExtension(file)) == wanted));
            if (hit < 0) continue;

            item.LocalZipPath = pool[hit];
            pool.RemoveAt(hit);
        }
    }

    private static string Key(string raw) => new(raw.Where(char.IsLetterOrDigit).ToArray());

    public int FoldOldVersions(List<BoothPurchaseItem> items, Dictionary<string, int> hiddenByNumber)
    {
        var dropped = new List<BoothPurchaseItem>();
        var hidden = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var group in items.GroupBy(i => i.ProductKey).Where(g => g.Count() > 1))
        {
            var ordered = group.OrderByDescending(i => i,
                Comparer<BoothPurchaseItem>.Create(CompareFreshness)).ToList();
            hidden[ordered[0].Number] = ordered.Count - 1;
            dropped.AddRange(ordered.Skip(1));
        }

        if (dropped.Count == 0) return 0;

        items.RemoveAll(dropped.Contains);
        foreach (var pair in hidden)
            hiddenByNumber[pair.Key] =
                (hiddenByNumber.TryGetValue(pair.Key, out var seen) ? seen : 0) + pair.Value;

        return dropped.Count;
    }

    private static int CompareFreshness(BoothPurchaseItem a, BoothPurchaseItem b)
    {
        var va = ParseVersion(a);
        var vb = ParseVersion(b);

        if (va is not null && vb is not null) return va.CompareTo(vb);
        if (va is not null) return 1;
        if (vb is not null) return -1;

        var na = long.TryParse(a.Number, out var x) ? x : 0;
        var nb = long.TryParse(b.Number, out var y) ? y : 0;
        return na.CompareTo(nb);
    }

    private static Version? ParseVersion(BoothPurchaseItem item)
    {
        foreach (var source in new[] { item.FormLabel, item.DisplayTitle })
        {
            if (string.IsNullOrWhiteSpace(source)) continue;

            var match = System.Text.RegularExpressions.Regex.Match(source,
                @"(?:^|[vV\s_\-\[])(\d{1,3}(?:\.\d{1,3}){1,3})(?:$|[\s_\-\]]|$)");
            if (match.Success && Version.TryParse(match.Groups[1].Value, out var parsed))
                return parsed;
        }

        return null;
    }

    public static string NormalizeKey(string raw) => Key(raw);

    public async Task<string> DownloadAsync(BoothPurchaseItem item, IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (!int.TryParse(item.Number, out _))
            throw new InvalidOperationException($"商品 {item.DisplayTitle} 的编号无法识别，无法下载。");

        var target = await ResolveFileAsync(item.Number, ct);

        var directory = GetDownloadDirectory();
        Directory.CreateDirectory(directory);
        var zipPath = Path.Combine(directory, target.FileName ?? $"{item.Number}.zip");

        using var response = await SendAsync(_fileClient, target.Url, HttpCompletionOption.ResponseHeadersRead,
            sendCookies: false, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1L;
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var file = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[81920];
        long written = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct);
            written += read;
            if (total > 0) progress?.Report(written * 100.0 / total);
        }

        progress?.Report(100);
        item.LocalZipPath = zipPath;
        return zipPath;
    }

    public async Task<BoothArchiveContents> InspectAsync(BoothPurchaseItem item, CancellationToken ct = default)
    {
        var zipPath = item.LocalZipPath ?? throw new InvalidOperationException("该商品还没有下载。");
        if (!File.Exists(zipPath)) throw new FileNotFoundException("找不到下载的压缩包。", zipPath);

        var target = Path.Combine(Path.GetDirectoryName(zipPath) ?? ".",
            Path.GetFileNameWithoutExtension(zipPath) + "-extracted");

        await Task.Run(() => SafeExtract(zipPath, target, ct), ct);

        var contents = new BoothArchiveContents { ZipPath = zipPath, ExtractedPath = target };

        foreach (var file in EnumerateFiles(target, ct))
        {
            var name = Path.GetFileName(file);
            var extension = Path.GetExtension(file);

            if (extension.Equals(".unitypackage", StringComparison.OrdinalIgnoreCase))
            {
                contents.UnityPackageFiles.Add(file);
                continue;
            }

            if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase) &&
                name.Equals("package.json", StringComparison.OrdinalIgnoreCase))
            {
                var meta = ReadPackageJson(file);
                if (meta is not null)
                {
                    contents.UpmPackages.Add(new BoothUpmPackage
                    {
                        Path = Path.GetDirectoryName(file) ?? target,
                        Name = meta.Name!.Trim(),
                        Version = meta.Version?.Trim() ?? string.Empty,
                        DisplayName = string.IsNullOrWhiteSpace(meta.DisplayName) ? meta.Name!.Trim() : meta.DisplayName.Trim()
                    });
                }

                continue;
            }

            if (extension is ".txt" or ".pdf" or ".md" && IsDocument(name))
                contents.Documents.Add(file);
        }

        var packageRoots = contents.UpmPackages.Select(p => Path.GetFullPath(p.Path)).ToList();
        foreach (var assetsRoot in EnumerateDirectories(target, ct)
                     .Where(d => Path.GetFileName(d).Equals("Assets", StringComparison.OrdinalIgnoreCase)))
        {
            var full = Path.GetFullPath(assetsRoot);
            if (packageRoots.Any(p => full.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
            contents.AssetFolders.Add(full);
        }

        item.Contents = contents;
        return contents;
    }

    private async Task<(Uri Url, string? FileName)> ResolveFileAsync(string number, CancellationToken ct)
    {
        var url = new Uri($"https://{BoothHost}/downloadables/{number}");

        for (var hop = 0; hop < MaxRedirectHops; hop++)
        {
            using var response = await SendAsync(_boothClient, url, HttpCompletionOption.ResponseHeadersRead,
                sendCookies: true, ct);

            if (IsRedirect(response, url, out var next))
            {
                if (!next.Host.EndsWith(BoothHost, StringComparison.OrdinalIgnoreCase))
                    return (next, FileNameFromUrl(next));

                url = next;
                continue;
            }

            if (response.IsSuccessStatusCode)
                return (url, FileNameFromUrl(url));

            throw Failures(response.StatusCode, url);
        }

        throw new InvalidOperationException("Booth 的下载跳转层数过多，已停止。请重新登录后再试。");
    }

    private static bool IsRedirect(HttpResponseMessage response, Uri current, out Uri next)
    {
        var location = response.Headers.Location;
        next = location switch
        {
            null => current,
            { IsAbsoluteUri: true } => location,
            _ => new Uri(current, location)
        };

        return location is not null && response.StatusCode is HttpStatusCode.Found or HttpStatusCode.Moved
            or HttpStatusCode.Redirect or HttpStatusCode.RedirectKeepVerb or HttpStatusCode.PermanentRedirect;
    }

    private static Exception Failures(HttpStatusCode code, Uri url) => code switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            new InvalidOperationException(
                "Booth 拒绝了该请求：免费商品可直接下载，付费商品需要先在 Booth 页登录已购账号。"),
        HttpStatusCode.NotFound =>
            new InvalidOperationException($"Booth 找不到该下载地址（{url}），可能商品已下架或版本已变更。"),
        _ => new InvalidOperationException($"Booth 返回了 {(int)code} {url}")
    };

    private async Task<HttpResponseMessage> SendAsync(HttpClient client, Uri url, HttpCompletionOption completion,
        bool sendCookies, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,*/*;q=0.8");
        request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,ja;q=0.8");
        request.Headers.TryAddWithoutValidation("Referer", $"https://{BoothHost}/");
        if (sendCookies && _cookies.Count > 0)
            request.Headers.TryAddWithoutValidation("Cookie", BuildCookieHeader());

        var response = await client.SendAsync(request, completion, ct);

        if (sendCookies) AdoptRotatedCookies(response);

        return response;
    }

    private string BuildCookieHeader()
    {
        var builder = new StringBuilder();
        foreach (var cookie in _cookies)
        {
            if (builder.Length > 0) builder.Append("; ");
            builder.Append(cookie.Key).Append('=').Append(cookie.Value);
        }

        return builder.ToString();
    }

    private void AdoptRotatedCookies(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return;

        foreach (var raw in values)
        {
            var pair = raw.Split(';')[0].Trim();
            var split = pair.IndexOf('=');
            if (split <= 0) continue;

            var name = pair[..split].Trim();
            if (_cookies.TryGetValue(name, out var current))
            {
                var updated = pair[(split + 1)..].Trim();
                if (updated.Length > 0) _cookies[name] = updated;
            }
        }
    }

    private static string? FileNameFromUrl(Uri url)
    {
        var last = url.Segments.Length > 0 ? url.Segments[^1] : string.Empty;
        var name = Path.GetFileName(Uri.UnescapeDataString(last)).Trim();
        if (name.Length == 0 || name is "." or "..") return null;

        var builder = new StringBuilder();
        foreach (var c in name)
            builder.Append(char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_');

        var safe = builder.ToString().Trim('_');
        if (safe.Length == 0) return null;
        if (!safe.Contains('.')) safe += ".zip";

        return safe.Length > 120 ? safe[^120..] : safe;
    }

    private void RequireSession()
    {
        if (!HasSession)
            throw new InvalidOperationException("尚未取得 Booth 登录会话，请先在 Booth 页完成登录。");
    }

    private static void SafeExtract(string zipPath, string targetDirectory, CancellationToken ct)
    {
        Directory.CreateDirectory(targetDirectory);
        var root = Path.GetFullPath(targetDirectory);

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();

            var name = entry.FullName.Replace('\\', '/').TrimStart('/');
            if (name.Length == 0) continue;

            var destination = Path.GetFullPath(Path.Combine(root, name));

            if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                continue;

            var parent = Path.GetDirectoryName(destination);
            if (parent is not null) Directory.CreateDirectory(parent);
            if (name.EndsWith('/')) continue;

            entry.ExtractToFile(destination, overwrite: true);
        }
    }

    private static IEnumerable<string> EnumerateFiles(string root, CancellationToken ct)
    {
        var queue = new Queue<string>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var directory = queue.Dequeue();

            string[] files;
            try { files = Directory.GetFiles(directory); }
            catch { files = []; }

            foreach (var file in files) yield return file;

            foreach (var child in ChildDirectories(directory, ct)) queue.Enqueue(child);
        }
    }

    private static IEnumerable<string> EnumerateDirectories(string root, CancellationToken ct)
    {
        var queue = new Queue<string>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var directory = queue.Dequeue();

            foreach (var child in ChildDirectories(directory, ct))
            {
                yield return child;
                queue.Enqueue(child);
            }
        }
    }

    private static IEnumerable<string> ChildDirectories(string directory, CancellationToken ct)
    {
        IEnumerable<string> children;
        try { children = Directory.EnumerateDirectories(directory); }
        catch { return []; }

        return children.Where(child =>
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(child).ToLowerInvariant();
            return !SkipDirectories.Contains(name) && !name.StartsWith('.');
        }).ToList();
    }

    private static bool IsDocument(string fileName)
    {
        string[] prefixes = ["readme", "license", "licence", "eula", "terms", "guide", "manual", "注意", "説明", "说明"];
        var lower = fileName.ToLowerInvariant();
        return prefixes.Any(lower.StartsWith);
    }

    private static PackageJson? ReadPackageJson(string path)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<PackageJson>(File.ReadAllText(path), SerializerOptions);
            return string.IsNullOrWhiteSpace(parsed?.Name) ? null : parsed;
        }
        catch
        {
            return null;
        }
    }

    private AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath), SerializerOptions)
                       ?? new AppSettings();
        }
        catch {  }

        return new AppSettings();
    }

    private async Task SaveSettingsAsync(AppSettings settings, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var current = LoadSettings();
        current.BoothDownloadDirectory = settings.BoothDownloadDirectory;

        await File.WriteAllTextAsync(_settingsPath, JsonSerializer.Serialize(current, SerializerOptions), ct);
    }

    private const string ExtractorScript = """
        (function () {
          function textOf(node) {
            return node ? (node.textContent || '').replace(/\s+/g, ' ').trim() : '';
          }
          // Booth renders each purchase as a card whose download control is a div, not a link.
          function cardOf(node) {
            var card = node;
            for (var i = 0; i < 8 && card.parentElement; i++) {
              card = card.parentElement;
              if (card.querySelector('a[href*="/items/"]')) break;
            }
            return card;
          }
          // The file name block is the sibling of the download control that holds no button itself.
          function fileOf(button) {
            var node = button;
            for (var i = 0; i < 6 && node.parentElement; i++) {
              node = node.parentElement;
              for (var j = 0; j < node.children.length; j++) {
                var sibling = node.children[j];
                if (sibling.contains(button) || sibling.querySelector('button')) continue;
                var text = textOf(sibling);
                if (text) return text;
              }
            }
            return '';
          }

          var out = [];
          var seen = {};
          function pushFrom(el) {
            var href = el.getAttribute('data-href') || el.getAttribute('href') || '';
            var match = /\/downloadables\/(\d+)/.exec(href);
            if (!match) return;
            var number = match[1];
            if (seen[number]) return;
            seen[number] = true;

            var card = cardOf(el);
            var title = '';
            var itemUrl = '';
            var shopUrl = '';
            Array.prototype.slice.call(card.querySelectorAll('a[href]')).forEach(function (a) {
              var target = a.getAttribute('href') || '';
              if (/\/items\/\d+/.test(target)) {
                itemUrl = a.href;
                var text = textOf(a);
                if (text.length > title.length) title = text;
              } else if (/^https?:\/\/[a-z0-9-]+\.booth\.pm\/?$/i.test(target)) {
                shopUrl = a.href;
              }
            });

            var shop = card.querySelector('.text-text-gray600');
            var avatar = card.querySelector('img[alt]');

            // The card's first <img> is the product thumbnail; the shop avatar only appears
            // without one, so whichever came first is the closest thing to a cover.
            var cover = card.querySelector('img');

            out.push({
              number: number,
              downloadablesUrl: href,
              itemUrl: itemUrl,
              title: title,
              formLabel: fileOf(el),
              shop: textOf(shop) || (avatar ? avatar.getAttribute('alt') : ''),
              shopUrl: shopUrl,
              imageUrl: cover ? (cover.getAttribute('src') || '') : '',
              external: el.getAttribute('data-is-external') === 'true'
            });
          }

          // Paid rows carry data-test="downloadable", but free and gifted items render the same
          // control without that marker — so every download button is collected, then anything
          // else on the page that still points at a downloadables id (plain links) as a fallback.
          Array.prototype.slice.call(document.querySelectorAll('.js-download-button')).forEach(pushFrom);
          Array.prototype.slice.call(
            document.querySelectorAll('a[href*="/downloadables/"], [data-href*="/downloadables/"]')
          ).forEach(pushFrom);

          var lastPage = 1;
          Array.prototype.slice.call(document.querySelectorAll('a[href]')).forEach(function (a) {
            var raw = a.getAttribute('href') || '';
            // Pagination links read /library?page=N on the purchase list and
            // /library/free_downloads?page=N on the free list; both count.
            if (!/\/library(\/|\?)/.test(a.href)) return;
            var pageMatch = /[?&]page=(\d+)/.exec(raw);
            if (!pageMatch) return;
            var n = parseInt(pageMatch[1], 10);
            if (n > lastPage) lastPage = n;
          });

          var loginRequired = /sign[_-]?in|login|auth|id\.booth\.pm/i.test(location.href) ||
            !!document.querySelector('input[type=password]');

          return JSON.stringify({
            url: location.href,
            total: out.length,
            lastPage: lastPage,
            loginRequired: loginRequired,
            items: out
          });
        })()
        """;

    private const string ItemExtractorScript = """
        (function () {
          function meta(name) {
            var m = document.querySelector('meta[property="' + name + '"], meta[name="' + name + '"]');
            return m ? (m.getAttribute('content') || '').trim() : '';
          }

          var out = [];
          var seen = {};
          function pushFrom(el) {
            var href = el.getAttribute('data-href') || el.getAttribute('href') || '';
            var match = /\/downloadables\/(\d+)/.exec(href);
            if (!match) return;
            var number = match[1];
            if (seen[number]) return;
            seen[number] = true;

            var label = (el.textContent || '').replace(/\s+/g, ' ').trim();

            var shopUrl = '';
            Array.prototype.slice.call(document.querySelectorAll('a[href]')).forEach(function (a) {
              if (shopUrl) return;
              var target = a.getAttribute('href') || '';
              if (/^https?:\/\/[a-z0-9-]+\.booth\.pm\/?$/i.test(target)) shopUrl = a.href;
            });

            out.push({
              number: number,
              downloadablesUrl: href,
              itemUrl: location.origin + location.pathname,
              title: meta('og:title') || (document.title || '').replace(/\s*\|\s*BOOTH.*$/i, '').trim(),
              formLabel: label,
              shop: meta('og:site_name'),
              shopUrl: shopUrl,
              imageUrl: meta('og:image'),
              external: false
            });
          }

          Array.prototype.slice.call(document.querySelectorAll('.js-download-button')).forEach(pushFrom);
          Array.prototype.slice.call(
            document.querySelectorAll('a[href*="/downloadables/"], [data-href*="/downloadables/"]')
          ).forEach(pushFrom);

          return JSON.stringify({
            url: location.href,
            lastPage: 1,
            loginRequired: false,
            items: out
          });
        })()
        """;

    private const string FreeExtractorScript = """
        (function () {
          function textOf(node) {
            return node ? (node.textContent || '').replace(/\s+/g, ' ').trim() : '';
          }

          var out = [];
          var seen = {};
          Array.prototype.slice.call(document.querySelectorAll('a[href*="/items/"]')).forEach(function (a) {
            var m = /\/items\/(\d+)/.exec(a.getAttribute('href') || '');
            if (!m) return;
            var id = m[1];
            if (seen[id]) return;
            seen[id] = true;

            var card = a;
            for (var i = 0; i < 8 && card.parentElement; i++) {
              card = card.parentElement;
              if (card.className && /(^|\s)bg-white(\s|$)/.test(String(card.className))) break;
            }

            var title = textOf(a);
            var shopUrl = '';
            var shop = '';
            Array.prototype.slice.call((card || document).querySelectorAll('a[href]')).forEach(function (x) {
              var target = x.getAttribute('href') || '';
              if (/\/items\/\d+/.test(target)) {
                var t = textOf(x);
                if (t.length > title.length) title = t;
              } else if (/^https?:\/\/[a-z0-9-]+\.booth\.pm\/?$/i.test(target)) {
                shopUrl = x.href;
                var av = x.querySelector('img[alt]');
                shop = textOf(x) || (av ? av.getAttribute('alt') : '');
              }
            });

            var img = card ? card.querySelector('img.l-library-item-thumbnail') : null;

            out.push({
              number: id,
              itemUrl: 'https://booth.pm/ja/items/' + id,
              title: title,
              shop: shop,
              shopUrl: shopUrl,
              imageUrl: img ? (img.getAttribute('src') || '') : '',
              formLabel: ''
            });
          });

          var lastPage = 1;
          Array.prototype.slice.call(document.querySelectorAll('a[href]')).forEach(function (a) {
            var raw = a.getAttribute('href') || '';
            if (!/\/library(\/|\?)/.test(a.href)) return;
            var pageMatch = /[?&]page=(\d+)/.exec(raw);
            if (!pageMatch) return;
            var n = parseInt(pageMatch[1], 10);
            if (n > lastPage) lastPage = n;
          });

          return JSON.stringify({
            url: location.href,
            lastPage: lastPage,
            loginRequired: false,
            items: out
          });
        })()
        """;
}
