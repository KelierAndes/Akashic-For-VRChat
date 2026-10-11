using System.Text.Json.Serialization;

namespace Akashic.Core.Models;

/// <summary>
/// Application settings persisted to disk.
/// </summary>
public class AppSettings
{
    [JsonPropertyName("projectPaths")]
    public List<string> ProjectPaths { get; set; } = [];

    [JsonPropertyName("registryUrls")]
    public List<string> RegistryUrls { get; set; } =
    [
        "https://packages.vrchat.com/official",
        "https://packages.vrchat.com/curated"
    ];

    [JsonPropertyName("unityEditorPaths")]
    public List<string> UnityEditorPaths { get; set; } =
    [
        @"C:\Program Files\Unity\Hub\Editor"
    ];

    [JsonPropertyName("cacheDirectory")]
    public string CacheDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Akashic", "Cache");

    [JsonPropertyName("unityPackageLibraryPaths")]
    public List<string> UnityPackageLibraryPaths { get; set; } = [];

    [JsonPropertyName("unityPackageFiles")]
    public List<string> UnityPackageFiles { get; set; } = [];

    /// <summary>
    /// Who supplied each manually tracked .unitypackage, keyed by its absolute path. Only non-local
    /// entries are stored, so a plain hand-added file never appears here.
    /// </summary>
    [JsonPropertyName("unityPackageOrigins")]
    public Dictionary<string, PackageOrigin> UnityPackageOrigins { get; set; } = [];

    [JsonPropertyName("externalPackages")]
    public List<ExternalPackage> ExternalPackages { get; set; } = [];

    [JsonPropertyName("boothDownloadDirectory")]
    public string BoothDownloadDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Akashic", "Booth");

    /// <summary>Where Unity's own Asset Store downloads live; scanned read-only.</summary>
    [JsonPropertyName("assetStoreCacheDirectory")]
    public string AssetStoreCacheDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Unity", "Asset Store-5.x");

    /// <summary>Where this app writes the .unitypackage files it downloads itself.</summary>
    [JsonPropertyName("assetStoreDownloadDirectory")]
    public string AssetStoreDownloadDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Akashic", "AssetStore");

    [JsonPropertyName("useDarkTheme")]
    public bool UseDarkTheme { get; set; } = true;

    [JsonPropertyName("vccSettingsPath")]
    public string VccSettingsPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VRChatCreatorCompanion", "settings.json");
}
