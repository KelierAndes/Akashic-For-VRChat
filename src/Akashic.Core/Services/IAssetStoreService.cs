using Akashic.Core.Models;

namespace Akashic.Core.Services;

/// <summary>
/// The Asset Store side of the local libraries: what Unity already downloaded into a cache folder,
/// plus what the signed-in account owns.
/// </summary>
public interface IAssetStoreService
{
    /// <summary>
    /// The assetstore.unity.com cookies exported from the WebView2 profile. Assigning replaces the
    /// previous set; they are never written to disk.
    /// </summary>
    IReadOnlyDictionary<string, string>? SessionCookies { get; set; }

    bool HasSession { get; }

    /// <summary>Where the WebView2 has to go to sign in and reach 我的资产.</summary>
    string MyAssetsUrl { get; }

    /// <summary>JavaScript that reads the rendered 我的资产 cards. The store's JSON API rejects
    /// anything but its own token-carrying requests, so the rendered page is the source of truth.</summary>
    string MyAssetsExtractorScript { get; }

    string GetCacheDirectory();

    string GetDownloadDirectory();

    Task SetCacheDirectoryAsync(string directory, CancellationToken ct = default);

    Task SetDownloadDirectoryAsync(string directory, CancellationToken ct = default);

    /// <summary>Every .unitypackage under the cache and download folders, merged with the library state.</summary>
    Task<List<AssetStoreAsset>> ScanCacheAsync(CancellationToken ct = default);

    /// <summary>Turns the result of <see cref="MyAssetsExtractorScript"/> into rows.</summary>
    AssetStorePurchasePage ParseMyAssets(string extractorResultJson);

    /// <summary>Downloads one purchased .unitypackage into the download folder and returns its path.</summary>
    Task<string> DownloadAsync(AssetStoreAsset asset, IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Fills in <see cref="AssetStoreAsset.LocalPath"/> for account rows whose file is already in a
    /// scanned folder, so the list shows them as cached rather than 未下载.
    /// </summary>
    void MergeCachedFiles(IEnumerable<AssetStoreAsset> accountItems, IEnumerable<AssetStoreAsset> cached);

    /// <summary>The storefront search page for a product name, used to look up covers.</summary>
    string GetSearchUrl(string term);

    /// <summary>
    /// JavaScript that reads the rendered search result cards: one name and key image per product.
    /// </summary>
    string SearchCoverExtractorScript { get; }

    /// <summary>Turns the result of <see cref="SearchCoverExtractorScript"/> into cards.</summary>
    List<AssetStoreSearchCover> ParseSearchCovers(string extractorResultJson);

    /// <summary>
    /// Remembers a product cover by name so cache-only rows can pick one up later without a
    /// network round trip. Names are matched loosely, same as the cache-file merge.
    /// </summary>
    void CacheCoverUrl(string name, string url);

    /// <summary>Returns the cover remembered for a product name, if any.</summary>
    bool TryGetCoverUrl(string name, out string url);

    /// <summary>Loose name key shared by the cache merge and the cover cache.</summary>
    string MatchKey(string raw);
}
