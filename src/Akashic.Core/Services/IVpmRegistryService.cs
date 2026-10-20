using Akashic.Core.Models;

namespace Akashic.Core.Services;

public interface IVpmRegistryService
{
    Task<List<VpmPackage>> GetAllPackagesAsync(bool refresh = false, CancellationToken ct = default);
    Task<List<VpmPackage>> GetPackagesFromRegistryAsync(string registryUrl, CancellationToken ct = default);
    Task<VpmPackage?> GetPackageAsync(string packageName, CancellationToken ct = default);
    Task<List<VpmPackage>> SearchPackagesAsync(string query, CancellationToken ct = default);
    Task<string> DownloadPackageAsync(VpmPackage package, IProgress<double>? progress = null, CancellationToken ct = default);
    List<string> GetRegistryUrls();
    Task AddRegistryUrlAsync(string url);
    Task RemoveRegistryUrlAsync(string url);

    Task SaveRegistryOrderAsync(IEnumerable<string> orderedUrls, CancellationToken ct = default);

    Task<string?> FetchRepositoryNameAsync(string registryUrl, CancellationToken ct = default);

    IReadOnlyList<VccRepository> GetKnownRepositories();

    Task<int> ImportVccRepositoriesAsync();

    VccRepository? FindRepositoryFor(string packageName);
}
