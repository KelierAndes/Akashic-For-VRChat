using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Akashic.Core.Models;

namespace Akashic.Core.Services;

public interface IConfigSyncService
{
    Task<SyncSnapshot> StatusAsync(CancellationToken ct = default);

    Task<VcsRunResult> PushAsync(CancellationToken ct = default);

    Task<VcsRunResult> PullAsync(CancellationToken ct = default);

    /// <summary>Turns the sync on or off. The GitHub sign-in itself is untouched.</summary>
    Task SetEnabledAsync(bool enabled, CancellationToken ct = default);
}

/// <summary>
/// Moves this machine's configuration to another one through a private gist owned by the
/// user's own GitHub account.
///
/// The payload is encrypted before it leaves the box and only this app ever holds the key, so
/// GitHub stores opaque base64. The key is derived from the permanent numeric user.id of the
/// GitHub account plus a pepper built into Akashic, which is what makes the round trip zero-touch:
/// sign in on the new computer and the same key comes back, with no sync passphrase to set or to
/// remember.
///
/// Being exact about what that buys. user.id is public and the pepper ships inside the binary, so
/// this is strong obfuscation against GitHub, against anyone holding the gist, and against anyone
/// watching the transfer — it is not a secret that survives someone who has both the gist and
/// Akashic itself. The previous design derived the key from the OAuth token in the keyring, which
/// was genuinely secret but rotated on every sign-in and silently locked people out of their own
/// backup. The envelope still carries a four-byte key fingerprint, so restoring under another
/// account reports that rather than blaming a corrupt file.
///
/// DPAPI blobs are never shipped. The three storefront sessions are decrypted here, put in the
/// bundle as plain cookies, and re-protected by the local vault on the way back in — a copied
/// <c>sessions/*.bin</c> would be worthless on another machine anyway.
/// </summary>
public sealed class ConfigSyncService : IConfigSyncService
{
    private const string BundleKind = "akashic-bundle";
    private const string EnvelopeKind = "akashic-sync";
    private const int Version = 1;
    private const int Iterations = 310_000;

    private static readonly byte[] KeyPrefix = "Akashic.Sync.v2:github-id:"u8.ToArray();
    private static readonly byte[] OwnerHintSalt = "Akashic.Sync.Owner.v1"u8.ToArray();

    /// <summary>
    /// Compiled into every copy of Akashic on purpose: it is what turns a public user.id into a key
    /// nobody can rebuild from the gist alone. See the class summary for where that stops.
    /// </summary>
    private const string KeyPepper = "Akashic_v1_AutoDeriveKey_9f8a7b6c!";

    /// <summary>
    /// The only files ever carried, by name relative to the data directory. Anything else in a
    /// bundle is ignored, which is also what stops a crafted gist from writing outside it.
    /// </summary>
    private static readonly (string Path, string Label)[] CacheFiles =
    [
        ("cache/booth-library.json", "Booth 已购清单"),
        ("cache/assetstore-account.json", "Asset Store 资产清单"),
        ("cache/assetstore-covers.json", "Asset Store 封面链接"),
        ("cache/vrchat-content.json", "VRChat 内容清单"),
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
    private readonly string _settingsPath;
    private readonly string _dataDir;

    public ConfigSyncService(ISessionVault vault, GitHubCli gitHub, string? settingsPath = null)
    {
        _vault = vault;
        _gitHub = gitHub;
        _settingsPath = settingsPath ?? Path.Combine(DataRoot, "settings.json");
        _dataDir = Path.GetDirectoryName(_settingsPath) ?? DataRoot;
    }

    private static string DataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Akashic");

    // ---------------------------------------------------------------- status

    public async Task<SyncSnapshot> StatusAsync(CancellationToken ct = default)
    {
        var exe = GitHubCli.Find();
        var identity = exe == null ? null : await _gitHub.IdentityAsync(ct);
        var settings = LoadSettings();

        var included = new List<string> { "设置（工程列表、路径、模板、快捷栏、主题）" };
        included.AddRange(CacheFiles.Where(file => File.Exists(Resolve(file.Path))).Select(file => file.Label));
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

    /// <summary>
    /// The 启用 gist 同步 switch, honoured by both directions — including the automatic upload
    /// that follows a first sign-in, which is the path that would otherwise sync behind a user
    /// who has just turned it off.
    /// </summary>
    private VcsRunResult? Blocked(string action) =>
        LoadSettings().SyncEnabled
            ? null
            : VcsRunResult.Fail(action, string.Empty, "配置同步已关闭",
                "在 设置 → 配置同步 打开「启用 gist 同步」再试。GitHub 登录本身不受影响，" +
                "私有仓库外部包照常可用。");

    // ---------------------------------------------------------------- push

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

        var bundle = BuildBundle();
        var plain = JsonSerializer.SerializeToUtf8Bytes(bundle, TextOptions);

        var salt = RandomNumberGenerator.GetBytes(16);
        var iv = RandomNumberGenerator.GetBytes(12);
        var key = DeriveKey(identity.UserId, salt);

        var cipher = new byte[plain.Length];
        var tag = new byte[16];

        using (var aes = new AesGcm(key, 16)) aes.Encrypt(iv, plain, cipher, tag);

        // Taken before the key is wiped: it is what lets a restore blame the wrong identity
        // rather than a corrupt file.
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
                // What fed PBKDF2. Public and stable on purpose (account id + built-in pepper), so a
                // second machine derives the same key after one click in the browser.
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
                // Names only. Everything outside "payload" travels unencrypted, so a DeepClone
                // of the files here would ship the whole plaintext bundle — cookies included —
                // to GitHub beside the ciphertext it is supposed to hide.
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

    // ---------------------------------------------------------------- pull

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

        // Nobody types or sees an id: this machine's own record first, then the account's gist list —
        // which is how a second machine finds the backup after one click in the browser.
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
            // A field hand-edited into the wrong JSON type throws when it is read back, and that must
            // not turn into an unhandled error in the middle of a restore.
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

            // The bundle holds settings.json as it looked when it was made — on a first push that is
            // before the gist id and the upload stamp were written down. Put them back so a machine
            // that has just been restored still knows where its backup lives.
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
            // Bookkeeping only. A restore that worked must not be reported as a failure because the
            // stamp could not be written back.
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

            // Handled wholesale further down, and re-protected by the local vault.
            if (entry.Name == "sessions") continue;

            if (entry.Name == "settings.json")
            {
                // Refuse to write a settings file that would break every service on next read.
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

                // The vault treats an empty set as a sign-out, so only a real list is written back.
                if (cookies.Count == 0) continue;

                _vault.Save(site.Site, cookies);
                restored++;
                written.Add(site.Label);
            }
        }

        var output = $"已还原 {written.Count} 项：{string.Join(" · ", written)}";
        if (skipped.Count > 0) output += $"\n\n跳过 {skipped.Count} 项：{string.Join(" · ", skipped)}";
        output += "\n\n登录态与清单缓存立即可用；工程列表与其他设置在重启 Akashic 后生效。";

        return VcsRunResult.Ok("sync-pull", string.Empty,
            $"配置已还原（{restored} 站登录态）", output);
    }

    private static string? Read(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // ---------------------------------------------------------------- bundle

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

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// PBKDF2 over "<prefix><user.id>_<pepper>". The numeric account id never changes and the pepper
    /// is identical in every build, so two machines that sign into the same GitHub account land on the
    /// same key without anyone setting or remembering a sync password.
    /// </summary>
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

    /// <summary>
    /// Four bytes of HMAC over the key, hex-encoded. Enough to tell "this backup was made by a
    /// different GitHub authorisation" apart from "the file is corrupt", and it leaks nothing
    /// about the token that produced the key.
    /// </summary>
    private static string OwnerHint(byte[] key) =>
        Convert.ToHexString(HMACSHA256.HashData(key, OwnerHintSalt), 0, 4);

    /// <summary>Resolves a bundle name against the data directory, refusing anything that escapes.</summary>
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
        // Re-read first: every other service owns different fields of the same file.
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
