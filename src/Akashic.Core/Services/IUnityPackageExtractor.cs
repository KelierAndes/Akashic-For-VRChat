using Akashic.Core.Models;

namespace Akashic.Core.Services;

public interface IUnityPackageExtractor
{
    Task<UnityPackageArchive> ParseAsync(string filePath, CancellationToken ct = default);
}
