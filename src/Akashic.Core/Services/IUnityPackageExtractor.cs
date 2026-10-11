using Akashic.Core.Models;

namespace Akashic.Core.Services;

/// <summary>
/// Service for parsing .unitypackage files.
/// </summary>
public interface IUnityPackageExtractor
{
    Task<UnityPackageArchive> ParseAsync(string filePath, CancellationToken ct = default);
}
