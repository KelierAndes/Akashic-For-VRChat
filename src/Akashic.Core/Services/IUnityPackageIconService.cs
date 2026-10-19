namespace Akashic.Core.Services;

public interface IUnityPackageIconService
{
    Task<string?> GetThumbnailAsync(string unityPackagePath, CancellationToken ct = default);
}
