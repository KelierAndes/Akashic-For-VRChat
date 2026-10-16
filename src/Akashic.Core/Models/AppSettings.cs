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

    /// <summary>
    /// Whether the encrypted gist sync runs at all. Signing into GitHub is useful on its own —
    /// private external packages go through the same credential — so the login stays available
    /// with this off, and only the upload, the restore and the post-login auto-push honour it.
    /// </summary>
    [JsonPropertyName("syncEnabled")]
    public bool SyncEnabled { get; set; } = true;

    /// <summary>
    /// Alpha of the glass surfaces: the window sheet, the rail trays and the popup panels.
    /// The 透明度 slider shows this number as-is, so the usable band is 0.4 … 1 and 0.7
    /// is the shipped look.
    /// </summary>
    [JsonPropertyName("sheetOpacity")]
    public double SheetOpacity { get; set; } = 0.7;

    /// <summary>
    /// What the icon rail holds, in order: <c>nav:&lt;id&gt;</c> for a destination and
    /// <c>proj:&lt;path&gt;</c> for a pinned project. Empty means the built-in set.
    /// </summary>
    [JsonPropertyName("railItems")]
    public List<string> RailItems { get; set; } = [];

    [JsonPropertyName("vccSettingsPath")]
    public string VccSettingsPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VRChatCreatorCompanion", "settings.json");

    /// <summary>
    /// The private gist this machine pushes its encrypted config to. Left empty, the first
    /// upload creates one under the signed-in GitHub account and stores its id here.
    /// </summary>
    [JsonPropertyName("syncGistId")]
    public string SyncGistId { get; set; } = string.Empty;

    [JsonPropertyName("syncUploadedAt")]
    public DateTime? SyncUploadedAt { get; set; }

    /// <summary>How many bytes the last upload carried, so the page can say what it sent.</summary>
    [JsonPropertyName("syncPayloadBytes")]
    public int SyncPayloadBytes { get; set; }
}
