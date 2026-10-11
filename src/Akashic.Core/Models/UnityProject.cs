namespace Akashic.Core.Models;

/// <summary>
/// Represents a Unity project on disk.
/// </summary>
public class UnityProject
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
    /// Project type label (世界, 虚拟形象, Unity).
    /// </summary>
    public string ProjectTypeDisplayName => IsWorldProject ? "世界" : IsAvatarProject ? "虚拟形象" : "Unity";

    /// <summary>
    /// Project type icon glyph.
    /// </summary>
    public string ProjectTypeGlyph => IsWorldProject ? "\uE909" : IsAvatarProject ? "\uE77B" : "\uE74C";

    /// <summary>
    /// Formatted last modified date string.
    /// </summary>
    public string LastModifiedDisplay => LastModified.ToString("yyyy/MM/dd HH:mm");
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
