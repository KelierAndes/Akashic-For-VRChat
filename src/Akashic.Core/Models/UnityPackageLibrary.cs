using System.Text.Json.Serialization;

namespace Akashic.Core.Models;

/// <summary>
/// A .unitypackage file discovered in a local library folder or added by hand.
/// </summary>
public class UnityPackageFileInfo
{
    public string FilePath { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime LastModified { get; set; }

    /// <summary>Who supplied the file; the library scan fills this in from settings.json.</summary>
    public PackageOrigin Origin { get; set; }

    /// <summary>Stable identity used to match install records: file name without extension.</summary>
    [JsonIgnore]
    public string PackageId => Path.GetFileNameWithoutExtension(FilePath);

    [JsonIgnore]
    public string OriginDisplay => Origin.Display();

    [JsonIgnore]
    public string FileName => Path.GetFileName(FilePath);

    [JsonIgnore]
    public string SizeDisplay => Size >= 1048576 ? $"{(double)Size / 1048576:F1} MB" : $"{(double)Size / 1024:F1} KB";

    [JsonIgnore]
    public string LastModifiedDisplay => LastModified.ToString("yyyy/MM/dd");
}

/// <summary>
/// One recorded .unitypackage import into a project, used to report installed state and to undo it.
/// </summary>
public class UnityPackageInstallRecord
{
    [JsonPropertyName("packageId")]
    public string PackageId { get; set; } = string.Empty;

    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("installedAt")]
    public DateTime InstalledAt { get; set; }

    [JsonPropertyName("fileCount")]
    public int FileCount { get; set; }

    /// <summary>Project-relative paths (assets, folders and their .meta files) written by this import.</summary>
    [JsonPropertyName("installedPaths")]
    public List<string> InstalledPaths { get; set; } = [];
}
