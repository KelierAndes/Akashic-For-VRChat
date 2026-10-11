using Akashic.Core.Models;

namespace Akashic.Core.Services;

/// <summary>
/// Service for interacting with VPM package registries.
/// </summary>
public interface IVpmRegistryService
{
    Task<List<VpmPackage>> GetAllPackagesAsync(CancellationToken ct = default);
    Task<List<VpmPackage>> GetPackagesFromRegistryAsync(string registryUrl, CancellationToken ct = default);
    Task<VpmPackage?> GetPackageAsync(string packageName, CancellationToken ct = default);
    Task<List<VpmPackage>> SearchPackagesAsync(string query, CancellationToken ct = default);
    Task<string> DownloadPackageAsync(VpmPackage package, IProgress<double>? progress = null, CancellationToken ct = default);
    List<string> GetRegistryUrls();
    Task AddRegistryUrlAsync(string url);
    Task RemoveRegistryUrlAsync(string url);

    /// <summary>Persists a user-dragged repository order, which is also the resolution priority.</summary>
    Task SaveRegistryOrderAsync(IEnumerable<string> orderedUrls, CancellationToken ct = default);

    /// <summary>Reads the advertised repository name from a listing, used for the 名称 column.</summary>
    Task<string?> FetchRepositoryNameAsync(string registryUrl, CancellationToken ct = default);

    /// <summary>Every repository VCC has a listing for, whether or not this app lists it.</summary>
    IReadOnlyList<VccRepository> GetKnownRepositories();

    /// <summary>Adds the repositories VCC knows about that this app does not list yet.</summary>
    Task<int> ImportVccRepositoriesAsync();

    /// <summary>The repository that advertises a package, used to name the origin of foreign installs.</summary>
    VccRepository? FindRepositoryFor(string packageName);
}
