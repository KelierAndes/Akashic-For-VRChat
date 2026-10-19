namespace Akashic.Core.Models;

public class UnityProject : System.ComponentModel.INotifyPropertyChanged
{
    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string UnityVersion { get; set; } = string.Empty;

    public bool HasVpmManifest { get; set; }

    public List<InstalledPackage> InstalledPackages { get; set; } = [];

    public DateTime LastModified { get; set; }

    public string AssetsPath => System.IO.Path.Combine(Path, "Assets");

    public string PackagesPath => System.IO.Path.Combine(Path, "Packages");

    public string ManifestPath => System.IO.Path.Combine(PackagesPath, "manifest.json");

    public string VpmManifestPath => System.IO.Path.Combine(PackagesPath, "vpm-manifest.json");

    public bool IsWorldProject => InstalledPackages.Any(p => p.Name.Equals("com.vrchat.worlds", StringComparison.OrdinalIgnoreCase));

    public bool IsAvatarProject => InstalledPackages.Any(p => p.Name.Equals("com.vrchat.avatars", StringComparison.OrdinalIgnoreCase));

    public bool IsVrcProject => InstalledPackages.Any(p => p.Name.StartsWith("com.vrchat.", StringComparison.OrdinalIgnoreCase));

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

    public bool HasCloudLink => CloudBlueprintIds.Count > 0;

    public string CloudLinkTip => CloudBlueprintIds.Count == 0
        ? string.Empty
        : $"云端匹配到 {CloudBlueprintIds.Count} 项：{string.Join(" · ", CloudBlueprintIds)}";

    public string CloudLinkLabel => CloudBlueprintIds.Count == 1 ? "云端管理" : $"云端管理（{CloudBlueprintIds.Count}）";

    public string ProjectTypeDisplayName =>
        IsWorldProject ? "世界" : IsAvatarProject ? "虚拟形象" : IsVrcProject ? "VRChat" : "Unity";

    public string ProjectTypeGlyph =>
        IsWorldProject ? Glyph(0xE909) : IsAvatarProject ? Glyph(0xE77B) : IsVrcProject ? Glyph(0xE7FC) : Glyph(0xE74C);

    private static string Glyph(int codePoint) => char.ConvertFromUtf32(codePoint);

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

public class InstalledPackage
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public PackageSource Source { get; set; }
    public bool IsLocked { get; set; }
}

public enum PackageSource
{
    Vpm,
    Local,
    Git,
    Unknown
}
