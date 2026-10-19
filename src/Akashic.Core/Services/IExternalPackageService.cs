using Akashic.Core.Models;

namespace Akashic.Core.Services;

public interface IExternalPackageService
{
    IReadOnlyList<ExternalPackage> GetAll();

    ExternalPackage? FindByPackageName(string name);

    Task<ExternalPackage> AddLocalFolderAsync(string folderPath, PackageOrigin origin = PackageOrigin.Local, CancellationToken ct = default);

    Task<ExternalPackage> AddRepositoryAsync(string url, string? reference, CancellationToken ct = default);

    Task<bool> RemoveAsync(string id, CancellationToken ct = default);

    Task InstallAsync(UnityProject project, ExternalPackage package, IProgress<double>? progress = null,
        CancellationToken ct = default);

    Task RemoveFromProjectAsync(UnityProject project, ExternalPackage package, CancellationToken ct = default);
}
