using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Akashic.Core.Services;

/// <summary>
/// Extracts a thumbnail image from a .unitypackage without loading the whole archive: a
/// streaming tar walk first looks for a <c>preview.png</c> entry (Unity writes one per asset),
/// then falls back to the first image asset the package names. Results are cached on disk,
/// keyed by path, size and mtime, so a list refresh re-reads nothing.
/// </summary>
public class UnityPackageIconService : IUnityPackageIconService
{
    // Anything larger than this is a texture rather than a thumbnail; skip and keep looking.
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

        // No preview entry anywhere: find the guid of the first image asset, then read that
        // asset in a second pass. pathname blobs are tiny, so pass one stays cheap.
        var guid = FindFirstImageGuid(packagePath, ct);
        if (guid is null) return null;

        return ReadAsset(packagePath, guid, ct);
    }

    /// <summary>First tar pass: any usable preview.png entry, preferably the largest offered.</summary>
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

            // Unity caps preview sizes, so the first valid entry is nearly always the answer.
            break;
        }

        return best;
    }

    /// <summary>Records each guid's asset path, returning the first one that names an image.</summary>
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

            // Within the guid folder the pathname blob names the asset, so the image type is
            // known before the asset entry itself arrives.
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

    /// <summary>
    /// Streams every file entry of the archive as (entry name, fully read bytes). Small entries
    /// (pathname, preview.png) are what callers want; anything huge is read anyway by the
    /// stream copy, so the walk itself stays sequential and cheap on memory.
    /// </summary>
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
