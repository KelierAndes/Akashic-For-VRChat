using Akashic.Core.Models;

namespace Akashic.Core.Services;

public interface IAssetStoreService
{
    IReadOnlyDictionary<string, string>? SessionCookies { get; }

    IReadOnlyList<StoredCookie>? SessionEntries { get; }

    void ImportSession(IReadOnlyList<StoredCookie> cookies);

    bool HasSession { get; }

    string MyAssetsUrl { get; }

    string MyAssetsExtractorScript { get; }

    string GetCacheDirectory();

    string GetDownloadDirectory();

    Task SetCacheDirectoryAsync(string directory, CancellationToken ct = default);

    Task SetDownloadDirectoryAsync(string directory, CancellationToken ct = default);

    Task<List<AssetStoreAsset>> ScanCacheAsync(CancellationToken ct = default);

    AssetStorePurchasePage ParseMyAssets(string extractorResultJson);

    Task<string> DownloadAsync(AssetStoreAsset asset, IProgress<double>? progress = null, CancellationToken ct = default);

    void MergeCachedFiles(IEnumerable<AssetStoreAsset> accountItems, IEnumerable<AssetStoreAsset> cached);

    string GetSearchUrl(string term);

    string SearchCoverExtractorScript { get; }

    List<AssetStoreSearchCover> ParseSearchCovers(string extractorResultJson);

    void CacheCoverUrl(string name, string url);

    bool TryGetCoverUrl(string name, out string url);

    string MatchKey(string raw);

    AssetStoreAccountCache LoadAccountCache();

    Task SaveAccountCacheAsync(IReadOnlyList<AssetStoreAsset> items, CancellationToken ct = default);
}
