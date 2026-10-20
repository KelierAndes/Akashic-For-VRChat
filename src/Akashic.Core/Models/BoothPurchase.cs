using System.Text.Json.Serialization;

namespace Akashic.Core.Models;

public class BoothPurchaseItem
{
    [JsonPropertyName("number")]
    public string Number { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("formLabel")]
    public string FormLabel { get; set; } = string.Empty;

    [JsonPropertyName("itemUrl")]
    public string ItemUrl { get; set; } = string.Empty;

    [JsonPropertyName("downloadablesUrl")]
    public string DownloadablesUrl { get; set; } = string.Empty;

    [JsonPropertyName("shop")]
    public string Shop { get; set; } = string.Empty;

    [JsonPropertyName("shopUrl")]
    public string ShopUrl { get; set; } = string.Empty;

    [JsonPropertyName("external")]
    public bool External { get; set; }

    [JsonPropertyName("imageUrl")]
    public string ImageUrl { get; set; } = string.Empty;

    [JsonPropertyName("isFree")]
    public bool IsFree { get; set; }

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonIgnore]
    public string ProductKey
    {
        get
        {
            var match = System.Text.RegularExpressions.Regex.Match(ItemUrl, @"/items/(\d+)");
            if (match.Success) return "item:" + match.Groups[1].Value;

            var title = DisplayTitle.Trim();
            return string.IsNullOrWhiteSpace(title) ? "downloadable:" + Number : $"t:{title}|{ShopHint}".ToLowerInvariant();
        }
    }

    [JsonIgnore]
    public string ShopHint
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Shop)) return Shop;
            if (!Uri.TryCreate(ShopUrl, UriKind.Absolute, out var uri)) return string.Empty;
            return uri.Host.EndsWith(".booth.pm", StringComparison.OrdinalIgnoreCase)
                ? uri.Host[..^".booth.pm".Length]
                : uri.Host;
        }
    }

    [JsonIgnore]
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? $"商品 {Number}" : Title;

    [JsonIgnore]
    public string FormDisplay => string.IsNullOrWhiteSpace(FormLabel) ? "默认版本" : FormLabel;

    [JsonIgnore]
    public string FileNameWithoutExtension => Path.GetFileNameWithoutExtension(FormLabel);

    [JsonIgnore]
    public string? LocalZipPath { get; set; }

    [JsonIgnore]
    public BoothArchiveContents? Contents { get; set; }

    [JsonIgnore]
    public string? ParsedSummary { get; set; }

    [JsonIgnore]
    public bool IsDownloaded => LocalZipPath is not null && File.Exists(LocalZipPath);
}

public class BoothLibraryPage
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("lastPage")]
    public int LastPage { get; set; }

    [JsonPropertyName("loginRequired")]
    public bool LoginRequired { get; set; }

    [JsonPropertyName("items")]
    public List<BoothPurchaseItem> Items { get; set; } = [];
}

public class BoothLibraryCache
{
    public DateTime SavedAt { get; set; }

    public List<BoothPurchaseItem> Items { get; set; } = [];
}

public class BoothArchiveContents
{
    public string ZipPath { get; set; } = string.Empty;
    public string ExtractedPath { get; set; } = string.Empty;
    public List<string> UnityPackageFiles { get; set; } = [];
    public List<BoothUpmPackage> UpmPackages { get; set; } = [];
    public List<string> AssetFolders { get; set; } = [];
    public List<string> Documents { get; set; } = [];

    public bool HasAnything => UnityPackageFiles.Count > 0 || UpmPackages.Count > 0 ||
                               AssetFolders.Count > 0 || Documents.Count > 0;
}

public class BoothUpmPackage
{
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
