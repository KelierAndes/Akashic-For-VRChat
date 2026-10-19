using Akashic.Core.Models;

namespace Akashic.Core.Services;

public interface IPackageInstaller
{
    Task InstallVpmPackageAsync(UnityProject project, VpmPackage package, IProgress<double>? progress = null, CancellationToken ct = default);
    Task RemoveVpmPackageAsync(UnityProject project, string packageName, CancellationToken ct = default);
    Task AddUpmPackageAsync(UnityProject project, string packageName, string version, CancellationToken ct = default);
    Task RemoveUpmPackageAsync(UnityProject project, string packageName, CancellationToken ct = default);
    Task<IReadOnlyList<string>> InstallUnityPackageAsync(UnityProject project, UnityPackageArchive archive, IProgress<double>? progress = null, CancellationToken ct = default);
    Task RemoveUnityPackageAsync(UnityProject project, IEnumerable<string> installedPaths, CancellationToken ct = default);
    Task ResolveDependenciesAsync(UnityProject project, VpmPackage package, CancellationToken ct = default);
}
