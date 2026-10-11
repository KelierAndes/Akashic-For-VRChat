using System.Text.Json.Serialization;

namespace Akashic.Core.Models;

/// <summary>
/// One downloadable file of a product the account already owns, as read from the Booth library page.
/// </summary>
public class BoothPurchaseItem
{
    [JsonPropertyName("number")]
    public string Number { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>The file name Booth shows next to this download.</summary>
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

    /// <summary>Booth hands this file out through an external link rather than its own CDN.</summary>
    [JsonPropertyName("external")]
    public bool External { get; set; }

    /// <summary>Product cover image Booth renders on the library card or the item page.</summary>
    [JsonPropertyName("imageUrl")]
    public string ImageUrl { get; set; } = string.Empty;

    /// <summary>True when this entry came from a free item page rather than the purchase library.</summary>
    [JsonIgnore]
    public bool IsFree { get; set; }

    /// <summary>
    /// Identity of the product behind this downloadable: the /items/ link when the page offered
    /// one, else the title plus shop. Multiple versions of one product share this key.
    /// </summary>
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

    /// <summary>
    /// The seller's name, read from the library card, falling back to its booth.pm subdomain.
    /// </summary>
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

    /// <summary>
    /// The archive name Booth shows, without its extension. The downloaded file carries a
    /// CDN-sanitized variant of this.
    /// </summary>
    [JsonIgnore]
    public string FileNameWithoutExtension => Path.GetFileNameWithoutExtension(FormLabel);

    [JsonIgnore]
    public string? LocalZipPath { get; set; }

    [JsonIgnore]
    public BoothArchiveContents? Contents { get; set; }

    /// <summary>
    /// What parsing this archive registered, kept so refreshing the list does not turn a parsed
    /// row back into a plain "downloaded" one.
    /// </summary>
    [JsonIgnore]
    public string? ParsedSummary { get; set; }

    [JsonIgnore]
    public bool IsDownloaded => LocalZipPath is not null && File.Exists(LocalZipPath);
}

/// <summary>
/// One rendered page of the account's Booth library, as returned by the WebView2 extractor.
/// </summary>
public class BoothLibraryPage
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("lastPage")]
    public int LastPage { get; set; }

    /// <summary>
    /// The page turned out to be a sign-in form, which means the exported session died.
    /// </summary>
    [JsonPropertyName("loginRequired")]
    public bool LoginRequired { get; set; }

    [JsonPropertyName("items")]
    public List<BoothPurchaseItem> Items { get; set; } = [];
}

/// <summary>
/// What a downloaded Booth archive turned out to contain.
/// </summary>
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

/// <summary>
/// A folder inside the archive that carries a UPM package.json.
/// </summary>
public class BoothUpmPackage
{
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
