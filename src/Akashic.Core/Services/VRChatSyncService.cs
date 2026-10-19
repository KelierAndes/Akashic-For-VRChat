using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Akashic.Core.Models;

namespace Akashic.Core.Services;

public class VRChatSyncService : IVRChatSyncService
{
    private const string ApiBase = "https://vrchat.com/api/1";

    private const string VaultKey = "vrchat";

    private const string BrowserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/124.0.0.0 Safari/537.36 Edg/124.0.0.0";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _client;
    private readonly ISessionVault _vault;
    private Dictionary<string, string> _cookies = new(StringComparer.Ordinal);

    private List<StoredCookie> _entries = [];
    private bool _vaultChecked;

    public VRChatSyncService(ISessionVault vault)
    {
        _vault = vault;

        _client = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false });
        _client.DefaultRequestHeaders.TryAddWithoutValidation("accept", "application/json");
        _client.DefaultRequestHeaders.TryAddWithoutValidation("user-agent", BrowserAgent);
        _client.Timeout = TimeSpan.FromMinutes(3);
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

    public string LoginUrl => "https://vrchat.com/home";

    public async Task<VRChatUser> GetSelfAsync(CancellationToken ct = default)
    {
        var root = await SendAsync(HttpMethod.Get, "/auth/user", null, ct);
        return root.Deserialize<VRChatUser>(SerializerOptions) ?? new VRChatUser();
    }

    public async Task<List<VRChatContentItem>> GetMyContentAsync(CancellationToken ct = default)
    {
        var items = new List<VRChatContentItem>();

        items.AddRange(await GetPagedAsync("/avatars", VRChatContentKind.Avatar, ct));
        var worlds = await GetPagedAsync("/worlds", VRChatContentKind.World, ct);

        items.AddRange(await EnrichAsync(worlds, ct));

        return items;
    }

    private async Task<List<VRChatContentItem>> EnrichAsync(List<VRChatContentItem> rows, CancellationToken ct)
    {
        const int batch = 6;
        var enriched = new List<VRChatContentItem>();

        for (var start = 0; start < rows.Count; start += batch)
        {
            var group = rows.Skip(start).Take(batch).ToList();

            var results = await Task.WhenAll(group.Select(async row =>
                await GetContentAsync(row.Kind, row.Id, ct) ?? row));

            enriched.AddRange(results);
        }

        return enriched;
    }

    public async Task<VRChatContentItem?> GetContentAsync(VRChatContentKind kind, string id,
        CancellationToken ct = default)
    {
        var (root, error) = await SendRawAsync(HttpMethod.Get, $"/{Endpoint(kind)}/{id}", null, ct);

        if (root is null)
            return error?.Contains("没有找到") == true ? null : throw new InvalidOperationException(error ?? "读取失败。");

        return Read(root.Value, kind);
    }

    public async Task<VRChatContentItem> UpdateContentAsync(VRChatContentEdit edit, CancellationToken ct = default)
    {
        var current = await GetContentAsync(edit.Source.Kind, edit.Source.Id, ct) ?? edit.Source;

        var body = new Dictionary<string, object?>
        {
            ["name"] = string.IsNullOrWhiteSpace(edit.Name) ? current.Name : edit.Name.Trim(),
            ["description"] = edit.Description ?? current.Description,
            ["tags"] = edit.Tags ?? current.Tags,
            ["releaseStatus"] = string.IsNullOrWhiteSpace(edit.ReleaseStatus)
                ? current.ReleaseStatus
                : edit.ReleaseStatus
        };

        var image = edit.ImageUrl ?? current.ImageUrl;
        if (!string.IsNullOrWhiteSpace(image)) body["imageUrl"] = image;

        if (current.Kind == VRChatContentKind.World)
        {
            var max = edit.MaxCapacity ?? current.MaxCapacity;
            if (max > 0) body["capacity"] = Math.Max(1, max / 2);

            body["urlList"] = edit.UrlList ?? current.UrlList;
            body["previewYoutubeId"] = edit.PreviewYoutubeId ?? current.PreviewYoutubeId;
            body["defaultContentSettings"] = MergeSettings(current, edit);
            body["disabledPropAbilities"] = edit.DisabledPropAbilities ?? current.DisabledPropAbilities;
        }
        else
        {
            if (current.Version > 0) body["version"] = current.Version;
            if (!string.IsNullOrWhiteSpace(current.UnityPackageUrl)) body["unityPackageUrl"] = current.UnityPackageUrl;

            var styles = edit.Styles ?? current.Styles;
            if (styles is not null) body["styles"] = styles;
        }

        var root = await SendAsync(HttpMethod.Put, $"/{Endpoint(current.Kind)}/{current.Id}", body, ct);

        if (current.Kind == VRChatContentKind.World && edit.Publish is { } publish)
        {
            if (publish) await PublishWorldAsync(current.Id, ct);
            else await UnpublishWorldAsync(current.Id, ct);
        }

        return Read(root, current.Kind) ?? current;
    }

    private static Dictionary<string, JsonElement> MergeSettings(VRChatContentItem current, VRChatContentEdit edit)
    {
        var merged = new Dictionary<string, JsonElement>(current.DefaultContentSettings, StringComparer.Ordinal);

        if (edit.ContentSettings is null) return merged;

        foreach (var (key, enabled) in edit.ContentSettings)
            merged[key] = JsonDocument.Parse(enabled ? "true" : "false").RootElement.Clone();

        return merged;
    }

    public async Task<bool> GetWorldPublishStatusAsync(string id, CancellationToken ct = default)
    {
        var root = await SendAsync(HttpMethod.Get, $"/worlds/{id}/publish", null, ct);

        return root.TryGetProperty("publish", out var p) && p.ValueKind == JsonValueKind.True;
    }

    public async Task PublishWorldAsync(string id, CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Put, $"/worlds/{id}/publish", null, ct);

    public async Task UnpublishWorldAsync(string id, CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Delete, $"/worlds/{id}/publish", null, ct);

    public async Task DeleteContentAsync(VRChatContentKind kind, string id, CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Delete, $"/{Endpoint(kind)}/{id}", null, ct);

    public async Task SelectAvatarAsync(string id, bool fallback, CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Put, $"/avatars/{id}/{(fallback ? "selectFallback" : "select")}", null, ct);

    public async Task EnqueueImpostorAsync(string id, string platform, CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Post, $"/avatars/{id}/impostor/enqueue",
            new Dictionary<string, object?> { ["platform"] = platform }, ct);

    public async Task DeleteImpostorsAsync(string id, CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Delete, $"/avatars/{id}/impostor", null, ct);

    public async Task<List<string>> GetAvatarStylesAsync(CancellationToken ct = default)
    {
        var root = await SendAsync(HttpMethod.Get, "/avatarStyles", null, ct);

        return root.ValueKind != JsonValueKind.Array
            ? []
            : root.EnumerateArray()
                .Select(s => s.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : s.ToString())
                .Where(s => s.Length > 0)
                .ToList();
    }

    public async Task<string> UploadImageAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException("找不到要上传的图片。", filePath);

        using var form = new MultipartFormDataContent();
        var content = new StreamContent(File.OpenRead(filePath));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(GuessType(filePath));
        form.Add(content, "file", Path.GetFileName(filePath));

        var root = await SendFormAsync(form, "/file/image", ct);

        var id = root.TryGetProperty("id", out var i) ? i.GetString() : null;
        if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("VRChat 没有接受这张封面图。");

        return $"{ApiBase}/file/{id}/1/file";
    }

    private static string ContentCachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Akashic", "cache", "vrchat-content.json");

    public VRChatContentCache LoadContentCache()
    {
        try
        {
            if (File.Exists(ContentCachePath))
                return JsonSerializer.Deserialize<VRChatContentCache>(
                           File.ReadAllText(ContentCachePath), SerializerOptions) ?? new VRChatContentCache();
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }

        return new VRChatContentCache();
    }

    public async Task SaveContentCacheAsync(IReadOnlyList<VRChatContentItem> items, CancellationToken ct = default)
    {
        var path = ContentCachePath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var snapshot = new VRChatContentCache { SavedAt = DateTime.Now, Items = [.. items] };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(snapshot, SerializerOptions), ct);
    }

    private static string GuessType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "application/octet-stream"
    };

    private async Task<List<VRChatContentItem>> GetPagedAsync(string endpoint, VRChatContentKind kind,
        CancellationToken ct)
    {
        const int pageSize = 50;
        var collected = new List<VRChatContentItem>();

        for (var offset = 0; offset < 500; offset += pageSize)
        {
            var root = await SendAsync(HttpMethod.Get,
                $"{endpoint}?user=me&n={pageSize}&offset={offset}&sort=updated&order=descending&releaseStatus=all",
                null, ct);

            var page = JsonSerializer.Deserialize<List<VRChatContentItem>>(root.GetRawText(), SerializerOptions)
                       ?? [];

            foreach (var item in page)
            {
                item.Kind = kind;
                collected.Add(item);
            }

            if (page.Count < pageSize) break;
        }

        return collected;
    }

    private static string Endpoint(VRChatContentKind kind) =>
        kind == VRChatContentKind.Avatar ? "avatars" : "worlds";

    private async Task<JsonElement> SendAsync(HttpMethod method, string relative,
        Dictionary<string, object?>? body, CancellationToken ct)
    {
        var (root, error) = await SendRawAsync(method, relative, body, ct);

        if (error is { } message) throw new InvalidOperationException(message);
        if (root is null) throw new InvalidOperationException($"VRChat 接口 {relative} 没有返回内容。");

        return root.Value;
    }

    private async Task<JsonElement> SendFormAsync(HttpContent form, string relative, CancellationToken ct)
    {
        var (root, error) = await SendRawAsync(HttpMethod.Post, relative, null, ct, form);

        if (error is { } message) throw new InvalidOperationException(message);
        return root ?? JsonDocument.Parse("{}").RootElement;
    }

    private async Task<(JsonElement? Root, string? Error)> SendRawAsync(HttpMethod method, string relative,
        Dictionary<string, object?>? body, CancellationToken ct, HttpContent? rawBody = null)
    {
        if (!HasSession) return (null, "还没有登录 VRChat 账号。");

        using var request = new HttpRequestMessage(method, $"{ApiBase}{relative}");

        if (rawBody is not null) request.Content = rawBody;
        else if (body is not null) request.Content = JsonContent.Create(body, options: SerializerOptions);

        request.Headers.TryAddWithoutValidation("cookie", CookieHeader());

        using var response = await _client.SendAsync(request, ct);
        var payload = await response.Content.ReadAsStringAsync(ct);
        var status = (int)response.StatusCode;

        if (status is 401 or 403)
            return (null, DescribeError(payload) ?? "VRChat 拒绝了这次请求，请重新登录后再试。");

        if (status == 404)
            return (null, DescribeError(payload) ?? "VRChat 没有找到这条内容。");

        if (!response.IsSuccessStatusCode)
            return (null, DescribeError(payload) ?? $"VRChat 接口返回 {status}。");

        if (string.IsNullOrWhiteSpace(payload)) return (JsonDocument.Parse("true").RootElement, null);

        try
        {
            return (JsonDocument.Parse(payload).RootElement, null);
        }
        catch (JsonException)
        {
            return (null, "VRChat 返回的不是 JSON，可能被登录页拦截，请在面板中重新登录。");
        }
    }

    private static VRChatContentItem? Read(JsonElement root, VRChatContentKind kind)
    {
        var item = root.Deserialize<VRChatContentItem>(SerializerOptions);
        if (item is null) return null;

        item.Kind = kind;
        return item;
    }

    private static string? DescribeError(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;

        try
        {
            var root = JsonDocument.Parse(payload).RootElement;
            if (!root.TryGetProperty("error", out var error)) return null;

            return error.ValueKind switch
            {
                JsonValueKind.String => error.GetString(),
                JsonValueKind.Object when error.TryGetProperty("message", out var m) => m.GetString(),
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string CookieHeader() =>
        string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}"));
}
