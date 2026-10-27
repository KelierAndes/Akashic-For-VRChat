using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Akashic.Core.Data;
using Akashic.Core.Models;
using Akashic.Core.Services.Preview;

namespace Akashic.Core.Services;

public interface IConfigSyncService
{
    Task<SyncSnapshot> StatusAsync(CancellationToken ct = default);

    Task<VcsRunResult> PushAsync(CancellationToken ct = default);

    Task<VcsRunResult> PullAsync(CancellationToken ct = default);

    Task SetEnabledAsync(bool enabled, CancellationToken ct = default);
}

public sealed class ConfigSyncService : IConfigSyncService
{
    private const string BundleKind = "akashic-bundle";
    private const string EnvelopeKind = "akashic-sync";
    private const int Version = 1;
    private const int Iterations = 310_000;

    private static readonly byte[] KeyPrefix = "Akashic.Sync.v2:github-id:"u8.ToArray();
    private static readonly byte[] OwnerHintSalt = "Akashic.Sync.Owner.v1"u8.ToArray();

    private const string KeyPepper = "Akashic_v1_AutoDeriveKey_9f8a7b6c!";

    private static readonly (string Path, string Label)[] CacheFiles =
    [
        ("cache/booth-library.json", "Booth 已购清单"),
        ("cache/assetstore-account.json", "Asset Store 资产清单"),
        ("cache/assetstore-covers.json", "Asset Store 封面链接"),
        ("cache/vrchat-content.json", "VRChat 内容清单"),
        ("cache/resource-meta.json", "资源分类与匹配"),
        ("unitypackage-ledger.json", "unitypackage 安装台账")
    ];

    private static readonly (string Site, string Label)[] Sessions =
    [
        ("vrchat", "VRChat 登录态"),
        ("booth", "Booth 登录态"),
        ("assetstore", "Asset Store 登录态")
    ];

    private static readonly JsonSerializerOptions TextOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly GitHubCli _gitHub;
    private readonly ISessionVault _vault;
    private readonly AkashicDatabase _db;
    private readonly IAssetStoreService? _assetStore;
    private readonly IResourceCatalogService? _resources;
    private readonly IThemeService? _theme;
    private readonly IRailLayoutService? _rail;
    private readonly string _settingsPath;
    private readonly string _dataDir;

    public ConfigSyncService(ISessionVault vault, GitHubCli gitHub, string? settingsPath = null,
        AkashicDatabase? database = null, IAssetStoreService? assetStore = null,
        IResourceCatalogService? resources = null, IThemeService? theme = null,
        IRailLayoutService? rail = null)
    {
        _vault = vault;
        _gitHub = gitHub;
        _settingsPath = settingsPath ?? Path.Combine(DataRoot, "settings.json");
        _dataDir = Path.GetDirectoryName(_settingsPath) ?? DataRoot;
        _db = database ?? new AkashicDatabase(_dataDir);
        _assetStore = assetStore;
        _resources = resources;
        _theme = theme;
        _rail = rail;
    }

    private static string DataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Akashic");


    private List<string> ActiveCacheLabels()
    {
        var labels = new List<string>();
        if (_db.Count("booth_items") > 0) labels.Add("Booth 已购清单");
        if (_db.Count("assetstore_items") > 0) labels.Add("Asset Store 资产清单");
        if (_db.Count("assetstore_covers") > 0) labels.Add("Asset Store 封面链接");
        if (_db.Count("vrchat_items") > 0) labels.Add("VRChat 内容清单");
        if (_db.Count("resource_meta") > 0) labels.Add("资源分类与匹配");
        if (_db.Count("install_records") > 0) labels.Add("unitypackage 安装台账");
        return labels;
    }

    public async Task<SyncSnapshot> StatusAsync(CancellationToken ct = default)
    {
        var exe = GitHubCli.Find();
        var identity = exe == null ? null : await _gitHub.IdentityAsync(ct);
        var settings = LoadSettings();

        var included = new List<string> { "设置（工程列表、路径、模板、快捷栏、主题、透明度与预览缓存）" };
        included.AddRange(ActiveCacheLabels());
        included.AddRange(Sessions
            .Where(site => _vault.Load(site.Site)?.Cookies.Count > 0)
            .Select(site => site.Label));

        var note = exe == null
            ? "未检测到 GitHub CLI。在 设置 → 版本控制依赖 一键安装，然后在 配置同步 点「使用浏览器登录 GitHub」。"
            : identity?.SignedIn != true
                ? "GitHub CLI 已安装，但没有登录账号。点「使用浏览器登录 GitHub」，在浏览器里点一次 Authorize 即可。"
                : !identity.HasGistScope
                    ? $"{identity.Login} 的令牌没有 gist 权限。重新点一次「使用浏览器登录 GitHub」会带上该权限。"
                    : identity.UserId <= 0
                        ? "没有取到 GitHub 数字 id，无法派生同步密钥。重新登录一次即可。"
                        : string.Empty;

        return new SyncSnapshot
        {
            GhFound = exe != null,
            GhVersion = identity?.Version ?? string.Empty,
            SignedIn = identity?.SignedIn == true,
            Login = identity?.Login ?? string.Empty,
            AvatarUrl = identity?.AvatarUrl ?? string.Empty,
            HasGistScope = identity?.HasGistScope == true,
            Enabled = settings.SyncEnabled,
            UploadedAt = settings.SyncUploadedAt,
            PayloadBytes = settings.SyncPayloadBytes,
            Included = included,
            Note = note
        };
    }

    public async Task SetEnabledAsync(bool enabled, CancellationToken ct = default)
    {
        var settings = LoadSettings();
        settings.SyncEnabled = enabled;
        await SaveSettingsAsync(settings, ct);
    }

    private VcsRunResult? Blocked(string action) =>
        LoadSettings().SyncEnabled
            ? null
            : VcsRunResult.Fail(action, string.Empty, "配置同步已关闭",
                "在 设置 → 配置同步 打开「启用 gist 同步」再试。GitHub 登录本身不受影响，" +
                "私有仓库外部包照常可用。");


    public async Task<VcsRunResult> PushAsync(CancellationToken ct = default)
    {
        const string action = "sync-push";

        if (Blocked(action) is { } pushBlocked) return pushBlocked;

        var identity = await _gitHub.IdentityAsync(ct);
        if (GitHubCli.Find() == null)
            return VcsRunResult.Fail(action, string.Empty, "没有 GitHub CLI",
                "请先在 设置 → 版本控制依赖 安装 GitHub CLI。");

        if (identity?.SignedIn != true)
            return VcsRunResult.Fail(action, string.Empty, "GitHub 未登录",
                "请在 设置 → 配置同步 点「使用浏览器登录 GitHub」，在浏览器里点一次 Authorize。");

        if (!identity.HasGistScope)
            return VcsRunResult.Fail(action, string.Empty, "令牌缺少 gist 权限",
                "重新点一次「使用浏览器登录 GitHub」，授权时勾上 gist。");

        if (identity.UserId <= 0)
            return VcsRunResult.Fail(action, string.Empty, "读不到 GitHub 数字 id",
                "同步密钥是从账号 id 派生的，没有它无法加密。重新登录一次即可。");

        _db.ExportSnapshots();
        var bundle = BuildBundle();
        var plain = JsonSerializer.SerializeToUtf8Bytes(bundle, TextOptions);

        var salt = RandomNumberGenerator.GetBytes(16);
        var iv = RandomNumberGenerator.GetBytes(12);
        var key = DeriveKey(identity.UserId, salt);

        var cipher = new byte[plain.Length];
        var tag = new byte[16];

        using (var aes = new AesGcm(key, 16)) aes.Encrypt(iv, plain, cipher, tag);

        var hint = OwnerHint(key);

        CryptographicOperations.ZeroMemory(key);
        var plainBytes = plain.Length;
        CryptographicOperations.ZeroMemory(plain);

        var envelope = new JsonObject
        {
            ["kind"] = EnvelopeKind,
            ["version"] = Version,
            ["createdAt"] = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ["device"] = Environment.MachineName,
            ["owner"] = new JsonObject
            {
                ["login"] = identity.Login,
                ["id"] = identity.UserId,
                ["hint"] = hint
            },
            ["kdf"] = new JsonObject
            {
                ["algo"] = "PBKDF2-HMAC-SHA256",
                ["secret"] = "github-id",
                ["iterations"] = Iterations,
                ["salt"] = Convert.ToBase64String(salt)
            },
            ["cipher"] = new JsonObject
            {
                ["algo"] = "A256GCM",
                ["iv"] = Convert.ToBase64String(iv),
                ["tag"] = Convert.ToBase64String(tag)
            },
            ["payload"] = Convert.ToBase64String(cipher),
            ["summary"] = new JsonObject
            {
                ["bytes"] = plainBytes,
                ["files"] = new JsonArray([.. bundle["files"]!.AsObject()
                    .Select(entry => (JsonNode?)JsonValue.Create(entry.Key))])
            }
        };

        var settings = LoadSettings();
        var target = settings.SyncGistId;
        if (string.IsNullOrWhiteSpace(target)) target = await _gitHub.FindGistAsync(ct);

        var (id, error) = await _gitHub.PushGistAsync(envelope.ToJsonString(), target, ct);
        if (id is null)
            return VcsRunResult.Fail(action, string.Empty, "上传失败", error);

        settings.SyncGistId = id;
        settings.SyncUploadedAt = DateTime.Now;
        settings.SyncPayloadBytes = cipher.Length;
        await SaveSettingsAsync(settings, ct);

        var listed = bundle["files"]!.AsObject();
        return VcsRunResult.Ok(action, string.Empty, "已上传到私有 gist",
            $"{identity.Login} · {cipher.Length / 1024} KB 密文 · {listed.Count} 项\n\n" +
            "明文从未离开本机，GitHub 只看到 base64。备份地址不在界面上出现，恢复时按当前账号自动定位。");
    }


    public async Task<VcsRunResult> PullAsync(CancellationToken ct = default)
    {
        const string action = "sync-pull";

        if (Blocked(action) is { } pullBlocked) return pullBlocked;

        var identity = await _gitHub.IdentityAsync(ct);
        if (identity?.SignedIn != true)
            return VcsRunResult.Fail(action, string.Empty, "GitHub 未登录",
                "请在 设置 → 配置同步 点「使用浏览器登录 GitHub」，在浏览器里点一次 Authorize。");

        if (identity.UserId <= 0)
            return VcsRunResult.Fail(action, string.Empty, "读不到 GitHub 数字 id",
                "同步密钥是从账号 id 派生的，没有它无法解密。重新登录一次即可。");

        var settings = LoadSettings();
        var target = settings.SyncGistId;
        if (string.IsNullOrWhiteSpace(target)) target = await _gitHub.FindGistAsync(ct) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(target))
            return VcsRunResult.Fail(action, string.Empty, "没有找到备份 gist",
                "这个 GitHub 账号下还没有 Akashic 的同步 gist。先在原来的机器上点一次「上传到 GitHub」。");

        var raw = await _gitHub.PullGistAsync(target, ct);
        if (string.IsNullOrWhiteSpace(raw))
            return VcsRunResult.Fail(action, string.Empty, "读取 gist 失败",
                $"这个账号下的同步 gist 里没有 {GitHubCli.GistFile}，或没有权限读取。");

        JsonNode? envelope;
        try
        {
            envelope = JsonNode.Parse(raw);
        }
        catch (JsonException)
        {
            return VcsRunResult.Fail(action, string.Empty, "备份内容不是有效的 JSON", "已停止，本机配置未改动。");
        }

        if (envelope is null)
            return VcsRunResult.Fail(action, string.Empty, "备份内容是空的", "已停止，本机配置未改动。");

        string owner;
        try
        {
            owner = envelope["owner"]?["login"]?.GetValue<string>() ?? string.Empty;

            if (envelope["kind"]?.GetValue<string>() != EnvelopeKind)
                return VcsRunResult.Fail(action, string.Empty, "这不是 Akashic 的同步 gist",
                    "已停止，本机配置未改动。");

            if (envelope["version"]?.GetValue<int>() != Version)
                return VcsRunResult.Fail(action, string.Empty, "备份版本不受支持", "请升级 Akashic 后重试。");
        }
        catch (InvalidOperationException)
        {
            return VcsRunResult.Fail(action, string.Empty, "备份结构不符合预期", "已停止，本机配置未改动。");
        }

        byte[] plain;
        try
        {
            plain = Open(envelope, identity.UserId);
        }
        catch (CryptographicException)
        {
            var from = owner.Length > 0 ? owner : "另一个";
            return VcsRunResult.Fail(action, string.Empty, "解密失败",
                $"这份备份属于 {from} GitHub 账号（当前登录：{identity.Login}）。" +
                "密钥由账号 id 派生，请用上传它的那个账号登录后再恢复。");
        }
        catch (Exception ex)
        {
            return VcsRunResult.Fail(action, string.Empty, "备份无法解析", ex.Message);
        }

        try
        {
            var bundle = JsonSerializer.Deserialize<JsonElement>(plain);
            var restored = Restore(bundle, ct);

            if (restored.Success) await RememberSourceAsync(envelope, target, ct).ConfigureAwait(false);

            return restored;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private async Task RememberSourceAsync(JsonNode envelope, string gistId, CancellationToken ct)
    {
        try
        {
            var settings = LoadSettings();
            settings.SyncGistId = gistId;
            settings.SyncPayloadBytes =
                Convert.FromBase64String(envelope["payload"]!.GetValue<string>()).Length;

            if (DateTime.TryParse(envelope["createdAt"]?.GetValue<string>(), out var when))
                settings.SyncUploadedAt = when;

            await SaveSettingsAsync(settings, ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private static byte[] Open(JsonNode envelope, long userId)
    {
        var salt = Convert.FromBase64String(envelope["kdf"]!["salt"]!.GetValue<string>());
        var iv = Convert.FromBase64String(envelope["cipher"]!["iv"]!.GetValue<string>());
        var tag = Convert.FromBase64String(envelope["cipher"]!["tag"]!.GetValue<string>());
        var cipher = Convert.FromBase64String(envelope["payload"]!.GetValue<string>());
        var iterations = envelope["kdf"]!["iterations"]?.GetValue<int>() ?? Iterations;

        var key = DeriveKey(userId, salt, iterations);
        try
        {
            var expected = OwnerHint(key);
            var stored = envelope["owner"]?["hint"]?.GetValue<string>() ?? string.Empty;
            if (!string.IsNullOrEmpty(stored) &&
                !CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(stored)))
                throw new CryptographicException("密钥指纹不一致");

            var plain = new byte[cipher.Length];
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(iv, cipher, tag, plain);
            return plain;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private VcsRunResult Restore(JsonElement bundle, CancellationToken ct)
    {
        if (bundle.ValueKind != JsonValueKind.Object ||
            !bundle.TryGetProperty("files", out var files) ||
            files.ValueKind != JsonValueKind.Object)
            throw new JsonException("备份里没有 files 段");

        var written = new List<string>();
        var skipped = new List<string>();

        foreach (var entry in files.EnumerateObject())
        {
            var text = entry.Value.ValueKind == JsonValueKind.String
                ? entry.Value.GetString()
                : entry.Value.GetRawText();
            if (string.IsNullOrEmpty(text)) continue;

            if (entry.Name == "sessions") continue;

            if (entry.Name == "settings.json")
            {
                try
                {
                    JsonDocument.Parse(text);
                }
                catch (JsonException)
                {
                    skipped.Add("settings.json（不是合法 JSON）");
                    continue;
                }

                KeepBackup(_settingsPath);
                File.WriteAllText(_settingsPath, text);
                written.Add("设置");
                continue;
            }

            var known = CacheFiles.FirstOrDefault(file =>
                string.Equals(file.Path.Replace('\\', '/'), entry.Name.Replace('\\', '/'),
                    StringComparison.OrdinalIgnoreCase));

            if (known.Path is null)
            {
                skipped.Add($"{entry.Name}（不在允许清单）");
                continue;
            }

            var target = Resolve(known.Path);
            if (target is null)
            {
                skipped.Add($"{entry.Name}（路径越界）");
                continue;
            }

            try
            {
                JsonDocument.Parse(text);
            }
            catch (JsonException)
            {
                skipped.Add($"{known.Label}（不是合法 JSON）");
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, text);
            written.Add(known.Label);
        }

        var restored = 0;
        if (files.TryGetProperty("sessions", out var sessions) && sessions.ValueKind == JsonValueKind.Object)
        {
            foreach (var site in Sessions)
            {
                if (!sessions.TryGetProperty(site.Site, out var stored) ||
                    !stored.TryGetProperty("Cookies", out var list) ||
                    list.ValueKind != JsonValueKind.Array)
                    continue;

                var cookies = new List<StoredCookie>();
                foreach (var cookie in list.EnumerateArray())
                {
                    var name = Read(cookie, "Name");
                    var domain = Read(cookie, "Domain");
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(domain)) continue;

                    var path = Read(cookie, "Path");
                    cookies.Add(new StoredCookie(name, Read(cookie, "Value") ?? string.Empty, domain,
                        string.IsNullOrWhiteSpace(path) ? "/" : path));
                }

                if (cookies.Count == 0) continue;

                _vault.Save(site.Site, cookies);
                restored++;
                written.Add(site.Label);
            }
        }

        _db.ImportSnapshots();
        _assetStore?.ReloadCaches();
        _resources?.ReloadMeta();
        _theme?.Reload();
        _rail?.Reload();

        var applied = LoadSettings();
        PreviewCachePaths.Configure(applied.PreviewCacheDirectory, applied.PreviewCacheQuotaBytes);

        var output = $"已还原 {written.Count} 项：{string.Join(" · ", written)}";
        if (skipped.Count > 0) output += $"\n\n跳过 {skipped.Count} 项：{string.Join(" · ", skipped)}";
        output += "\n\n登录态、清单缓存、主题与透明度、快捷栏、预览缓存目录与上限立即可用；" +
                  "工程列表与其他设置在重启 Akashic 后生效。";

        return VcsRunResult.Ok("sync-pull", string.Empty,
            $"配置已还原（{restored} 站登录态）", output);
    }

    private static string? Read(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;


    private JsonObject BuildBundle()
    {
        var files = new JsonObject();

        if (File.Exists(_settingsPath))
        {
            try
            {
                files["settings.json"] = JsonValue.Create(File.ReadAllText(_settingsPath));
            }
            catch (IOException)
            {
            }
        }

        foreach (var (path, _) in CacheFiles)
        {
            var full = Resolve(path);
            if (full is null || !File.Exists(full)) continue;

            try
            {
                files[path] = JsonValue.Create(File.ReadAllText(full));
            }
            catch (IOException)
            {
            }
        }

        var sessions = new JsonObject();
        foreach (var (site, _) in Sessions)
        {
            var stored = _vault.Load(site);
            if (stored is null || stored.Cookies.Count == 0) continue;

            sessions[site] = new JsonObject
            {
                ["SavedAt"] = stored.SavedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                ["Cookies"] = new JsonArray([.. stored.Cookies.Select(cookie => (JsonNode)new JsonObject
                {
                    ["Name"] = cookie.Name,
                    ["Value"] = cookie.Value,
                    ["Domain"] = cookie.Domain,
                    ["Path"] = cookie.Path
                })])
            };
        }

        if (sessions.Count > 0) files["sessions"] = sessions;

        return new JsonObject
        {
            ["kind"] = BundleKind,
            ["version"] = Version,
            ["createdAt"] = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ["device"] = Environment.MachineName,
            ["files"] = files
        };
    }


    private static byte[] DeriveKey(long userId, byte[] salt, int iterations = Iterations)
    {
        var material = $"{userId}_{KeyPepper}";
        var secret = new byte[KeyPrefix.Length + Encoding.UTF8.GetByteCount(material)];
        KeyPrefix.CopyTo(secret.AsSpan());
        Encoding.UTF8.GetBytes(material, secret.AsSpan(KeyPrefix.Length));

        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(secret, salt, iterations, HashAlgorithmName.SHA256, 32);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    private static string OwnerHint(byte[] key) =>
        Convert.ToHexString(HMACSHA256.HashData(key, OwnerHintSalt), 0, 4);

    private string? Resolve(string relative)
    {
        var clean = relative.Replace('\\', '/').TrimStart('/');
        if (clean.Length == 0 || clean.Contains(":\\") || clean.Contains("../")) return null;

        var full = Path.GetFullPath(Path.Combine(_dataDir, clean));
        var root = Path.GetFullPath(_dataDir);
        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? full
            : null;
    }

    private static void KeepBackup(string path)
    {
        if (!File.Exists(path)) return;

        try
        {
            File.Copy(path, $"{path}.before-sync", overwrite: true);
        }
        catch (IOException)
        {
        }
    }

    private AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath), TextOptions)
                       ?? new AppSettings();
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }

        return new AppSettings();
    }

    private async Task SaveSettingsAsync(AppSettings settings, CancellationToken ct)
    {
        var current = LoadSettings();
        current.SyncGistId = settings.SyncGistId;
        current.SyncUploadedAt = settings.SyncUploadedAt;
        current.SyncPayloadBytes = settings.SyncPayloadBytes;
        current.SyncEnabled = settings.SyncEnabled;

        var temp = _settingsPath + ".sync.tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(current, TextOptions), ct);
        File.Move(temp, _settingsPath, overwrite: true);
    }
}
