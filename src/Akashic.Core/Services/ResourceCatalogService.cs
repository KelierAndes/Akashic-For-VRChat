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

    List<PendingMatch> GetPendingMatches();

    Task<List<ResourceRow>> SaveMetaAsync(ResourceMetaUpdate update, CancellationToken ct = default);

    Task<List<ResourceRow>> ConfirmPendingMatchAsync(PendingMatchRequest request, CancellationToken ct = default);

    Task<List<ResourceRow>> DismissPendingMatchAsync(string key, CancellationToken ct = default);

    Task<List<ResourceRow>> ResetMetaAsync(string key, CancellationToken ct = default);
}

public sealed partial class ResourceCatalogService : IResourceCatalogService
{
    private const double AcceptScore = 0.62;
    private const double SegmentAcceptScore = 0.8;
    private const int MaxSuggestions = 3;

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
    private Dictionary<string, PendingMatch>? _pending;
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

    private Dictionary<string, PendingMatch> Pending
    {
        get
        {
            if (_pending is not null) return _pending;

            _pending = _db.LoadPendingMatches();
            return _pending;
        }
    }

    public List<PendingMatch> GetPendingMatches()
    {
        lock (_gate) return Pending.Values.OrderByDescending(item => item.CreatedAt).ToList();
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
                child.ManualMatch = row.ManualMatch;
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
            row.ManualMatch = matched.ManualMatch;
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

        var names = NameVariants(row.Name.Length > 0
            ? row.Name
            : Path.GetFileNameWithoutExtension(row.FilePath));

        foreach (var purchase in purchases)
        {
            var label = Path.GetFileNameWithoutExtension(purchase.FormLabel);
            var title = purchase.DisplayTitle;

            if (!AnyNameMatches(names, label) && !AnyNameMatches(names, title)) continue;

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
            if (!AnyNameMatches(names, asset.Name)) continue;

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

    private static List<string> NameVariants(string name)
    {
        var variants = new List<string>();

        void Add(string? raw)
        {
            var normalized = PackageCategory.Normalize(raw);
            if (normalized.Length > 0 && !variants.Contains(normalized, StringComparer.Ordinal))
                variants.Add(normalized);
        }

        Add(name);
        Add(PackageCategory.LeadingSegment(name));
        return variants;
    }

    private static bool AnyNameMatches(IReadOnlyList<string> variants, string? candidate) =>
        variants.Any(variant => NameMatches(variant, PackageCategory.Normalize(candidate)));

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
        row.ManualMatch = entry.ManualMatch;
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
                Pending = _status.Pending,
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

            var pendingContainers = PendingContainerKeys();

            var leaves = rows
                .SelectMany(row => row.IsContainer ? (row.Matched ? [] : row.Children) : [row])
                .Where(row => row.IsMatchable)
                .Where(row => !IsDependencyPackage(row))
                .Where(row => row.ContainerPath.Length == 0 || !pendingContainers.Contains(row.ContainerPath))
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
                var parts = new List<string> { $"已匹配 {_status.Matched} 项" };
                if (_status.Pending > 0) parts.Add($"{_status.Pending} 项待确认");
                if (_status.Failed > 0) parts.Add($"{_status.Failed} 项没有找到对应商品");
                _status.Message = string.Join(" · ", parts);
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
                var resolution = await ResolveAsync(row, ct);

                if (resolution.Candidate is not null)
                {
                    PutMeta(row.MetaKey, FromCandidate(resolution.Candidate));
                    if (RemovePending(row.MetaKey)) SavePending();
                    SaveMeta();

                    lock (_gate) _status.Matched++;
                }
                else if (resolution.Suggestions.Count > 0)
                {
                    PutPending(row.MetaKey, row.Name, row.IsContainer ? row.Key : string.Empty,
                        resolution.Suggestions);
                    MarkAttempt(row.MetaKey);
                    SaveMeta();
                    SavePending();

                    lock (_gate) _status.Pending++;
                }
                else
                {
                    if (RemovePending(row.MetaKey)) SavePending();
                    MarkAttempt(row.MetaKey);
                    SaveMeta();
                    lock (_gate) _status.Failed++;
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

    private sealed record MatchResolution(BoothMatchCandidate? Candidate, List<BoothMatchCandidate> Suggestions);

    private async Task<MatchResolution> ResolveAsync(ResourceRow row, CancellationToken ct)
    {
        var explicitId = ItemIdRegex().Match(row.Name);
        if (explicitId.Success)
        {
            var item = await _booth.GetItemAsync(explicitId.Groups[1].Value, ct);
            if (item is not null)
            {
                item.Score = 1;
                return new MatchResolution(item, []);
            }
        }

        var queries = new List<string>();
        var full = row.Name.Replace('_', ' ').Trim();
        if (full.Length >= 3) queries.Add(full);

        var head = PackageCategory.LeadingSegment(row.Name);
        if (head is not null && !string.Equals(head, full, StringComparison.OrdinalIgnoreCase))
            queries.Add(head);

        if (queries.Count == 0) return new MatchResolution(null, []);

        var tokens = PackageCategory.QueryTokens(row.Name);
        var pool = new Dictionary<string, BoothMatchCandidate>(StringComparer.OrdinalIgnoreCase);

        foreach (var query in queries)
        {
            ct.ThrowIfCancellationRequested();

            var strict = !string.Equals(query, full, StringComparison.OrdinalIgnoreCase);

            var candidates = await _booth.SearchAsync(query, ct);
            var picked = PickBest(candidates, query, strict);
            if (picked is not null) return new MatchResolution(picked, []);

            CollectSuggestions(candidates, query, tokens, pool);

            var tagged = await _booth.SearchAsync(query, ct, "VRChat");
            picked = PickBest(tagged, query, strict);
            if (picked is not null) return new MatchResolution(picked, []);

            CollectSuggestions(tagged, query, tokens, pool);
        }

        var suggestions = pool.Values
            .Select(candidate => (
                Candidate: candidate,
                Hits: tokens.Count(token => PackageCategory.ContainsKeyword(candidate.Title, token))))
            .OrderByDescending(item => item.Hits)
            .ThenByDescending(item => item.Candidate.Score)
            .ThenBy(item => item.Candidate.Title, StringComparer.OrdinalIgnoreCase)
            .Take(MaxSuggestions)
            .Select(item => item.Candidate)
            .ToList();

        return new MatchResolution(null, suggestions);
    }

    private static BoothMatchCandidate? PickBest(IReadOnlyList<BoothMatchCandidate> candidates, string query,
        bool strict)
    {
        if (candidates.Count == 0) return null;

        BoothMatchCandidate? best = null;

        foreach (var candidate in candidates)
        {
            candidate.Score = PackageCategory.Score(query, candidate.Title, candidate.Shop);
            if (best is null || candidate.Score > best.Score) best = candidate;
        }

        if (strict)
        {
            if (best is not null && best.Score >= SegmentAcceptScore) return best;

            if (candidates.Count > 0 && PackageCategory.IsDistinctive(query))
            {
                candidates[0].Score = Math.Max(candidates[0].Score, 0.7);
                return candidates[0];
            }

            return null;
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

        var queryTokens = PackageCategory.QueryTokens(query);
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

    private static void CollectSuggestions(IEnumerable<BoothMatchCandidate> candidates, string query,
        IReadOnlySet<string> tokens, Dictionary<string, BoothMatchCandidate> pool)
    {
        if (tokens.Count == 0) return;

        foreach (var candidate in candidates)
        {
            if (candidate.Id.Length == 0 || candidate.Title.Length == 0) continue;
            if (!tokens.Any(token => PackageCategory.ContainsKeyword(candidate.Title, token))) continue;

            candidate.Score = PackageCategory.Score(query, candidate.Title, candidate.Shop);

            if (pool.TryGetValue(candidate.Id, out var existing))
            {
                if (candidate.Score > existing.Score) pool[candidate.Id] = candidate;
                continue;
            }

            pool[candidate.Id] = candidate;
        }
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
            ManualMatch = existing.ManualMatch,
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

        var sourceUrl = string.IsNullOrWhiteSpace(update.SourceUrl)
            ? existing.SourceUrl
            : update.SourceUrl.Trim();

        var boothId = BoothIdFromUrl(sourceUrl);
        var boothChanged = boothId.Length > 0 && boothId != existing.BoothId;

        BoothMatchCandidate? fetched = null;

        if (boothChanged)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                fetched = await _booth.GetItemAsync(boothId, timeout.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
            }
        }

        var entry = new ResourceMetaEntry
        {
            Category = Pick(update.Category, fetched?.Category, existing.Category),
            Tags = boothChanged && fetched is { Tags.Count: > 0 }
                ? fetched.Tags
                : update.Tags is { Count: > 0 }
                    ? update.Tags
                    : fetched is { Tags.Count: > 0 }
                        ? fetched.Tags
                        : existing.Tags,
            SourceUrl = sourceUrl,
            CoverUrl = boothChanged && fetched is { CoverUrl.Length: > 0 }
                ? fetched.CoverUrl
                : Pick(update.CoverUrl, fetched?.CoverUrl, existing.CoverUrl),
            BoothId = boothId.Length > 0 ? boothId : existing.BoothId,
            BoothTitle = fetched?.Title ?? existing.BoothTitle,
            BoothShop = fetched?.Shop ?? existing.BoothShop,
            AssetStoreName = existing.AssetStoreName,
            ManualMatch = boothChanged || existing.ManualMatch,
            MatchedAt = boothId.Length > 0 ? DateTime.Now : existing.MatchedAt,
            AttemptedAt = existing.AttemptedAt ?? (boothId.Length > 0 ? DateTime.Now : null),
            EditedAt = DateTime.Now
        };

        PutMeta(key, entry);
        if (RemovePending(key)) SavePending();
        SaveMeta();

        return await GetRowsAsync(ct);
    }

    public async Task<List<ResourceRow>> ConfirmPendingMatchAsync(PendingMatchRequest request,
        CancellationToken ct = default)
    {
        var key = PackageCategory.Normalize(request.Key);
        PendingMatch? pending = null;

        if (key.Length > 0) lock (_gate) Pending.TryGetValue(key, out pending);

        if (pending is not null)
        {
            var candidate = pending.Candidates.FirstOrDefault(item =>
                                 string.Equals(item.Id, request.BoothId, StringComparison.OrdinalIgnoreCase))
                             ?? pending.Candidates.FirstOrDefault();

            if (candidate is not null && candidate.Id.Length > 0)
            {
                var entry = FromCandidate(candidate);
                entry.EditedAt = DateTime.Now;
                entry.ManualMatch = true;
                PutMeta(pending.MetaKey, entry);
            }

            if (RemovePending(pending.MetaKey)) SavePending();
            SaveMeta();
        }

        return await GetRowsAsync(ct);
    }

    public async Task<List<ResourceRow>> DismissPendingMatchAsync(string key, CancellationToken ct = default)
    {
        var normalized = PackageCategory.Normalize(key);
        if (normalized.Length > 0 && RemovePending(normalized)) SavePending();

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

            if (RemovePending(normalized)) SavePending();
        }

        return await GetRowsAsync(ct);
    }

    private static string Pick(string? first, string? second, string? third)
    {
        if (!string.IsNullOrWhiteSpace(first)) return first.Trim();
        if (!string.IsNullOrWhiteSpace(second)) return second.Trim();
        return third ?? string.Empty;
    }

    private static string BoothIdFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        if (!url.Contains("booth.pm", StringComparison.OrdinalIgnoreCase)) return string.Empty;

        var match = BoothItemUrlRegex().Match(url);
        return match.Success ? match.Groups[1].Value : string.Empty;
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

    private void PutPending(string key, string name, string containerKey,
        IReadOnlyList<BoothMatchCandidate> candidates)
    {
        if (string.IsNullOrEmpty(key)) return;

        lock (_gate)
        {
            Pending[key] = new PendingMatch
            {
                MetaKey = key,
                Name = name,
                ContainerKey = containerKey,
                CreatedAt = DateTime.Now,
                Candidates = [.. candidates]
            };
        }
    }

    private bool RemovePending(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        lock (_gate) return Pending.Remove(key);
    }

    private HashSet<string> PendingContainerKeys()
    {
        lock (_gate)
        {
            return Pending.Values
                .Where(item => item.ContainerKey.Length > 0)
                .Select(item => item.ContainerKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void SavePending()
    {
        Dictionary<string, PendingMatch> copy;

        lock (_gate)
        {
            copy = new Dictionary<string, PendingMatch>(Pending, StringComparer.OrdinalIgnoreCase);
        }

        lock (_saveGate) _db.ReplacePendingMatches(copy);
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

    [GeneratedRegex(@"items/(\d{4,})")]
    private static partial Regex BoothItemUrlRegex();
}
