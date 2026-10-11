using Akashic.Core.Models;

namespace Akashic.Core.Services;

/// <summary>
/// Packages the user attaches outside of VPM: local UPM package folders and git repositories
/// (GitHub, GitLab, Gitee and other public hosts). They are installed straight into a project's
/// Packages/manifest.json, so VCC is not involved.
/// </summary>
public interface IExternalPackageService
{
    IReadOnlyList<ExternalPackage> GetAll();

    ExternalPackage? FindByPackageName(string name);

    Task<ExternalPackage> AddLocalFolderAsync(string folderPath, PackageOrigin origin = PackageOrigin.Local, CancellationToken ct = default);

    /// <summary>
    /// Attaches a git-hosted UPM package. The URL's host decides the platform (GitHub, GitLab,
    /// Gitee), so callers do not need to know which one they are holding.
    /// </summary>
    Task<ExternalPackage> AddRepositoryAsync(string url, string? reference, CancellationToken ct = default);

    Task<bool> RemoveAsync(string id, CancellationToken ct = default);

    Task InstallAsync(UnityProject project, ExternalPackage package, IProgress<double>? progress = null,
        CancellationToken ct = default);

    Task RemoveFromProjectAsync(UnityProject project, ExternalPackage package, CancellationToken ct = default);
}
