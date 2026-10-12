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

    /// <summary>
    /// Editors the user registered by hand. They are consulted before Unity Hub and VCC, so a
    /// project always starts with the editor the user pointed at for that version.
    /// </summary>
    [JsonPropertyName("unityEditorEntries")]
    public List<UnityEditorEntry> UnityEditorEntries { get; set; } = [];

    /// <summary>Where backup zips go. Empty falls back to VCC's own setting, then to the app folder.</summary>
    [JsonPropertyName("projectBackupPath")]
    public string ProjectBackupPath { get; set; } = string.Empty;

    /// <summary>
    /// Drops the package folders from a backup. The manifests stay in, so the packages can be
    /// resolved again, and the archive only carries what no registry can hand back.
    /// </summary>
    [JsonPropertyName("backupExcludePackages")]
    public bool BackupExcludePackages { get; set; }

    /// <summary>Where new projects are created and where the folder scan looks for projects.</summary>
    [JsonPropertyName("defaultProjectPath")]
    public string DefaultProjectPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "VRCProjects");

    /// <summary>Templates the user saved from existing projects, used by the new project dialog.</summary>
    [JsonPropertyName("projectTemplates")]
    public List<ProjectTemplate> ProjectTemplates { get; set; } = [];

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
