using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Akashic.Core.Services;

public class UnityPackageIconService : IUnityPackageIconService
{
    private const long PreviewMaxBytes = 8 * 1024 * 1024;
    private const long AssetMaxBytes = 3 * 1024 * 1024;

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg"];

    private readonly string _cacheDir;

    public UnityPackageIconService(string? cacheDirectory = null)
    {
        _cacheDir = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Akashic", "Thumbnails");
    }

    public Task<string?> GetThumbnailAsync(string unityPackagePath, CancellationToken ct = default) =>
        Task.Run(() => GetThumbnail(unityPackagePath, ct), ct);

    private string? GetThumbnail(string packagePath, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(packagePath)) return null;

            var info = new FileInfo(packagePath);
            var cached = CachedPath(packagePath, info);
            if (File.Exists(cached)) return cached;

            var image = ExtractImage(packagePath, ct);
            if (image is null) return null;

            Directory.CreateDirectory(_cacheDir);
            var target = cached + image.Value.Extension;
            File.WriteAllBytes(target, image.Value.Bytes);
            return target;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or InvalidDataException or EndOfStreamException)
        {
            return null;
        }
    }

    private static (byte[] Bytes, string Extension)? ExtractImage(string packagePath, CancellationToken ct)
    {
        var preview = FirstPass(packagePath, ct);
        if (preview is not null) return preview;

        var guid = FindFirstImageGuid(packagePath, ct);
        if (guid is null) return null;

        return ReadAsset(packagePath, guid, ct);
    }

    private static (byte[] Bytes, string Extension)? FirstPass(string packagePath, CancellationToken ct)
    {
        (byte[] Bytes, string Extension)? best = null;

        foreach (var (name, data) in WalkEntries(packagePath, ct))
        {
            var fileName = name.Split('/')[^1];
            if (!fileName.Equals("preview.png", StringComparison.OrdinalIgnoreCase)) continue;

            if (data.Length == 0 || data.Length > PreviewMaxBytes) continue;
            if (best is null || data.Length > best.Value.Bytes.Length)
                best = (data, ".png");

            break;
        }

        return best;
    }

    private static string? FindFirstImageGuid(string packagePath, CancellationToken ct)
    {
        var pathNames = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (name, data) in WalkEntries(packagePath, ct))
        {
            var parts = name.Split('/');
            if (parts.Length < 2) continue;

            var guid = parts[0];
            var fileName = parts[1];

            if (fileName.Equals("pathname", StringComparison.OrdinalIgnoreCase))
            {
                var assetPath = Encoding.UTF8.GetString(data);
                var lineBreak = assetPath.IndexOfAny(['\n', '\r', '\0']);
                if (lineBreak >= 0) assetPath = assetPath[..lineBreak];

                pathNames[guid] = assetPath.Trim().Replace('\\', '/');

                if (ImageExtensions.Contains(Path.GetExtension(pathNames[guid]).ToLowerInvariant()))
                    return guid;
            }
        }

        return null;
    }

    private static (byte[] Bytes, string Extension)? ReadAsset(string packagePath, string guid, CancellationToken ct)
    {
        string? extension = null;

        foreach (var (name, data) in WalkEntries(packagePath, ct))
        {
            var parts = name.Split('/');
            if (parts.Length < 2 || !parts[0].Equals(guid, StringComparison.Ordinal)) continue;

            if (parts[1].Equals("pathname", StringComparison.OrdinalIgnoreCase))
            {
                var path = ReadAssetPath(data);
                if (!string.IsNullOrEmpty(path))
                    extension = Path.GetExtension(path).ToLowerInvariant();
            }
            else if (parts[1].Equals("asset", StringComparison.OrdinalIgnoreCase))
            {
                if (data.Length == 0 || data.Length > AssetMaxBytes) return null;
                return (data, extension is ".jpg" or ".jpeg" ? ".jpg" : ".png");
            }
        }

        return null;
    }

    private static string? ReadAssetPath(byte[] data)
    {
        var text = Encoding.UTF8.GetString(data);
        var lineBreak = text.IndexOfAny(['\n', '\r', '\0']);
        if (lineBreak >= 0) text = text[..lineBreak];
        return text.Trim().Replace('\\', '/').TrimEnd('/');
    }

    private static IEnumerable<(string Name, byte[] Data)> WalkEntries(string packagePath, CancellationToken ct)
    {
        using var fileStream = File.OpenRead(packagePath);
        using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        using var tarReader = new TarReader(gzipStream);

        TarEntry? entry;
        while ((entry = tarReader.GetNextEntry()) != null)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.EntryType == TarEntryType.Directory || entry.DataStream is null) continue;

            var name = entry.Name.Replace('\\', '/').TrimStart('/');
            if (name.Length == 0) continue;

            using var buffer = new MemoryStream();
            entry.DataStream.CopyTo(buffer);
            yield return (name, buffer.ToArray());
        }
    }

    private string CachedPath(string packagePath, FileInfo info)
    {
        var seed = $"{packagePath.ToLowerInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed)))[..24];
        return Path.Combine(_cacheDir, hash);
    }
}
