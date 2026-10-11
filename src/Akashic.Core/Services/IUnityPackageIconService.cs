namespace Akashic.Core.Services;

/// <summary>
/// Pulls a representative image out of a .unitypackage so list rows can show one, caching the
/// result on disk because packages are read with a streaming tar walk.
/// </summary>
public interface IUnityPackageIconService
{
    /// <summary>
    /// Returns the path of a cached thumbnail image for the package, or null when the package
    /// holds no usable image. Missing or unreadable packages never throw.
    /// </summary>
    Task<string?> GetThumbnailAsync(string unityPackagePath, CancellationToken ct = default);
}
