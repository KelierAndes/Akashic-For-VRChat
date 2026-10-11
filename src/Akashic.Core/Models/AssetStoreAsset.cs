using System.Text.Json.Serialization;

namespace Akashic.Core.Models;

/// <summary>
/// One Asset Store product as this app sees it: the .unitypackage sitting in a scanned cache
/// folder, a row of the account's 我的资产 list, or both.
/// </summary>
public class AssetStoreAsset
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Publisher folder in the cache, or the store's publisher name for account rows.</summary>
    public string Publisher { get; set; } = string.Empty;

    /// <summary>Second cache level, e.g. "Textures MaterialsSkies". Empty for account-only rows.</summary>
    public string Category { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    /// <summary>Set when a downloaded .unitypackage of this product sits in a scanned folder.</summary>
    public string? LocalPath { get; set; }

    public long Size { get; set; }

    public DateTime LastModified { get; set; }

    /// <summary>Numeric store product id, which is what the download endpoint is keyed by.</summary>
    public string? ProductId { get; set; }

    /// <summary>Product cover image the 我的资产 row rendered, empty for cache-only entries.</summary>
    public string ImageUrl { get; set; } = string.Empty;

    /// <summary>True once the file itself has been registered in 用户软件包.</summary>
    public bool IsInLibrary { get; set; }

    /// <summary>False for products only discovered by scanning the cache, i.e. not on the account list.</summary>
    public bool IsFromAccount { get; set; }

    [JsonIgnore]
    public bool IsCached => LocalPath is not null && File.Exists(LocalPath);

    [JsonIgnore]
    public string StatusDisplay => IsInLibrary ? "已入库" : IsCached ? "已缓存" : "未下载";

    [JsonIgnore]
    public string VersionDisplay => string.IsNullOrWhiteSpace(Version) ? "未标注版本" : Version;

    [JsonIgnore]
    public string SizeDisplay => Size switch
    {
        >= 1048576 => $"{(double)Size / 1048576:F1} MB",
        >= 1024 => $"{(double)Size / 1024:F1} KB",
        > 0 => $"{Size} B",
        _ => "—"
    };

    [JsonIgnore]
    public string LastModifiedDisplay => LastModified == default ? "—" : LastModified.ToString("yyyy/MM/dd");

    /// <summary>Publisher and category as one line, with a dash for whatever level is missing.</summary>
    [JsonIgnore]
    public string FolderDisplay => string.IsNullOrWhiteSpace(Category)
        ? (string.IsNullOrWhiteSpace(Publisher) ? "—" : Publisher)
        : $"{Publisher} · {Category}";

    [JsonIgnore]
    public string LocalDirectory => LocalPath is null ? string.Empty : Path.GetDirectoryName(LocalPath) ?? string.Empty;
}

/// <summary>One product card of a storefront search page: its name and key image.</summary>
public class AssetStoreSearchCover
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("image")]
    public string ImageUrl { get; set; } = string.Empty;
}

/// <summary>
/// Disk snapshot of the last 我的资产 read, so the Asset Store page opens with account rows and
/// their covers without re-walking the storefront.
/// </summary>
public class AssetStoreAccountCache
{
    public DateTime SavedAt { get; set; }

    public List<AssetStoreAsset> Items { get; set; } = [];
}

/// <summary>One rendered page of the account's 我的资产 list.</summary>
public class AssetStorePurchasePage
{
    public int Page { get; set; }

    public int TotalPages { get; set; }

    public int Total { get; set; }

    /// <summary>The store bounced the request, which means the exported session died.</summary>
    public bool LoginRequired { get; set; }

    /// <summary>Where a page read ran and what it counted, so an empty list is explainable.</summary>
    public string ReadDiagnostics { get; set; } = string.Empty;

    public List<AssetStoreAsset> Items { get; set; } = [];
}
