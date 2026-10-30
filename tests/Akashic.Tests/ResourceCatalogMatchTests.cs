using System.Text.Json;
using Akashic.Core.Data;
using Akashic.Core.Models;
using Akashic.Core.Services;
using Xunit;

namespace Akashic.Tests;

public sealed class ResourceCatalogMatchTests
{
    private static readonly string[] JapaneseTitles =
    [
        "しなの専用 ロリ化Prefab & 表情16",
        "しなの専用衣装",
        "Shinano Hair",
        "しなの用 靴",
        "しなの対応 アクセサリー"
    ];

    [Fact]
    public async Task CompositeNameFallsBackToTheUnderscoreSegment()
    {
        using var workspace = new Workspace(
            Container("school elegan_shinano_v1.7", "zip-school"),
            new FakeBooth
            {
                Search = (query, _) => query.StartsWith("school elegan", StringComparison.Ordinal) &&
                                       !query.Contains("shinano", StringComparison.Ordinal)
                    ? [Card("999", "【12アバター対応】スクールエレガン"), Card("1000", "Elegance school uniform")]
                    : []
            });

        var status = await workspace.MatchAsync();

        Assert.Equal(1, status.Matched);
        Assert.Equal(0, status.Failed);

        var rows = await workspace.Catalog.GetRowsAsync();
        var row = rows.Single(item => item.Name == "school elegan_shinano_v1.7");
        Assert.True(row.Matched);
        Assert.Equal("999", row.BoothId);
        Assert.False(row.ManualMatch);
    }

    [Fact]
    public async Task AmbiguousSearchKeepsTopThreeCandidatesForConfirmation()
    {
        using var workspace = new Workspace(
            Container("Shinano_ver1.02", "zip-shinano"),
            new FakeBooth
            {
                Search = (query, _) => JapaneseTitles
                    .Select((title, index) => Card((1001 + index).ToString(), title))
                    .ToList()
            });

        var status = await workspace.MatchAsync();

        Assert.Equal(1, status.Pending);
        Assert.Equal(0, status.Matched);

        var pending = workspace.Catalog.GetPendingMatches();
        var entry = Assert.Single(pending);
        Assert.Equal("Shinano_ver1.02", entry.Name);
        Assert.Equal(3, entry.Candidates.Count);

        var chosen = entry.Candidates[0].Id;
        await workspace.Catalog.ConfirmPendingMatchAsync(new PendingMatchRequest(entry.MetaKey, chosen));

        Assert.Empty(workspace.Catalog.GetPendingMatches());

        var rows = await workspace.Catalog.GetRowsAsync();
        var row = rows.Single(item => item.Name == "Shinano_ver1.02");
        Assert.True(row.Matched);
        Assert.Equal(chosen, row.BoothId);
        Assert.True(row.ManualMatch);
    }

    [Fact]
    public async Task PendingConfirmationSurvivesARestart()
    {
        using var workspace = new Workspace(
            Container("Shinano_ver1.02", "zip-shinano"),
            new FakeBooth
            {
                Search = (_, _) => JapaneseTitles
                    .Select((title, index) => Card((1001 + index).ToString(), title))
                    .ToList()
            });

        await workspace.MatchAsync();
        Assert.Single(workspace.Catalog.GetPendingMatches());

        var reopened = new ResourceCatalogService(
            workspace.Library, workspace.Booth, workspace.AssetStore, workspace.Icons,
            workspace.SettingsPath, workspace.Database);

        var entry = Assert.Single(reopened.GetPendingMatches());
        Assert.Equal("Shinano_ver1.02", entry.Name);

        await reopened.DismissPendingMatchAsync(entry.MetaKey);
        Assert.Empty(reopened.GetPendingMatches());
    }

    [Fact]
    public async Task ManualBoothLinkExtractsCoverTagsAndMarksTheRowMatched()
    {
        var booth = new FakeBooth
        {
            Items =
            {
                ["7657840"] = new BoothMatchCandidate
                {
                    Id = "7657840",
                    Title = "もちふぃった～",
                    Shop = "Nine Gates",
                    ShopUrl = "https://yamirin.booth.pm/",
                    Category = "服饰",
                    CoverUrl = "https://example.test/cover.jpg",
                    Tags = ["衣装", "VRChat"]
                }
            }
        };

        using var workspace = new Workspace(
            new ResourceRow { Key = "pkg-someoutfit", Name = "SomeOutfit", FilePath = "pkg-someoutfit" },
            booth);

        var rows = await workspace.Catalog.SaveMetaAsync(new ResourceMetaUpdate(
            "SomeOutfit", string.Empty, [], "https://booth.pm/en/items/7657840", string.Empty));

        var row = Assert.Single(rows);
        Assert.True(row.Matched);
        Assert.True(row.ManualMatch);
        Assert.Equal("Booth", row.MatchSource);
        Assert.Equal("7657840", row.BoothId);
        Assert.Equal("服饰", row.Category);
        Assert.Equal("https://example.test/cover.jpg", row.CoverUrl);
        Assert.Contains("VRChat", row.Tags);
        Assert.Equal("https://booth.pm/en/items/7657840", row.SourceUrl);
    }

    [Fact]
    public async Task ManualMatchSurvivesTheGistSnapshotRoundTrip()
    {
        var booth = new FakeBooth
        {
            Items =
            {
                ["7657840"] = new BoothMatchCandidate
                {
                    Id = "7657840",
                    Title = "もちふぃった～",
                    Shop = "Nine Gates",
                    Category = "服饰",
                    CoverUrl = "https://example.test/cover.jpg",
                    Tags = ["衣装"]
                }
            }
        };

        using var workspace = new Workspace(
            new ResourceRow { Key = "pkg-someoutfit", Name = "SomeOutfit", FilePath = "pkg-someoutfit" },
            booth);

        await workspace.Catalog.SaveMetaAsync(new ResourceMetaUpdate(
            "SomeOutfit", string.Empty, [], "https://booth.pm/en/items/7657840", string.Empty));

        workspace.Database.ExportSnapshots();
        var snapshot = Path.Combine(workspace.Database.DataDirectory, "cache", "resource-meta.json");
        Assert.True(File.Exists(snapshot));

        workspace.Database.ReplaceResourceMeta(new Dictionary<string, ResourceMetaEntry>());
        Assert.False((await workspace.Reopen().GetRowsAsync()).Single().Matched);

        workspace.Database.ImportSnapshots();

        var restored = (await workspace.Reopen().GetRowsAsync()).Single();
        Assert.True(restored.Matched);
        Assert.True(restored.ManualMatch);
        Assert.Equal("7657840", restored.BoothId);
        Assert.Equal("服饰", restored.Category);
        Assert.Equal("https://example.test/cover.jpg", restored.CoverUrl);
    }

    private static BoothMatchCandidate Card(string id, string title) => new()
    {
        Id = id,
        Title = title,
        Shop = "Test Shop",
        CoverUrl = $"https://example.test/{id}.jpg"
    };

    private static ResourceRow Container(string name, string key) => new()
    {
        Key = key,
        Kind = "archive",
        Name = name,
        FilePath = key,
        Children =
        [
            new ResourceRow
            {
                Key = key + "!inner",
                Kind = "unitypackage",
                Name = key + " inner",
                FilePath = key,
                ArchiveEntry = "Assets/inner.unitypackage",
                ContainerPath = key
            }
        ]
    };

    private sealed class Workspace : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(),
            "akashic-match-test-" + Guid.NewGuid().ToString("N"));

        public Workspace(ResourceRow row, FakeBooth booth)
        {
            Directory.CreateDirectory(_directory);
            SettingsPath = Path.Combine(_directory, "settings.json");
            Library = new FakeLibrary(row);
            Booth = booth;
            AssetStore = new FakeAssetStore();
            Icons = new FakeIcons();
            Database = new AkashicDatabase(_directory);
            Catalog = new ResourceCatalogService(Library, Booth, AssetStore, Icons, SettingsPath, Database);
        }

        public string SettingsPath { get; }

        public FakeLibrary Library { get; }

        public FakeBooth Booth { get; }

        public FakeAssetStore AssetStore { get; }

        public FakeIcons Icons { get; }

        public AkashicDatabase Database { get; }

        public ResourceCatalogService Catalog { get; }

        /**
         * A second service over the same database. The metadata cache lives in the instance,
         * so reading through a fresh one is what "another machine after a gist restore" looks
         * like; reusing Catalog would prove nothing about what actually reached disk.
         */
        public ResourceCatalogService Reopen() =>
            new(Library, Booth, AssetStore, Icons, SettingsPath, Database);

        public async Task<ResourceMatchStatus> MatchAsync()
        {
            await Catalog.StartMatchAsync();

            for (var attempt = 0; attempt < 200 && Catalog.GetMatchStatus().Running; attempt++)
                await Task.Delay(50);

            var status = Catalog.GetMatchStatus();
            Assert.False(status.Running);
            return status;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed class FakeLibrary(ResourceRow row) : IUnityPackageLibraryService
    {
        // The real library re-scans from disk, so every read starts from a pristine row.
        // Handing back the same instance would let one GetRowsAsync leak its Matched/BoothId
        // into the next, which is exactly the state a snapshot round trip has to disprove.
        private readonly string _pristine = JsonSerializer.Serialize(row);

        public List<string> GetLibraryFolders() => [];

        public List<string> GetAddedFiles() => [];

        public Task AddLibraryFolderAsync(string folderPath, CancellationToken ct = default) => Task.CompletedTask;

        public Task RemoveLibraryFolderAsync(string folderPath, CancellationToken ct = default) => Task.CompletedTask;

        public Task AddPackageFileAsync(string filePath, PackageOrigin origin = PackageOrigin.Local,
            CancellationToken ct = default) => Task.CompletedTask;

        public Task RemovePackageFileAsync(string filePath, CancellationToken ct = default) => Task.CompletedTask;

        public Task<List<UnityPackageFileInfo>> ScanAsync(CancellationToken ct = default) =>
            Task.FromResult(new List<UnityPackageFileInfo>());

        public Task<List<ResourceRow>> ScanResourcesAsync(CancellationToken ct = default) =>
            Task.FromResult(new List<ResourceRow> { JsonSerializer.Deserialize<ResourceRow>(_pristine)! });

        public Task<List<string>> ExtractArchiveAsync(string archivePath, IReadOnlyList<string>? entries = null,
            CancellationToken ct = default) => Task.FromResult(new List<string>());

        public UnityPackageFileInfo DescribePackage(string filePath) => new();

        public Task<List<UnityPackageInstallRecord>> GetRecordsAsync(string projectPath,
            CancellationToken ct = default) => Task.FromResult(new List<UnityPackageInstallRecord>());

        public Task SaveRecordAsync(string projectPath, UnityPackageInstallRecord record,
            CancellationToken ct = default) => Task.CompletedTask;

        public Task RemoveRecordAsync(string projectPath, string packageId, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeBooth : IBoothService
    {
        public Func<string, string?, List<BoothMatchCandidate>> Search { get; init; } = (_, _) => [];

        public Dictionary<string, BoothMatchCandidate> Items { get; } = new(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, string>? SessionCookies => null;

        public IReadOnlyList<StoredCookie>? SessionEntries => null;

        public void ImportSession(IReadOnlyList<StoredCookie> cookies)
        {
        }

        public bool HasSession => false;

        public Task<bool?> ProbeSessionAsync(CancellationToken ct = default) => Task.FromResult<bool?>(null);

        public string GetLibraryUrl(int page) => string.Empty;

        public string GetFreeLibraryUrl(int page) => string.Empty;

        public string GetGiftsUrl(int page) => string.Empty;

        public string PurchaseExtractorScript => string.Empty;

        public string FreeLibraryExtractorScript => string.Empty;

        public string ItemPageExtractorScript => string.Empty;

        public BoothLibraryPage ParseLibraryPage(string extractorResultJson) => new();

        public BoothLibraryPage ParseItemPage(string extractorResultJson) => new();

        public BoothLibraryPage ParseFreeLibraryPage(string extractorResultJson) => new();

        public Task<List<BoothMatchCandidate>> SearchAsync(string query, CancellationToken ct = default,
            string? tag = null) => Task.FromResult(Search(query, tag));

        public Task<BoothMatchCandidate?> GetItemAsync(string itemId, CancellationToken ct = default) =>
            Task.FromResult(Items.TryGetValue(itemId, out var item) ? item : null);

        public BoothLibraryCache LoadLibraryCache() => new();

        public Task SaveLibraryCacheAsync(IReadOnlyList<BoothPurchaseItem> items, CancellationToken ct = default) =>
            Task.CompletedTask;

        public string GetDownloadDirectory() => string.Empty;

        public Task SetDownloadDirectoryAsync(string directory, CancellationToken ct = default) =>
            Task.CompletedTask;

        public void ReconcileLocalArchives(IEnumerable<BoothPurchaseItem> items)
        {
        }

        public int FoldOldVersions(List<BoothPurchaseItem> items, Dictionary<string, int> hiddenByNumber) => 0;

        public Task<string> DownloadAsync(BoothPurchaseItem item, IProgress<double>? progress = null,
            CancellationToken ct = default) => Task.FromResult(string.Empty);

        public Task<BoothArchiveContents> InspectAsync(BoothPurchaseItem item, CancellationToken ct = default) =>
            Task.FromResult(new BoothArchiveContents());
    }

    private sealed class FakeAssetStore : IAssetStoreService
    {
        public IReadOnlyDictionary<string, string>? SessionCookies => null;

        public IReadOnlyList<StoredCookie>? SessionEntries => null;

        public void ImportSession(IReadOnlyList<StoredCookie> cookies)
        {
        }

        public void ReloadCaches()
        {
        }

        public bool HasSession => false;

        public string MyAssetsUrl => string.Empty;

        public string MyAssetsExtractorScript => string.Empty;

        public string GetCacheDirectory() => string.Empty;

        public string GetDownloadDirectory() => string.Empty;

        public Task SetCacheDirectoryAsync(string directory, CancellationToken ct = default) => Task.CompletedTask;

        public Task SetDownloadDirectoryAsync(string directory, CancellationToken ct = default) => Task.CompletedTask;

        public Task<List<AssetStoreAsset>> ScanCacheAsync(CancellationToken ct = default) =>
            Task.FromResult(new List<AssetStoreAsset>());

        public AssetStorePurchasePage ParseMyAssets(string extractorResultJson) => new();

        public Task<string> DownloadAsync(AssetStoreAsset asset, IProgress<double>? progress = null,
            CancellationToken ct = default) => Task.FromResult(string.Empty);

        public void MergeCachedFiles(IEnumerable<AssetStoreAsset> accountItems,
            IEnumerable<AssetStoreAsset> cached)
        {
        }

        public string GetSearchUrl(string term) => string.Empty;

        public string SearchCoverExtractorScript => string.Empty;

        public List<AssetStoreSearchCover> ParseSearchCovers(string extractorResultJson) => [];

        public void CacheCoverUrl(string name, string url)
        {
        }

        public bool TryGetCoverUrl(string name, out string url)
        {
            url = string.Empty;
            return false;
        }

        public string MatchKey(string raw) => raw;

        public AssetStoreAccountCache LoadAccountCache() => new();

        public Task SaveAccountCacheAsync(IReadOnlyList<AssetStoreAsset> items, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeIcons : IUnityPackageIconService
    {
        public Task<string?> GetThumbnailAsync(string unityPackagePath, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);
    }
}
