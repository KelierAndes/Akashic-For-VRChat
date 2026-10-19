using System.IO.Compression;
using System.Text.Json;
using Akashic.Core.Models;

namespace Akashic.Core.Services;

public class PackageInstaller : IPackageInstaller
{
    private readonly IVpmRegistryService _registryService;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public PackageInstaller(IVpmRegistryService registryService)
    {
        _registryService = registryService;
    }

    public async Task InstallVpmPackageAsync(UnityProject project, VpmPackage package,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        progress?.Report(0.0);

        await ResolveDependenciesAsync(project, package, ct);
        progress?.Report(0.2);

        var zipPath = await _registryService.DownloadPackageAsync(package, null, ct);
        progress?.Report(0.5);

        var targetDir = Path.Combine(project.PackagesPath, package.Name);
        if (Directory.Exists(targetDir))
            Directory.Delete(targetDir, true);

        ZipFile.ExtractToDirectory(zipPath, targetDir);
        progress?.Report(0.7);

        await UpdateVpmManifestAsync(project, package);
        progress?.Report(0.9);

        await AddLocalPackageReferenceAsync(project, package.Name);
        progress?.Report(1.0);
    }

    public async Task RemoveVpmPackageAsync(UnityProject project, string packageName, CancellationToken ct = default)
    {
        var packageDir = Path.Combine(project.PackagesPath, packageName);
        if (Directory.Exists(packageDir))
            Directory.Delete(packageDir, true);

        await RemoveFromVpmManifestAsync(project, packageName);
        await RemoveUpmPackageAsync(project, packageName, ct);
    }

    public async Task AddUpmPackageAsync(UnityProject project, string packageName, string version, CancellationToken ct = default)
    {
        var manifest = await ReadUpmManifestAsync(project);
        manifest.Dependencies[packageName] = version;
        await WriteUpmManifestAsync(project, manifest);
    }

    public async Task RemoveUpmPackageAsync(UnityProject project, string packageName, CancellationToken ct = default)
    {
        var manifest = await ReadUpmManifestAsync(project);
        manifest.Dependencies.Remove(packageName);
        await WriteUpmManifestAsync(project, manifest);
    }

    public async Task<IReadOnlyList<string>> InstallUnityPackageAsync(UnityProject project, UnityPackageArchive archive,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var projectRoot = Path.GetFullPath(project.Path);
        if (!projectRoot.EndsWith(Path.DirectorySeparatorChar.ToString()))
            projectRoot += Path.DirectorySeparatorChar;

        var selectedEntries = archive.Entries.Where(e => e.IsSelected && !string.IsNullOrEmpty(e.PathName)).ToList();
        var total = selectedEntries.Count;
        var installedPaths = new List<string>();

        for (var i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();
            var entry = selectedEntries[i];

            var relativePath = entry.PathName
                .Replace('/', Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);
            var targetPath = Path.GetFullPath(Path.Combine(projectRoot, relativePath));

            if (!targetPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"包内路径越界，已中止安装: {entry.PathName}");

            if (entry.IsDirectory)
            {
                Directory.CreateDirectory(targetPath);
                installedPaths.Add(relativePath);
            }
            else
            {
                var parentDir = Path.GetDirectoryName(targetPath);
                if (parentDir != null)
                    Directory.CreateDirectory(parentDir);

                await File.WriteAllBytesAsync(targetPath, entry.AssetData!, ct);
                installedPaths.Add(relativePath);
            }

            if (entry.HasMeta)
            {
                await File.WriteAllTextAsync(targetPath + ".meta", entry.MetaContent, ct);
                installedPaths.Add(relativePath + ".meta");
            }

            progress?.Report((double)(i + 1) / total);
        }

        return installedPaths;
    }

    public Task RemoveUnityPackageAsync(UnityProject project, IEnumerable<string> installedPaths, CancellationToken ct = default)
    {
        var projectRoot = Path.GetFullPath(project.Path);
        if (!projectRoot.EndsWith(Path.DirectorySeparatorChar.ToString()))
            projectRoot += Path.DirectorySeparatorChar;

        var ordered = installedPaths
            .Select(p => p.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(p => p.Count(c => c == Path.DirectorySeparatorChar))
            .ToList();

        foreach (var relative in ordered)
        {
            ct.ThrowIfCancellationRequested();

            var targetPath = Path.GetFullPath(Path.Combine(projectRoot, relative));
            if (!targetPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                if (File.Exists(targetPath))
                {
                    File.SetAttributes(targetPath, FileAttributes.Normal);
                    File.Delete(targetPath);
                }
                else if (Directory.Exists(targetPath))
                {
                    Directory.Delete(targetPath, false);
                }
            }
            catch (IOException) {  }
            catch (UnauthorizedAccessException) { }
        }

        return Task.CompletedTask;
    }

    public async Task ResolveDependenciesAsync(UnityProject project, VpmPackage package, CancellationToken ct = default)
    {
        if (package.VpmDependencies == null && package.Dependencies == null) return;

        var deps = package.VpmDependencies ?? package.Dependencies ?? [];

        foreach (var (depName, depVersion) in deps)
        {
            var vpmManifest = await ReadVpmManifestAsync(project);
            if (vpmManifest.Locked.TryGetValue(depName, out var lockedVal))
            {
                if (CompareVersions(lockedVal.Version, depVersion) >= 0)
                    continue;
            }

            var depPackage = await _registryService.GetPackageAsync(depName, ct);
            if (depPackage != null)
            {
                if (depPackage.Versions.TryGetValue(depVersion, out var exactVersion))
                {
                    await InstallVpmPackageAsync(project, exactVersion, null, ct);
                }
                else
                {
                    await InstallVpmPackageAsync(project, depPackage, null, ct);
                }
            }
        }
    }

    private async Task UpdateVpmManifestAsync(UnityProject project, VpmPackage package)
    {
        var manifest = await ReadVpmManifestAsync(project);

        manifest.Dependencies[package.Name] = new VpmDependencyInfo { Version = package.Version };
        manifest.Locked[package.Name] = new VpmLockedPackage
        {
            Version = package.Version,
            Dependencies = package.VpmDependencies ?? package.Dependencies
        };

        await WriteVpmManifestAsync(project, manifest);
    }

    private async Task RemoveFromVpmManifestAsync(UnityProject project, string packageName)
    {
        var manifest = await ReadVpmManifestAsync(project);
        manifest.Dependencies.Remove(packageName);
        manifest.Locked.Remove(packageName);
        await WriteVpmManifestAsync(project, manifest);
    }

    private async Task AddLocalPackageReferenceAsync(UnityProject project, string packageName)
    {
        var manifest = await ReadUpmManifestAsync(project);
        manifest.Dependencies[packageName] = $"file:{packageName}";
        await WriteUpmManifestAsync(project, manifest);
    }

    private async Task<VpmManifest> ReadVpmManifestAsync(UnityProject project)
    {
        if (!File.Exists(project.VpmManifestPath))
            return new VpmManifest();

        var json = await File.ReadAllTextAsync(project.VpmManifestPath);
        return JsonSerializer.Deserialize<VpmManifest>(json, JsonOptions) ?? new VpmManifest();
    }

    private async Task WriteVpmManifestAsync(UnityProject project, VpmManifest manifest)
    {
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        var dir = Path.GetDirectoryName(project.VpmManifestPath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(project.VpmManifestPath, json);
    }

    private async Task<UpmManifest> ReadUpmManifestAsync(UnityProject project)
    {
        if (!File.Exists(project.ManifestPath))
            return new UpmManifest();

        var json = await File.ReadAllTextAsync(project.ManifestPath);
        return JsonSerializer.Deserialize<UpmManifest>(json, JsonOptions) ?? new UpmManifest();
    }

    private async Task WriteUpmManifestAsync(UnityProject project, UpmManifest manifest)
    {
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        var dir = Path.GetDirectoryName(project.ManifestPath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(project.ManifestPath, json);
    }

    private static int CompareVersions(string a, string b)
    {
        var va = Version.TryParse(a.Split('-')[0], out var verA) ? verA : new Version(0, 0, 0);
        var vb = Version.TryParse(b.Split('-')[0], out var verB) ? verB : new Version(0, 0, 0);
        return va.CompareTo(vb);
    }
}
