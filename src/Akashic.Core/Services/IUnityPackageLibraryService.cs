using Akashic.Core.Models;

namespace Akashic.Core.Services;

public interface IUnityPackageLibraryService
{
    List<string> GetLibraryFolders();
    List<string> GetAddedFiles();

    Task AddLibraryFolderAsync(string folderPath, CancellationToken ct = default);
    Task RemoveLibraryFolderAsync(string folderPath, CancellationToken ct = default);
    Task AddPackageFileAsync(string filePath, PackageOrigin origin = PackageOrigin.Local, CancellationToken ct = default);
    Task RemovePackageFileAsync(string filePath, CancellationToken ct = default);

    Task<List<UnityPackageFileInfo>> ScanAsync(CancellationToken ct = default);

    Task<List<ResourceRow>> ScanResourcesAsync(CancellationToken ct = default);

    Task<List<string>> ExtractArchiveAsync(string archivePath, IReadOnlyList<string>? entries = null,
        CancellationToken ct = default);

    UnityPackageFileInfo DescribePackage(string filePath);

    Task<List<UnityPackageInstallRecord>> GetRecordsAsync(string projectPath, CancellationToken ct = default);
    Task SaveRecordAsync(string projectPath, UnityPackageInstallRecord record, CancellationToken ct = default);
    Task RemoveRecordAsync(string projectPath, string packageId, CancellationToken ct = default);
}
