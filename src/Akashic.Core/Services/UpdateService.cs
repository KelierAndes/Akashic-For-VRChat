using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Akashic.Core.Services;

public sealed class UpdateInfo
{
    public string Current { get; init; } = string.Empty;
    public string Latest { get; init; } = string.Empty;
    public bool HasUpdate { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
    public string PageUrl { get; init; } = string.Empty;
    public string AssetName { get; init; } = string.Empty;
    public string AssetUrl { get; init; } = string.Empty;
    public long AssetBytes { get; init; }
    public DateTime? PublishedAt { get; init; }
    public string Error { get; init; } = string.Empty;
    public string Route { get; set; } = string.Empty;
}

public sealed class UpdateState
{
    public string Phase { get; init; } = "idle";
    public string Message { get; init; } = string.Empty;
    public double Percent { get; init; }
    public string Version { get; init; } = string.Empty;
    public string Route { get; init; } = string.Empty;
    public long Received { get; init; }
    public long Total { get; init; }
}

public interface IUpdateService
{
    Task<UpdateInfo> CheckAsync(bool force = false, CancellationToken ct = default);
    UpdateState GetState();
    Task<UpdateState> DownloadAsync(CancellationToken ct = default);
    bool Apply(int uiPid);
}

public sealed class UpdateService : IUpdateService
{
    private const string Repo = "KelierAndes/Akashic-For-VRChat";

    public const string PublicKey =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEo7XtlI53iNX62gHct82H2+gupeEvhBRY1c3v+gq1Z5ftaAE4As6CWIywYgAR6si7Qe8E1v4CS757t/Q+esAPJA==";

    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FailureCacheFor = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);

    private static readonly (string Label, string Prefix, string ProxyUrl)[] Routes =
    [
        ("直连", "", ""),
        ("镜像 gh-proxy.com", "https://gh-proxy.com/", ""),
        ("镜像 ghfast.top", "https://ghfast.top/", ""),
        ("镜像 ghproxy.net", "https://ghproxy.net/", ""),
        ("镜像 gh.llkk.cc", "https://gh.llkk.cc/", ""),
        ("代理 127.0.0.1:7890", "", "http://127.0.0.1:7890"),
        ("代理 127.0.0.1:7897", "", "http://127.0.0.1:7897"),
        ("代理 127.0.0.1:10809", "", "http://127.0.0.1:10809"),
        ("代理 127.0.0.1:1080", "", "http://127.0.0.1:1080"),
        ("代理 socks5://127.0.0.1:1080", "", "socks5://127.0.0.1:1080"),
        ("代理 socks5://127.0.0.1:10808", "", "socks5://127.0.0.1:10808"),
        ("代理 127.0.0.1:1087", "", "http://127.0.0.1:1087"),
        ("代理 127.0.0.1:8889", "", "http://127.0.0.1:8889"),
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const string ApplyScript = """
        param([int]$UiPid, [int]$HostPid, [string]$Staged, [string]$Install)
        $ErrorActionPreference = 'Stop'
        while (Get-Process -Id $UiPid -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 400 }
        while (Get-Process -Id $HostPid -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 400 }
        Start-Sleep -Seconds 2
        Copy-Item -Path (Join-Path $Staged '*') -Destination $Install -Recurse -Force
        Start-Process -FilePath (Join-Path $Install 'akashic_ui.exe') -WorkingDirectory $Install
        """;

    private readonly string _updatesDir;
    private readonly object _gate = new();

    private HttpClient? _http;
    private string _route = string.Empty;
    private string _prefix = string.Empty;

    private UpdateInfo? _cached;
    private DateTime _cachedAt;
    private UpdateState _state = new();
    private string _staged = string.Empty;
    private string _readyVersion = string.Empty;

    public UpdateService()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Akashic");
        _updatesDir = Path.Combine(root, "updates");
    }

    public static string CurrentVersion
    {
        get
        {
            var raw = typeof(UpdateService).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? typeof(UpdateService).Assembly.GetName().Version?.ToString()
                ?? "0.0.0";

            var plus = raw.IndexOf('+');
            return plus < 0 ? raw : raw[..plus];
        }
    }

    private HttpClient Route
    {
        get
        {
            lock (_gate) return _http ??= CreateClient(null);
        }
    }

    private static HttpClient CreateClient(WebProxy? proxy)
    {
        var handler = new HttpClientHandler();
        if (proxy is not null) handler.Proxy = proxy;

        var client = new HttpClient(handler) { Timeout = RequestTimeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Akashic/{CurrentVersion}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private async Task<HttpClient> EnsureRouteAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (_http is not null) return _http;
        }

        foreach (var (label, prefix, proxyUrl) in Routes)
        {
            WebProxy? proxy = null;

            if (proxyUrl.Length > 0)
            {
                if (!Uri.TryCreate(proxyUrl, UriKind.Absolute, out var uri)) continue;
                if (!PortOpen(uri.Host, uri.Port)) continue;

                proxy = new WebProxy(uri);
            }

            var client = CreateClient(proxy);

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(ProbeTimeout);

                using var probe = await client.GetAsync(
                    prefix + "https://api.github.com/rate_limit", timeout.Token);

                if (probe.IsSuccessStatusCode || (int)probe.StatusCode == 403)
                {
                    lock (_gate)
                    {
                        _http = client;
                        _route = label;
                        _prefix = prefix;
                    }
                    return client;
                }
            }
            catch (Exception)
            {
            }

            client.Dispose();
        }

        lock (_gate)
        {
            _http = CreateClient(null);
            _route = "直连";
            _prefix = string.Empty;
        }

        return _http;
    }

    private string RouteUrl(string url)
    {
        lock (_gate) return _prefix + url;
    }

    private static bool PortOpen(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            var task = client.ConnectAsync(host, port);
            return task.Wait(TimeSpan.FromMilliseconds(300)) && client.Connected;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<UpdateInfo> CheckAsync(bool force = false, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!force && _cached is not null)
            {
                var ttl = _cached.Error.Length == 0 ? CacheFor : FailureCacheFor;
                if (DateTime.UtcNow - _cachedAt < ttl) return _cached;
            }
        }

        UpdateInfo info;

        try
        {
            var client = await EnsureRouteAsync(ct);
            var url = RouteUrl($"https://api.github.com/repos/{Repo}/releases/latest");

            using var response = await client.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var release = JsonSerializer.Deserialize<GitHubRelease>(
                await response.Content.ReadAsStringAsync(ct), JsonOptions);

            info = Build(release);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException)
        {
            info = new UpdateInfo
            {
                Current = CurrentVersion,
                Error = "检查更新失败：" + error.Message
            };
        }

        info.Route = _route;

        lock (_gate)
        {
            _cached = info;
            _cachedAt = DateTime.UtcNow;
        }

        return info;
    }

    private static UpdateInfo Build(GitHubRelease? release)
    {
        var current = CurrentVersion;

        if (release is null || string.IsNullOrWhiteSpace(release.TagName))
        {
            return new UpdateInfo { Current = current, Error = "GitHub 没有返回任何发布版本。" };
        }

        var latest = release.TagName.Trim().TrimStart('v', 'V');
        var asset = release.Assets
            .FirstOrDefault(item => item.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

        var currentVersion = ParseVersion(current);
        var latestVersion = ParseVersion(latest);

        return new UpdateInfo
        {
            Current = current,
            Latest = latest,
            HasUpdate = currentVersion is not null && latestVersion is not null && latestVersion > currentVersion,
            Title = release.Name ?? string.Empty,
            Notes = release.Body ?? string.Empty,
            PageUrl = release.HtmlUrl ?? string.Empty,
            AssetName = asset?.Name ?? string.Empty,
            AssetUrl = asset?.BrowserDownloadUrl ?? string.Empty,
            AssetBytes = asset?.Size ?? 0,
            PublishedAt = DateTime.TryParse(release.PublishedAt, out var published) ? published.ToLocalTime() : null
        };
    }

    private static Version? ParseVersion(string raw)
    {
        var match = System.Text.RegularExpressions.Regex.Match(raw, @"\d+(?:\.\d+){1,3}");
        return match.Success && Version.TryParse(match.Value, out var parsed) ? parsed : null;
    }

    public UpdateState GetState()
    {
        lock (_gate)
        {
            return new UpdateState
            {
                Phase = _state.Phase,
                Message = _state.Message,
                Percent = _state.Percent,
                Version = _state.Version,
                Route = _route,
                Received = _state.Received,
                Total = _state.Total
            };
        }
    }

    private void SetState(string phase, string message, double percent = 0, string version = "",
        long received = 0, long total = 0)
    {
        lock (_gate)
        {
            _state = new UpdateState
            {
                Phase = phase,
                Message = message,
                Percent = percent,
                Version = version,
                Route = _route,
                Received = received,
                Total = total
            };
        }
    }

    public async Task<UpdateState> DownloadAsync(CancellationToken ct = default)
    {
        var info = await CheckAsync(false, ct);

        if (info.Error.Length > 0)
        {
            SetState("error", info.Error);
            return GetState();
        }

        if (!info.HasUpdate)
        {
            SetState("latest", $"已经是最新版本（{info.Current}）");
            return GetState();
        }

        if (string.IsNullOrWhiteSpace(info.AssetUrl))
        {
            SetState("error", $"{info.Latest} 没有附带 zip 资源。");
            return GetState();
        }

        var versionDir = Path.Combine(_updatesDir, info.Latest);
        var zipPath = Path.Combine(versionDir, info.AssetName);
        var stagedDir = Path.Combine(versionDir, "staged");

        try
        {
            var client = await EnsureRouteAsync(ct);
            var release = await FetchReleaseAsync(client, ct);

            var asset = release?.Assets.FirstOrDefault(item =>
                string.Equals(item.Name, info.AssetName, StringComparison.OrdinalIgnoreCase));
            var hashAsset = release?.Assets.FirstOrDefault(item =>
                string.Equals(item.Name, info.AssetName + ".sha256", StringComparison.OrdinalIgnoreCase));
            var sigAsset = release?.Assets.FirstOrDefault(item =>
                string.Equals(item.Name, info.AssetName + ".sig", StringComparison.OrdinalIgnoreCase));

            if (asset is null || hashAsset is null || sigAsset is null)
            {
                SetState("error", "这个版本缺少签名文件，已拒绝安装。发布时需附 .sha256 与 .sig。");
                return GetState();
            }

            Directory.CreateDirectory(versionDir);
            SetState("downloading", $"正在下载 {info.Latest}…", 0, info.Latest);

            await DownloadToAsync(client, RouteUrl(asset.BrowserDownloadUrl), zipPath,
                asset.Size, info.Latest, ct);

            SetState("verifying", "正在校验签名…", 100, info.Latest);

            var expected = await ReadTextAsync(client, RouteUrl(hashAsset.BrowserDownloadUrl), ct);
            var signature = Convert.FromBase64String(
                (await ReadTextAsync(client, RouteUrl(sigAsset.BrowserDownloadUrl), ct)).Trim());

            if (!VerifyZip(zipPath, expected, signature, out var reason))
            {
                SetState("error", "更新包校验失败：" + reason);
                return GetState();
            }

            SetState("extracting", "正在解压安装包…", 100, info.Latest);

            if (Directory.Exists(stagedDir)) Directory.Delete(stagedDir, true);
            ZipFile.ExtractToDirectory(zipPath, stagedDir, overwriteFiles: true);

            var inner = FindAppRoot(stagedDir);
            if (inner is null)
            {
                SetState("error", "下载的压缩包里没有 akashic_ui.exe。");
                return GetState();
            }

            lock (_gate)
            {
                _staged = inner;
                _readyVersion = info.Latest;
            }

            SetState("ready", $"已下载并校验 {info.Latest}，重启后完成安装。", 100, info.Latest,
                asset.Size, asset.Size);
            return GetState();
        }
        catch (Exception error) when (error is HttpRequestException or IOException or FormatException
                                          or InvalidDataException or UnauthorizedAccessException
                                          or CryptographicException)
        {
            SetState("error", "下载或校验失败：" + error.Message);
            return GetState();
        }
    }

    private async Task<GitHubRelease?> FetchReleaseAsync(HttpClient client, CancellationToken ct)
    {
        var url = RouteUrl($"https://api.github.com/repos/{Repo}/releases/latest");
        using var response = await client.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode) return null;

        return JsonSerializer.Deserialize<GitHubRelease>(
            await response.Content.ReadAsStringAsync(ct), JsonOptions);
    }

    private async Task DownloadToAsync(HttpClient client, string url, string path, long expectedBytes,
        string version, CancellationToken ct)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? expectedBytes;

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var file = new FileStream(path, FileMode.Create, FileAccess.Write,
            FileShare.None, 81920, true);

        var buffer = new byte[81920];
        long written = 0;
        int read;

        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct);
            written += read;

            if (total > 0)
            {
                SetState("downloading", $"正在下载 {version}…", written * 100.0 / total, version,
                    written, total);
            }
        }
    }

    private static async Task<string> ReadTextAsync(HttpClient client, string url, CancellationToken ct)
    {
        using var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    private static bool VerifyZip(string path, string hashText, byte[] signature, out string reason)
    {
        reason = string.Empty;

        using var stream = File.OpenRead(path);
        var actual = SHA256.HashData(stream);

        var match = System.Text.RegularExpressions.Regex.Match(hashText, @"\b[0-9a-fA-F]{64}\b");
        if (!match.Success)
        {
            reason = "发布页的 .sha256 格式不对。";
            return false;
        }

        var expected = Convert.FromHexString(match.Value);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            reason = "SHA256 与发布页记录不一致。";
            return false;
        }

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKey), out _);

        if (!ecdsa.VerifyHash(actual, signature, DSASignatureFormat.Rfc3279DerSequence))
        {
            reason = "签名与内置公钥不匹配。";
            return false;
        }

        return true;
    }

    private static string? FindAppRoot(string stagedDir)
    {
        if (File.Exists(Path.Combine(stagedDir, "akashic_ui.exe"))) return stagedDir;

        foreach (var child in Directory.EnumerateDirectories(stagedDir))
            if (File.Exists(Path.Combine(child, "akashic_ui.exe")))
                return child;

        return null;
    }

    public bool Apply(int uiPid)
    {
        string staged;

        lock (_gate) staged = _staged;

        if (staged.Length == 0 || !File.Exists(Path.Combine(staged, "akashic_ui.exe")))
            return false;

        var hostDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var install = Directory.GetParent(hostDir)?.FullName;
        if (install is null || !File.Exists(Path.Combine(install, "akashic_ui.exe")))
            return false;

        try
        {
            var scriptPath = Path.Combine(Path.GetDirectoryName(staged) ?? _updatesDir, "apply.ps1");
            File.WriteAllText(scriptPath, ApplyScript);

            var start = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-ExecutionPolicy");
            start.ArgumentList.Add("Bypass");
            start.ArgumentList.Add("-File");
            start.ArgumentList.Add(scriptPath);
            start.ArgumentList.Add("-UiPid");
            start.ArgumentList.Add(uiPid.ToString());
            start.ArgumentList.Add("-HostPid");
            start.ArgumentList.Add(Environment.ProcessId.ToString());
            start.ArgumentList.Add("-Staged");
            start.ArgumentList.Add(staged);
            start.ArgumentList.Add("-Install");
            start.ArgumentList.Add(install);

            Process.Start(start);

            SetState("applying", "应用将在退出后自动完成。", 100, _readyVersion);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("published_at")]
        public string? PublishedAt { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; set; } = [];
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;
    }
}
