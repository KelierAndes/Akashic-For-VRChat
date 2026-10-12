namespace Akashic.Core.Models;

/// <summary>
/// Represents a Unity project on disk.
/// </summary>
public class UnityProject : System.ComponentModel.INotifyPropertyChanged
{
    /// <summary>
    /// Display name of the project (folder name).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Full path to the project root directory.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Unity editor version used by this project.
    /// </summary>
    public string UnityVersion { get; set; } = string.Empty;

    /// <summary>
    /// Whether this project has VPM (vpm-manifest.json) configured.
    /// </summary>
    public bool HasVpmManifest { get; set; }

    /// <summary>
    /// List of installed packages in this project.
    /// </summary>
    public List<InstalledPackage> InstalledPackages { get; set; } = [];

    /// <summary>
    /// Last time the project was opened/modified.
    /// </summary>
    public DateTime LastModified { get; set; }

    /// <summary>
    /// Project's Assets directory path.
    /// </summary>
    public string AssetsPath => System.IO.Path.Combine(Path, "Assets");

    /// <summary>
    /// Project's Packages directory path.
    /// </summary>
    public string PackagesPath => System.IO.Path.Combine(Path, "Packages");

    /// <summary>
    /// Path to the UPM manifest.json.
    /// </summary>
    public string ManifestPath => System.IO.Path.Combine(PackagesPath, "manifest.json");

    /// <summary>
    /// Path to the VPM manifest (vpm-manifest.json).
    /// </summary>
    public string VpmManifestPath => System.IO.Path.Combine(PackagesPath, "vpm-manifest.json");

    /// <summary>
    /// Whether this project is a VRChat World project.
    /// </summary>
    public bool IsWorldProject => InstalledPackages.Any(p => p.Name.Equals("com.vrchat.worlds", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether this project is a VRChat Avatar project.
    /// </summary>
    public bool IsAvatarProject => InstalledPackages.Any(p => p.Name.Equals("com.vrchat.avatars", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether this is a VRChat project at all: any part of the SDK counts, including a base-only
    /// project that is neither a world nor an avatar.
    /// </summary>
    public bool IsVrcProject => InstalledPackages.Any(p => p.Name.StartsWith("com.vrchat.", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Blueprint ids this project's scenes produce that the signed-in VRChat account also holds.
    /// Never persisted: the scan that fills them now runs after the list is already on screen, so
    /// changing them has to raise change notification for the card to update itself.
    /// </summary>
    public List<string> CloudBlueprintIds
    {
        get => _cloudBlueprintIds;
        set
        {
            _cloudBlueprintIds = value ?? [];
            RaiseAll(nameof(CloudBlueprintIds), nameof(HasCloudLink), nameof(CloudLinkTip), nameof(CloudLinkLabel));
        }
    }

    private List<string> _cloudBlueprintIds = [];

    /// <summary>Whether the card should offer the jump to VRChat's own management page.</summary>
    public bool HasCloudLink => CloudBlueprintIds.Count > 0;

    /// <summary>What the jump control is worth showing, e.g. "wrld_…" or both ids.</summary>
    public string CloudLinkTip => CloudBlueprintIds.Count == 0
        ? string.Empty
        : $"云端匹配到 {CloudBlueprintIds.Count} 项：{string.Join(" · ", CloudBlueprintIds)}";

    /// <summary>Short label for the card, since the ids themselves are unreadable.</summary>
    public string CloudLinkLabel => CloudBlueprintIds.Count == 1 ? "云端管理" : $"云端管理（{CloudBlueprintIds.Count}）";

    /// <summary>
    /// Project type label (世界, 虚拟形象, VRChat, Unity).
    /// </summary>
    public string ProjectTypeDisplayName =>
        IsWorldProject ? "世界" : IsAvatarProject ? "虚拟形象" : IsVrcProject ? "VRChat" : "Unity";

    /// <summary>
    /// Project type icon glyph.
    /// </summary>
    public string ProjectTypeGlyph =>
        IsWorldProject ? Glyph(0xE909) : IsAvatarProject ? Glyph(0xE77B) : IsVrcProject ? Glyph(0xE7FC) : Glyph(0xE74C);

    private static string Glyph(int codePoint) => char.ConvertFromUtf32(codePoint);

    /// <summary>
    /// Formatted last modified date string.
    /// </summary>
    public string LastModifiedDisplay => LastModified.ToString("yyyy/MM/dd HH:mm");

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    private void RaiseAll(params string[] names)
    {
        var handler = PropertyChanged;
        if (handler is null) return;

        foreach (var name in names)
            handler(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }
}

/// <summary>
/// Represents a package installed in a Unity project.
/// </summary>
public class InstalledPackage
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public PackageSource Source { get; set; }
    public bool IsLocked { get; set; }
}

/// <summary>
/// Indicates where a package originates from.
/// </summary>
public enum PackageSource
{
    Vpm,
    Local,
    Git,
    Unknown
}
