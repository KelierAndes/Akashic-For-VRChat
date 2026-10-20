using System.Text.Json;
using System.Text.RegularExpressions;
using Akashic.Core.Models;

namespace Akashic.Core.Services;

public interface IResourceCatalogService
{
    Task<List<ResourceRow>> GetRowsAsync(CancellationToken ct = default);

    Task<ResourceMatchStatus> StartMatchAsync(bool force = false);

    ResourceMatchStatus GetMatchStatus();

    Task<List<ResourceRow>> SaveMetaAsync(ResourceMetaUpdate update, CancellationToken ct = default);

    Task<List<ResourceRow>> ResetMetaAsync(string key, CancellationToken ct = default);
}

public sealed partial class ResourceCatalogService : IResourceCatalogService
{
    private const double AcceptScore = 0.62;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly IUnityPackageLibraryService _library;
    private readonly IBoothService _booth;
    private readonly IAssetStoreService _assetStore;
    private readonly IUnityPackageIconService _icons;
    private readonly string _metaPath;

    private readonly object _gate = new();
    private readonly object _saveGate = new();

    private ResourceMetaCache? _meta;
    private ResourceMatchStatus _status = new();
    private CancellationTokenSource? _running;

    public ResourceCatalogService(IUnityPackageLibraryService library, IBoothService booth,
        IAssetStoreService assetStore, IUnityPackageIconService icons, string? settingsPath = null)
    {
        _library = library;
        _booth = booth;
        _assetStore = assetStore;
        _icons = icons;

        var settings = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Akashic", "settings.json");

        _metaPath = Path.Combine(Path.GetDirectoryName(settings) ?? ".", "cache", "resource-meta.json");
    }

    private ResourceMetaCache Meta
    {
        get
        {
            if (_meta is not null) return _meta;

            try
            {
                if (File.Exists(_metaPath))
                    _meta = JsonSerializer.Deserialize<ResourceMetaCache>(
                        File.ReadAllText(_metaPath), JsonOptions);
            }
            catch (JsonException)
            {
            }

            return _meta ??= new ResourceMetaCache();
        }
    }

    public async Task<List<ResourceRow>> GetRowsAsync(CancellationToken ct = default)
    {
        var rows = await _library.ScanResourcesAsync(ct);
        var purchases = _booth.LoadLibraryCache().Items;
        var accountItems = _assetStore.LoadAccountCache().Items;
        var changed = false;

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();

            row.MetaKey = MetaKeyOf(row);

            var entry = GetMeta(row.MetaKey);
            if (entry is null)
            {
                entry = LinkOffline(row, purchases, accountItems);
                if (entry is not null)
                {
                    PutMeta(row.MetaKey, entry);
                    changed = true;
                }
            }

            Apply(row, entry);

            if (row.CoverUrl.Length == 0)
            {
                var target = row.Kind == "archive"
                    ? row.Children.FirstOrDefault(child => child.FilePath != row.FilePath)?.FilePath
                    : row.Kind == "unitypackage"
                        ? row.FilePath
                        : null;

                if (target is not null)
                    row.CoverUrl = await LocalThumbnailAsync(target, ct);
            }

            foreach (var child in row.Children)
            {
                child.MetaKey = row.MetaKey;
                if (child.CoverUrl.Length == 0) child.CoverUrl = row.CoverUrl;
                if (child.Category.Length == 0) child.Category = row.Category;
                if (child.Tags.Count == 0) child.Tags = row.Tags;
            }
        }

        if (changed)
        {
            SaveMeta();
            await SyncBoothCategoriesAsync(rows);
        }

        return rows;
    }

    private async Task SyncBoothCategoriesAsync(List<ResourceRow> rows)
    {
        var cache = _booth.LoadLibraryCache();
        var purchases = cache.Items
            .ToDictionary(item => item.Number, StringComparer.OrdinalIgnoreCase);

        var updated = false;

        foreach (var row in rows)
        {
            if (row.MatchSource != "Booth" || row.Category.Length == 0) continue;
            if (!purchases.TryGetValue(row.BoothId, out var purchase)) continue;
            if (purchase.Category == row.Category) continue;

            purchase.Category = row.Category;
            updated = true;
        }

        if (updated) await _booth.SaveLibraryCacheAsync([.. cache.Items]);
    }

    private ResourceMetaEntry? LinkOffline(ResourceRow row,
        IReadOnlyList<BoothPurchaseItem> purchases, IReadOnlyList<AssetStoreAsset> accountItems)
    {
        var key = row.MetaKey;
        if (key.Length == 0) return null;

        foreach (var purchase in purchases)
        {
            var label = PackageCategory.Normalize(
                Path.GetFileNameWithoutExtension(purchase.FormLabel));
            var title = PackageCategory.Normalize(purchase.DisplayTitle);

            if (!NameMatches(key, label) && !NameMatches(key, title)) continue;

            return new ResourceMetaEntry
            {
                BoothId = purchase.Number,
                BoothTitle = purchase.DisplayTitle,
                BoothShop = purchase.ShopHint,
                CoverUrl = purchase.ImageUrl,
                SourceUrl = purchase.ItemUrl,
                Category = purchase.Category.Length > 0
                    ? purchase.Category
                    : PackageCategory.Guess(purchase.DisplayTitle),
                MatchedAt = DateTime.Now
            };
        }

        foreach (var asset in accountItems)
        {
            if (!NameMatches(key, PackageCategory.Normalize(asset.Name))) continue;

            return new ResourceMetaEntry
            {
                AssetStoreName = asset.Name,
                CoverUrl = asset.ImageUrl,
                Category = PackageCategory.Guess(asset.Name),
                MatchedAt = DateTime.Now
            };
        }

        if (_assetStore.TryGetCoverUrl(row.Name, out var cover))
        {
            return new ResourceMetaEntry
            {
                AssetStoreName = row.Name,
                CoverUrl = cover,
                Category = PackageCategory.Guess(row.Name),
                MatchedAt = DateTime.Now
            };
        }

        return null;
    }

    private static bool NameMatches(string key, string candidate)
    {
        if (candidate.Length == 0) return false;
        if (string.Equals(key, candidate, StringComparison.Ordinal)) return true;

        var shorter = Math.Min(key.Length, candidate.Length);
        if (shorter < 6) return false;

        return key.Contains(candidate, StringComparison.Ordinal) ||
               candidate.Contains(key, StringComparison.Ordinal);
    }

    private static void Apply(ResourceRow row, ResourceMetaEntry? entry)
    {
        row.Category = entry is { Category.Length: > 0 }
            ? entry.Category
            : PackageCategory.Guess(row.Name);

        if (entry is null) return;

        row.Tags = [.. entry.Tags];
        row.CoverUrl = entry.CoverUrl;
        row.SourceUrl = entry.SourceUrl;
        row.BoothId = entry.BoothId;
        row.BoothShop = entry.BoothShop;
        row.IsManual = entry.IsEdited;
        row.Attempted = entry.AttemptedAt is not null;

        row.MatchSource = entry.BoothId.Length > 0 ? "Booth"
            : entry.AssetStoreName.Length > 0 ? "AssetStore"
            : string.Empty;

        row.Matched = row.MatchSource.Length > 0;
    }

    private async Task<string> LocalThumbnailAsync(string path, CancellationToken ct)
    {
        try
        {
            return await _icons.GetThumbnailAsync(path, ct) ?? string.Empty;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    public Task<ResourceMatchStatus> StartMatchAsync(bool force = false)
    {
        CancellationTokenSource cts;

        lock (_gate)
        {
            if (_running is not null) return Task.FromResult(_status);

            cts = new CancellationTokenSource();
            _running = cts;
            _status = new ResourceMatchStatus { Running = true, Message = "准备中…" };
        }

        _ = Task.Run(() => RunMatchAsync(force, cts.Token));
        return Task.FromResult(GetMatchStatus());
    }

    public ResourceMatchStatus GetMatchStatus()
    {
        lock (_gate)
        {
            return new ResourceMatchStatus
            {
                Running = _status.Running,
                Total = _status.Total,
                Scanned = _status.Scanned,
                Matched = _status.Matched,
                Failed = _status.Failed,
                Current = _status.Current,
                Message = _status.Message,
                Cancelled = _status.Cancelled
            };
        }
    }

    private async Task RunMatchAsync(bool force, CancellationToken ct)
    {
        try
        {
            var rows = await GetRowsAsync(ct);
            var targets = rows
                .Where(row => row.Kind != "child" && (force || (!row.Matched && !row.Attempted)))
                .ToList();

            lock (_gate)
            {
                _status.Total = targets.Count;
                _status.Message = "正在匹配…";
            }

            foreach (var row in targets)
            {
                ct.ThrowIfCancellationRequested();

                lock (_gate) _status.Current = row.Name;

                try
                {
                    var candidate = await ResolveAsync(row, ct);

                    if (candidate is null)
                    {
                        MarkAttempt(row.MetaKey);
                        SaveMeta();
                        lock (_gate) _status.Failed++;
                    }
                    else
                    {
                        PutMeta(row.MetaKey, FromCandidate(candidate));
                        SaveMeta();

                        lock (_gate) _status.Matched++;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    lock (_gate) _status.Failed++;
                }

                lock (_gate) _status.Scanned++;

                await Task.Delay(200, ct);
            }

            if (_status.Matched > 0) rows = await GetRowsAsync(ct);
            await SyncBoothCategoriesAsync(rows);

            lock (_gate)
            {
                _status.Message = _status.Failed > 0
                    ? $"已匹配 {_status.Matched} 项 · {_status.Failed} 项没有找到对应商品"
                    : $"已匹配 {_status.Matched} 项";
            }
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                _status.Cancelled = true;
                _status.Message = "匹配已取消";
            }
        }
        catch (Exception error)
        {
            lock (_gate) _status.Message = "匹配失败：" + error.Message;
        }
        finally
        {
            lock (_gate)
            {
                _status.Running = false;
                _status.Current = string.Empty;
                _running?.Dispose();
                _running = null;
            }
        }
    }

    private async Task<BoothMatchCandidate?> ResolveAsync(ResourceRow row, CancellationToken ct)
    {
        var explicitId = ItemIdRegex().Match(row.Name);
        if (explicitId.Success)
        {
            var item = await _booth.GetItemAsync(explicitId.Groups[1].Value, ct);
            if (item is not null)
            {
                item.Score = 1;
                return item;
            }
        }

        var query = row.Name.Replace('_', ' ').Trim();
        if (query.Length < 3) return null;

        var candidates = await _booth.SearchAsync(query, ct);
        if (candidates.Count == 0) return null;

        BoothMatchCandidate? best = null;

        foreach (var candidate in candidates)
        {
            candidate.Score = PackageCategory.Score(row.Name, candidate.Title, candidate.Shop);
            if (best is null || candidate.Score > best.Score) best = candidate;
        }

        if (best is not null && best.Score >= AcceptScore) return best;

        var distinct = candidates
            .Select(candidate => candidate.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinct.Count == 1)
        {
            var only = candidates.First(candidate =>
                string.Equals(candidate.Id, distinct[0], StringComparison.OrdinalIgnoreCase));
            only.Score = Math.Max(only.Score, 0.6);
            return only;
        }

        var queryTokens = PackageCategory.QueryTokens(row.Name);
        if (queryTokens.Count > 0)
        {
            var ranked = distinct
                .Select(id => candidates.First(candidate =>
                    string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase)))
                .Select(candidate => (
                    Candidate: candidate,
                    Hits: queryTokens.Intersect(PackageCategory.Hashtags(candidate.Title)).Count()))
                .OrderByDescending(item => item.Hits)
                .ToList();

            if (ranked[0].Hits > 0 && ranked.Skip(1).All(item => item.Hits < ranked[0].Hits))
            {
                ranked[0].Candidate.Score = Math.Max(ranked[0].Candidate.Score, 0.75);
                return ranked[0].Candidate;
            }
        }

        return null;
    }

    private static ResourceMetaEntry FromCandidate(BoothMatchCandidate candidate) => new()
    {
        BoothId = candidate.Id,
        BoothTitle = candidate.Title,
        BoothShop = candidate.Shop,
        CoverUrl = candidate.CoverUrl,
        SourceUrl = $"https://booth.pm/ja/items/{candidate.Id}",
        Category = candidate.Category.Length > 0
            ? candidate.Category
            : PackageCategory.Guess(candidate.Title),
        MatchedAt = DateTime.Now,
        AttemptedAt = DateTime.Now
    };

    private void MarkAttempt(string key)
    {
        if (key.Length == 0) return;

        var existing = GetMeta(key);
        if (existing is null)
        {
            PutMeta(key, new ResourceMetaEntry { AttemptedAt = DateTime.Now });
            return;
        }

        PutMeta(key, new ResourceMetaEntry
        {
            Category = existing.Category,
            Tags = existing.Tags,
            SourceUrl = existing.SourceUrl,
            CoverUrl = existing.CoverUrl,
            BoothId = existing.BoothId,
            BoothTitle = existing.BoothTitle,
            BoothShop = existing.BoothShop,
            AssetStoreName = existing.AssetStoreName,
            MatchedAt = existing.MatchedAt,
            EditedAt = existing.EditedAt,
            AttemptedAt = DateTime.Now
        });
    }

    public async Task<List<ResourceRow>> SaveMetaAsync(ResourceMetaUpdate update, CancellationToken ct = default)
    {
        var key = PackageCategory.Normalize(update.Key);
        if (key.Length == 0) return await GetRowsAsync(ct);

        var existing = GetMeta(key) ?? new ResourceMetaEntry();

        var entry = new ResourceMetaEntry
        {
            Category = update.Category ?? existing.Category,
            Tags = update.Tags ?? existing.Tags,
            SourceUrl = update.SourceUrl ?? existing.SourceUrl,
            CoverUrl = update.CoverUrl ?? existing.CoverUrl,
            BoothId = existing.BoothId,
            BoothTitle = existing.BoothTitle,
            BoothShop = existing.BoothShop,
            AssetStoreName = existing.AssetStoreName,
            MatchedAt = existing.MatchedAt,
            AttemptedAt = existing.AttemptedAt,
            EditedAt = DateTime.Now
        };

        PutMeta(key, entry);
        SaveMeta();

        return await GetRowsAsync(ct);
    }

    public async Task<List<ResourceRow>> ResetMetaAsync(string key, CancellationToken ct = default)
    {
        var normalized = PackageCategory.Normalize(key);

        if (normalized.Length > 0)
        {
            lock (_gate) Meta.Entries.Remove(normalized);
            SaveMeta();
        }

        return await GetRowsAsync(ct);
    }

    private ResourceMetaEntry? GetMeta(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        lock (_gate) return Meta.Entries.TryGetValue(key, out var entry) ? entry : null;
    }

    private void PutMeta(string key, ResourceMetaEntry entry)
    {
        if (string.IsNullOrEmpty(key)) return;
        lock (_gate) Meta.Entries[key] = entry;
    }

    private void SaveMeta()
    {
        Dictionary<string, ResourceMetaEntry> copy;

        lock (_gate)
        {
            copy = new Dictionary<string, ResourceMetaEntry>(Meta.Entries, StringComparer.OrdinalIgnoreCase);
        }

        var snapshot = new ResourceMetaCache { SavedAt = DateTime.Now, Entries = copy };

        try
        {
            var directory = Path.GetDirectoryName(_metaPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            lock (_saveGate)
                File.WriteAllText(_metaPath, JsonSerializer.Serialize(snapshot, JsonOptions));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string MetaKeyOf(ResourceRow row)
    {
        var key = PackageCategory.Normalize(row.Name);
        return key.Length > 0
            ? key
            : PackageCategory.Normalize(Path.GetFileNameWithoutExtension(row.FilePath));
    }

    [GeneratedRegex(@"(?:items/)?(\d{5,})")]
    private static partial Regex ItemIdRegex();
}
