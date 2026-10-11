using CommunityToolkit.Mvvm.ComponentModel;

namespace Akashic.Core.Models;

/// <summary>
/// Flat unified package item for the ALCOM-style package management table.
/// Combines VPM packages and .unitypackage assets.
/// </summary>
public partial class UnifiedPackageItem : ObservableObject
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public PackageCategory Category { get; set; } = PackageCategory.Vpm;
    public string SourceName { get; set; } = "Official";

    /// <summary>Who supplied a local package, so a Booth import can be filtered as its own class.</summary>
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

    /// <summary>Installed from a repository this app does not list, so it cannot be (re)installed from here.</summary>
    public bool ForeignRepository { get; set; }

    /// <summary>Only VPM packages have a version stream to pick from; everything else shows plain text.</summary>
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
