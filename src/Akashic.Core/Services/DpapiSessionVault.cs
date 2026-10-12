using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Akashic.Core.Services;

/// <summary>
/// Stores each site's cookies as one DPAPI blob under <c>%LOCALAPPDATA%\Akashic\sessions</c>.
///
/// The scope is <see cref="DataProtectionScope.CurrentUser"/>, so a blob can only be decrypted by
/// this Windows account: copying the folder to another machine or another user yields nothing. That
/// is the boundary worth being exact about — a process already running as this user can call DPAPI
/// itself, so this protects the file at rest and against casual leakage, not against malware on the
/// account. The previous design kept sessions in memory only, which meant any WebView2 profile reset
/// signed the user out of all three storefronts.
/// </summary>
public sealed class DpapiSessionVault : ISessionVault
{
    // Not a key — DPAPI keys come from the Windows login. This only stops a same-user process from
    // handing our blob to any other ProtectedData caller and getting plaintext back.
    private static readonly byte[] Entropy =
        "Akashic.SessionVault.v1"u8.ToArray();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private static readonly object Gate = new();

    private readonly Dictionary<string, StoredSession?> _memo = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _directory;

    public DpapiSessionVault()
    {
        _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Akashic", "sessions");
    }

    public StoredSession? Load(string site)
    {
        var key = SafeName(site);
        if (key is null) return null;

        lock (Gate)
        {
            if (_memo.TryGetValue(key, out var memoised)) return memoised;
        }

        var stored = ReadFromDisk(key);

        lock (Gate) _memo[key] = stored;
        return stored;
    }

    public void Save(string site, IReadOnlyList<StoredCookie> cookies)
    {
        var key = SafeName(site);
        if (key is null) return;

        var clean = new List<StoredCookie>(cookies.Count);

        foreach (var cookie in cookies)
        {
            if (string.IsNullOrWhiteSpace(cookie.Name) || string.IsNullOrWhiteSpace(cookie.Domain)) continue;

            clean.Add(cookie with
            {
                Name = cookie.Name.Trim(),
                Value = cookie.Value?.Trim() ?? string.Empty,
                Domain = cookie.Domain.Trim(),
                Path = string.IsNullOrWhiteSpace(cookie.Path) ? "/" : cookie.Path.Trim()
            });
        }

        // An empty set means the site reported a logged-out state, which is a reason to forget the
        // stored session rather than keep handing the services a dead cookie.
        if (clean.Count == 0)
        {
            Clear(site);
            return;
        }

        var payload = new StoredPayload { SavedAt = DateTime.Now, Cookies = clean };

        try
        {
            Directory.CreateDirectory(_directory);

            var plain = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
            var cipher = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);

            // Write aside so a crash mid-save cannot leave a half-written blob in place of a good one.
            var target = Path.Combine(_directory, key + ".bin");
            var temp = target + ".tmp";
            File.WriteAllBytes(temp, cipher);
            File.Move(temp, target, overwrite: true);

            lock (Gate) _memo[key] = new StoredSession(clean, payload.SavedAt);
        }
        catch (Exception)
        {
            // Failing to remember a session must never break the read that is in flight.
        }
    }

    public void Clear(string site)
    {
        var key = SafeName(site);
        if (key is null) return;

        try
        {
            var path = Path.Combine(_directory, key + ".bin");
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception)
        {
        }

        lock (Gate) _memo[key] = null;
    }

    private StoredSession? ReadFromDisk(string key)
    {
        try
        {
            var path = Path.Combine(_directory, key + ".bin");
            if (!File.Exists(path)) return null;

            var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            using var doc = JsonDocument.Parse(plain);

            if (!doc.RootElement.TryGetProperty("Cookies", out var element)) return null;

            var savedAt = doc.RootElement.TryGetProperty("SavedAt", out var stamp) &&
                          stamp.TryGetDateTime(out var when)
                ? when
                : DateTime.Now;

            var cookies = ReadCookies(element, key);
            return cookies.Count == 0 ? null : new StoredSession(cookies, savedAt);
        }
        catch (CryptographicException)
        {
            // Wrong Windows account, or the blob was edited: treat it as no session at all.
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static List<StoredCookie> ReadCookies(JsonElement element, string key)
    {
        var cookies = new List<StoredCookie>();

        // Blobs written before the domain was stored carry a flat name/value object. Promoting them
        // with the site's own root domain keeps a session that was already working — VRChat reads its
        // API off the name/value pair and never needed the domain — and gives the Asset Store's
        // session token back the domain it was actually issued on. The next export overwrites the
        // blob with the real per-cookie domains.
        if (element.ValueKind == JsonValueKind.Object)
        {
            var fallback = LegacyDomain(key);

            foreach (var property in element.EnumerateObject())
                if (property.Value.ValueKind == JsonValueKind.String)
                    cookies.Add(new StoredCookie(property.Name, property.Value.GetString() ?? "", fallback));

            return cookies;
        }

        if (element.ValueKind != JsonValueKind.Array) return cookies;

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (!item.TryGetProperty("Name", out var name)) continue;
            if (!item.TryGetProperty("Domain", out var domain)) continue;

            var value = item.TryGetProperty("Value", out var v) ? v.GetString() ?? "" : "";
            var path = item.TryGetProperty("Path", out var p) && !string.IsNullOrWhiteSpace(p.GetString())
                ? p.GetString()!
                : "/";

            cookies.Add(new StoredCookie(name.GetString() ?? "", value, domain.GetString() ?? "", path));
        }

        return cookies;
    }

    private static string LegacyDomain(string key) => key.ToLowerInvariant() switch
    {
        "booth" => ".booth.pm",
        "assetstore" => ".unity.com",
        _ => ".vrchat.com"
    };

    /// <summary>Site names become file names, so anything odder than a label is refused.</summary>
    private static string? SafeName(string site)
    {
        if (string.IsNullOrWhiteSpace(site)) return null;

        var trimmed = site.Trim();
        foreach (var c in trimmed)
            if (!char.IsLetterOrDigit(c) && c is not ('-' or '_')) return null;

        return trimmed.Length > 40 ? null : trimmed;
    }

    private sealed class StoredPayload
    {
        public DateTime SavedAt { get; set; }

        public List<StoredCookie> Cookies { get; set; } = [];
    }
}
