using CommunityToolkit.Mvvm.ComponentModel;

namespace Akashic.Core.Models;

public partial class UnifiedPackageItem : ObservableObject
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public PackageCategory Category { get; set; } = PackageCategory.Vpm;
    public string SourceName { get; set; } = "Official";

    public PackageOrigin Origin { get; set; } = PackageOrigin.Local;

    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    private string _installedVersion = "无";

    [ObservableProperty]
    private string _latestVersion = string.Empty;

    [ObservableProperty]
    private string _selectedVersion = string.Empty;

    [ObservableProperty]
    private bool _canUpgrade;

    public List<string> AvailableVersions { get; set; } = [];

    public VpmPackage? VpmPackageRef { get; set; }
    public InstalledPackage? InstalledPackageRef { get; set; }
    public UnityPackageFileInfo? UnityPackageRef { get; set; }
    public UnityPackageInstallRecord? InstallRecordRef { get; set; }
    public ExternalPackage? ExternalRef { get; set; }

    [ObservableProperty]
    private bool _isSelected;

    public bool ForeignRepository { get; set; }

    public bool ShowVersionSelector => IsInstalled && Category == PackageCategory.Vpm && AvailableVersions.Count > 0;
    public bool ShowInstalledVersionText => !ShowVersionSelector;
}

public enum PackageCategory
{
    All,
    Vpm,
    UnityPackage,
    External
}
