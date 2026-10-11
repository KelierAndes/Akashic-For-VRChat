using Akashic.Core.Models;

namespace Akashic.Core.Services;

/// <summary>
/// Tracks the local .unitypackage library (library folders + manually added files)
/// and which packages were imported into which project.
/// </summary>
public interface IUnityPackageLibraryService
{
    List<string> GetLibraryFolders();
    List<string> GetAddedFiles();

    Task AddLibraryFolderAsync(string folderPath, CancellationToken ct = default);
    Task RemoveLibraryFolderAsync(string folderPath, CancellationToken ct = default);
    Task AddPackageFileAsync(string filePath, PackageOrigin origin = PackageOrigin.Local, CancellationToken ct = default);
    Task RemovePackageFileAsync(string filePath, CancellationToken ct = default);

    Task<List<UnityPackageFileInfo>> ScanAsync(CancellationToken ct = default);

    /// <summary>Reads display name, version and size of a .unitypackage file name.</summary>
    UnityPackageFileInfo DescribePackage(string filePath);

    Task<List<UnityPackageInstallRecord>> GetRecordsAsync(string projectPath, CancellationToken ct = default);
    Task SaveRecordAsync(string projectPath, UnityPackageInstallRecord record, CancellationToken ct = default);
    Task RemoveRecordAsync(string projectPath, string packageId, CancellationToken ct = default);
}
