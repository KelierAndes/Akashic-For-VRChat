using CommunityToolkit.Mvvm.ComponentModel;

namespace Akashic.Core.Models;

/// <summary>
/// Represents an entry (asset or folder) inside a .unitypackage file.
/// </summary>
public partial class UnityPackageEntry : ObservableObject
{
    public string Guid { get; set; } = string.Empty;
    public string PathName { get; set; } = string.Empty;
    public byte[]? AssetData { get; set; }
    public string? MetaContent { get; set; }
    public byte[]? PreviewData { get; set; }

    /// <summary>Folder entries carry a pathname and a folderAsset meta but no asset payload.</summary>
    public bool IsDirectory => AssetData == null;
    public bool HasMeta => !string.IsNullOrEmpty(MetaContent);
    public long Size => AssetData?.Length ?? 0;

    [ObservableProperty]
    private bool _isSelected = true;

    public string FileName
    {
        get
        {
            var idx = PathName.LastIndexOf('/');
            return idx >= 0 ? PathName[(idx + 1)..] : PathName;
        }
    }

    public string SizeDisplay => IsDirectory
        ? "文件夹"
        : Size >= 1048576 ? $"{(double)Size / 1048576:F1} MB"
        : Size >= 1024 ? $"{(double)Size / 1024:F1} KB"
        : $"{Size} B";
}

/// <summary>
/// Represents a parsed .unitypackage file ready for installation.
/// </summary>
public class UnityPackageArchive
{
    public string FilePath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<UnityPackageEntry> Entries { get; set; } = [];
    public long TotalSize => Entries.Sum(e => e.Size);
    public int FileCount => Entries.Count(e => !e.IsDirectory);
    public int DirectoryCount => Entries.Count(e => e.IsDirectory);
}
