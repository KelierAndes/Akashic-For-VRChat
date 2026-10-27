using System.Text.Json.Serialization;

namespace Akashic.Core.Models;

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

    [JsonPropertyName("unityEditorEntries")]
    public List<UnityEditorEntry> UnityEditorEntries { get; set; } = [];

    [JsonPropertyName("projectBackupPath")]
    public string ProjectBackupPath { get; set; } = string.Empty;

    [JsonPropertyName("backupExcludePackages")]
    public bool BackupExcludePackages { get; set; }

    [JsonPropertyName("defaultProjectPath")]
    public string DefaultProjectPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "VRCProjects");

    [JsonPropertyName("projectTemplates")]
    public List<ProjectTemplate> ProjectTemplates { get; set; } = [];

    [JsonPropertyName("cacheDirectory")]
    public string CacheDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Akashic", "Cache");

    [JsonPropertyName("previewCacheDirectory")]
    public string PreviewCacheDirectory { get; set; } = string.Empty;

    [JsonPropertyName("previewCacheQuotaBytes")]
    public long PreviewCacheQuotaBytes { get; set; } = 8L * 1024 * 1024 * 1024;

    [JsonPropertyName("unityPackageLibraryPaths")]
    public List<string> UnityPackageLibraryPaths { get; set; } = [];
    [JsonPropertyName("unityPackageFiles")]
    public List<string> UnityPackageFiles { get; set; } = [];

    [JsonPropertyName("unityPackageOrigins")]
    public Dictionary<string, PackageOrigin> UnityPackageOrigins { get; set; } = [];

    [JsonPropertyName("externalPackages")]
    public List<ExternalPackage> ExternalPackages { get; set; } = [];

    [JsonPropertyName("boothDownloadDirectory")]
    public string BoothDownloadDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Akashic", "Booth");

    [JsonPropertyName("assetStoreCacheDirectory")]
    public string AssetStoreCacheDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Unity", "Asset Store-5.x");

    [JsonPropertyName("assetStoreDownloadDirectory")]
    public string AssetStoreDownloadDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Akashic", "AssetStore");

    [JsonPropertyName("useDarkTheme")]
    public bool UseDarkTheme { get; set; } = true;

    [JsonPropertyName("syncEnabled")]
    public bool SyncEnabled { get; set; } = true;

    [JsonPropertyName("sheetOpacity")]
    public double SheetOpacity { get; set; } = 0.7;

    [JsonPropertyName("railItems")]
    public List<string> RailItems { get; set; } = [];

    [JsonPropertyName("vccSettingsPath")]
    public string VccSettingsPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VRChatCreatorCompanion", "settings.json");

    [JsonPropertyName("syncGistId")]
    public string SyncGistId { get; set; } = string.Empty;

    [JsonPropertyName("syncUploadedAt")]
    public DateTime? SyncUploadedAt { get; set; }

    [JsonPropertyName("syncPayloadBytes")]
    public int SyncPayloadBytes { get; set; }
}
