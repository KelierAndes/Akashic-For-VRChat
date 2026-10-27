using System.Text.Json;
using System.Text.RegularExpressions;
using Akashic.Core.Data;
using Akashic.Core.Models;

namespace Akashic.Core.Services;

public interface IResourceCatalogService
{
    Task<List<ResourceRow>> GetRowsAsync(CancellationToken ct = default);

    Task<ResourceMatchStatus> StartMatchAsync(bool force = false);

    ResourceMatchStatus GetMatchStatus();

    void ReloadMeta();

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
    private readonly AkashicDatabase _db;

    private readonly object _gate = new();
    private readonly object _saveGate = new();

    private ResourceMetaCache? _meta;
    private ResourceMatchStatus _status = new();
    private CancellationTokenSource? _running;

    public ResourceCatalogService(IUnityPackageLibraryService library, IBoothService booth,
        IAssetStoreService assetStore, IUnityPackageIconService icons, string? settingsPath = null,
        AkashicDatabase? database = null)
    {
        _library = library;
        _booth = booth;
        _assetStore = assetStore;
        _icons = icons;

        var settings = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Akashic", "settings.json");

        _db = database ?? new AkashicDatabase(Path.GetDirectoryName(settings));
    }

    public void ReloadMeta()
    {
        lock (_gate) _meta = null;
    }

    private ResourceMetaCache Meta
    {
        get
        {
            if (_meta is not null) return _meta;

            var entries = _db.LoadResourceMeta();
            _meta = new ResourceMetaCache { SavedAt = DateTime.Now, Entries = entries };
            return _meta;
        }
    }

    public async Task<List<ResourceRow>> GetRowsAsync(CancellationToken ct = default)
    {
        var rows = await _library.ScanResourcesAsync(ct);
        var purchases = _booth.LoadLibraryCache().Items;
        var accountItems = _assetStore.LoadAccountCache().Items;
        var changed = false;

        var flat = new List<ResourceRow>();

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();

            row.MetaKey = MetaKeyOf(row);
            if (LinkRow(row, purchases, accountItems)) changed = true;
            Apply(row, GetMeta(row.MetaKey));

            flat.Add(row);

            foreach (var child in row.Children)
            {
                child.MetaKey = MetaKeyOf(child);
                if (LinkRow(child, purchases, accountItems)) changed = true;
                Apply(child, GetMeta(child.MetaKey));
                flat.Add(child);
            }

            InheritFromContainer(row);
        }

        foreach (var row in flat)
        {
            if (row.CoverUrl.Length > 0) continue;

            var target = PackageThumbnailOf(row);
            if (target is not null) row.CoverUrl = await LocalThumbnailAsync(target, ct);
        }

        foreach (var row in rows.Where(row => row.IsContainer))
            InheritFromChildren(row);

        if (changed)
        {
            SaveMeta();
            await SyncBoothCategoriesAsync(flat);
        }

        _db.ReplaceResources(flat);
        return rows;
    }

    private bool LinkRow(ResourceRow row, IReadOnlyList<BoothPurchaseItem> purchases,
        IReadOnlyList<AssetStoreAsset> accountItems)
    {
        if (GetMeta(row.MetaKey) is not null || row.MetaKey.Length == 0) return false;

        var entry = LinkOffline(row, purchases, accountItems);
        if (entry is null) return false;

        PutMeta(row.MetaKey, entry);
        return true;
    }

    private static string? PackageThumbnailOf(ResourceRow row)
    {
        if (row.Kind == "unitypackage")
            return row.ArchiveEntry.Length == 0 && File.Exists(row.FilePath) ? row.FilePath : null;

        var child = row.Children.FirstOrDefault(item => item.CoverUrl.Length == 0);
        return child is null ? null : PackageThumbnailOf(child);
    }

    private static void InheritFromContainer(ResourceRow row)
    {
        if (row.Children.Count == 0) return;

        foreach (var child in row.Children)
        {
            if (row.Matched && !child.Matched)
            {
                child.Matched = true;
                child.MatchSource = row.MatchSource;
                child.BoothId = row.BoothId;
                child.BoothShop = row.BoothShop;
                child.Attempted = true;
            }

            if (row.CoverUrl.Length > 0 && child.CoverUrl.Length == 0) child.CoverUrl = row.CoverUrl;
            if (row.Category.Length > 0 && child.Category.Length == 0) child.Category = row.Category;
            if (row.Tags.Count > 0 && child.Tags.Count == 0) child.Tags = row.Tags;
            if (row.SourceUrl.Length > 0 && child.SourceUrl.Length == 0) child.SourceUrl = row.SourceUrl;
        }
    }

    private static readonly string[] DependencyTokens =
    [
        "liltoon", "modularavatar", "vrchatsdk", "comvrchat", "vrcfury",
        "poiyomi", "animatorascode", "avataroptimizer", "textanimator",
        "vrchatutility"
    ];

    private static bool IsDependencyPackage(ResourceRow row)
    {
        var name = PackageCategory.Normalize(row.Name);
        if (name.Length == 0) return false;

        return DependencyTokens.Any(token =>
            name.Equals(token, StringComparison.Ordinal) ||
            name.StartsWith(token, StringComparison.Ordinal));
    }

    private static void InheritFromChildren(ResourceRow row)
    {
        var matched = row.Children.FirstOrDefault(child => child.Matched && !IsDependencyPackage(child)) ??
                      row.Children.FirstOrDefault(child => child.CoverUrl.Length > 0 && !IsDependencyPackage(child));

        if (matched is null) return;

        if (!row.Matched && matched.Matched)
        {
            row.Matched = true;
            row.MatchSource = matched.MatchSource;
            row.BoothId = matched.BoothId;
            row.BoothShop = matched.BoothShop;
            row.Attempted = true;
        }

        if (row.CoverUrl.Length == 0) row.CoverUrl = matched.CoverUrl;
        if (row.Category.Length == 0) row.Category = matched.Category;
        if (row.Tags.Count == 0) row.Tags = matched.Tags;
        if (row.SourceUrl.Length == 0) row.SourceUrl = matched.SourceUrl;
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

            var containers = rows
                .Where(row => row.IsContainer && row.CanMatch)
                .Where(row => !IsDependencyPackage(row))
                .Where(row => force || (!row.Matched && !row.Attempted))
                .ToList();

            lock (_gate)
            {
                _status.Total = containers.Count;
                _status.Message = containers.Count > 0 ? "正在匹配压缩包…" : "正在匹配…";
            }

            await MatchEach(containers, ct);

            rows = await GetRowsAsync(ct);

            var leaves = rows
                .SelectMany(row => row.IsContainer ? (row.Matched ? [] : row.Children) : [row])
                .Where(row => row.IsMatchable)
                .Where(row => !IsDependencyPackage(row))
                .Where(row => force || (!row.Matched && !row.Attempted))
                .ToList();

            lock (_gate)
            {
                _status.Total += leaves.Count;
                _status.Message = "正在匹配包内 .unitypackage…";
            }

            await MatchEach(leaves, ct);

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

    private async Task MatchEach(IReadOnlyList<ResourceRow> targets, CancellationToken ct)
    {
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
        var picked = PickBest(candidates, row.Name);
        if (picked is not null) return picked;

        var tagged = await _booth.SearchAsync(query, ct, "VRChat");
        return PickBest(tagged, row.Name);
    }

    private static BoothMatchCandidate? PickBest(IReadOnlyList<BoothMatchCandidate> candidates, string name)
    {
        if (candidates.Count == 0) return null;

        BoothMatchCandidate? best = null;

        foreach (var candidate in candidates)
        {
            candidate.Score = PackageCategory.Score(name, candidate.Title, candidate.Shop);
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

        var queryTokens = PackageCategory.QueryTokens(name);
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
            _db.DeleteResourceMeta(normalized);
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

        lock (_saveGate) _db.ReplaceResourceMeta(copy);
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
