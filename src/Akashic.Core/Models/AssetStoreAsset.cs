using System.Text.Json.Serialization;

namespace Akashic.Core.Models;

public class AssetStoreAsset
{
    public string Name { get; set; } = string.Empty;

    public string Publisher { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string? LocalPath { get; set; }

    public long Size { get; set; }

    public DateTime LastModified { get; set; }

    public string? ProductId { get; set; }

    public string ImageUrl { get; set; } = string.Empty;

    public bool IsInLibrary { get; set; }

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

    [JsonIgnore]
    public string FolderDisplay => string.IsNullOrWhiteSpace(Category)
        ? (string.IsNullOrWhiteSpace(Publisher) ? "—" : Publisher)
        : $"{Publisher} · {Category}";

    [JsonIgnore]
    public string LocalDirectory => LocalPath is null ? string.Empty : Path.GetDirectoryName(LocalPath) ?? string.Empty;
}

public class AssetStoreSearchCover
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("image")]
    public string ImageUrl { get; set; } = string.Empty;
}

public class AssetStoreAccountCache
{
    public DateTime SavedAt { get; set; }

    public List<AssetStoreAsset> Items { get; set; } = [];
}

public class AssetStorePurchasePage
{
    public int Page { get; set; }

    public int TotalPages { get; set; }

    public int Total { get; set; }

    public bool LoginRequired { get; set; }

    public string ReadDiagnostics { get; set; } = string.Empty;

    public List<AssetStoreAsset> Items { get; set; } = [];
}
