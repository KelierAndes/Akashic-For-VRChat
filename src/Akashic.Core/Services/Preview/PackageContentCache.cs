using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Akashic.Core.Models;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;

namespace Akashic.Core.Services.Preview;

public sealed class PackageAsset
{
    public string Path { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Folder { get; init; } = string.Empty;

    public string Kind { get; init; } = "other";

    public long Size { get; init; }
}

public sealed class PackageContent
{
    public string Root { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public List<string> Prefabs { get; init; } = [];

    public List<PackageAsset> Assets { get; init; } = [];
}

public sealed class PackageContentCache
{
    private static readonly HashSet<string> ModelExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".fbx", ".obj", ".gltf", ".glb"
    };

    private static readonly HashSet<string> TextureExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".tga", ".psd", ".exr", ".tif", ".tiff", ".bmp", ".gif", ".hdr", ".webp"
    };

    private static readonly HashSet<string> SkippedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".so", ".dylib", ".exe", ".pdb", ".unitypackage", ".zip", ".7z", ".rar", ".gz"
    };

    private const long MaxAssetBytes = 512L * 1024 * 1024;

    private const string CacheVersion = "extract-v5";

    private readonly string _cacheRoot;
    private readonly string _packageRoot;
    private readonly string _archiveRoot;
    private readonly object _gate = new();

    public PackageContentCache(string? cacheRoot = null)
    {
        _cacheRoot = cacheRoot ?? PreviewCachePaths.Root;
        Directory.CreateDirectory(_cacheRoot);
        _packageRoot = PreviewCachePaths.VersionedUnder(_cacheRoot, "packages", CacheVersion);
        _archiveRoot = PreviewCachePaths.VersionedUnder(_cacheRoot, "archives", CacheVersion);
    }

    public Task<PackageContent> ResolveAsync(ResourceRow row, string? entry, CancellationToken ct) =>
        Task.Run(() => Resolve(row, entry, ct), ct);

    private PackageContent Resolve(ResourceRow row, string? entry, CancellationToken ct)
    {
        if (row.Kind == "unitypackage" && row.ArchiveEntry.Length > 0)
        {
            var nested = ExtractInnerPackage(row.ArchivePath, row.ArchiveEntry, ct);
            return LoadUnityPackage(nested, $"{Path.GetFileName(row.ArchivePath)} / {row.RelativePath}", ct);
        }

        var path = row.FilePath;
        if (Directory.Exists(path))
            return LoadDirectory(path);

        if (row.IsContainer || path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var child = PickChild(row, entry);
            if (child is null)
                throw new InvalidOperationException("压缩包里没有找到 .unitypackage。");

            var inner = ExtractInnerPackage(path, child, ct);
            return LoadUnityPackage(inner, $"{Path.GetFileName(path)} / {child}", ct);
        }

        return LoadUnityPackage(path, Path.GetFileName(path), ct);
    }

    private static string? PickChild(ResourceRow row, string? entry)
    {
        if (row.Children.Count == 0) return null;

        if (!string.IsNullOrWhiteSpace(entry))
        {
            foreach (var match in row.Children)
            {
                if (string.Equals(match.RelativePath, entry, StringComparison.OrdinalIgnoreCase))
                    return ChildEntryOf(match);
                if (string.Equals(match.ArchiveEntry, entry, StringComparison.OrdinalIgnoreCase))
                    return ChildEntryOf(match);
                if (string.Equals(Path.GetFileName(match.RelativePath), entry, StringComparison.OrdinalIgnoreCase))
                    return ChildEntryOf(match);
            }
        }

        return ChildEntryOf(row.Children[0]);
    }

    private static string? ChildEntryOf(ResourceRow child) =>
        child.ArchiveEntry.Length > 0 ? child.ArchiveEntry : child.FilePath;

    private string ExtractInnerPackage(string zipPath, string entrySpec, CancellationToken ct)
    {
        var directory = Path.Combine(_archiveRoot, KeyOf(zipPath));
        Directory.CreateDirectory(directory);

        var segments = entrySpec.Split('!', StringSplitOptions.RemoveEmptyEntries);
        var source = zipPath;

        for (var i = 0; i < segments.Length; i++)
        {
            ct.ThrowIfCancellationRequested();

            var target = Path.Combine(directory,
                KeyOf(string.Join("!", segments[..(i + 1)])) + Path.GetExtension(segments[i]));

            if (!File.Exists(target))
            {
                using var zip = ZipFile.OpenRead(source);
                var entry = zip.GetEntry(segments[i])
                            ?? throw new InvalidOperationException($"压缩包里没有 {segments[i]}。");

                var temp = target + ".tmp";
                entry.ExtractToFile(temp, overwrite: true);
                File.Move(temp, target, overwrite: true);
            }

            source = target;
        }

        return source;
    }

    private PackageContent LoadUnityPackage(string packagePath, string label, CancellationToken ct)
    {
        if (!File.Exists(packagePath))
            throw new FileNotFoundException("包文件不存在。", packagePath);

        var info = new FileInfo(packagePath);
        var key = KeyOf(packagePath, info);
        var root = Path.Combine(_packageRoot, key);
        var ready = Path.Combine(root, ".ready");

        if (!File.Exists(ready))
        {
            lock (_gate)
            {
                if (!File.Exists(ready)) Extract(packagePath, root, ct);
            }
        }

        return BuildContent(root, label);
    }

    private static PackageContent LoadDirectory(string path) => BuildContent(path, Path.GetFileName(path));

    private static PackageContent BuildContent(string root, string source)
    {
        var assets = FindAssets(root);

        return new PackageContent
        {
            Root = root,
            Source = source,
            Assets = assets,
            Prefabs = assets.Where(asset => asset.Kind is "prefab" or "model")
                .Select(asset => asset.Path)
                .ToList()
        };
    }

    private static List<PackageAsset> FindAssets(string root)
    {
        if (!Directory.Exists(root)) return [];

        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Library{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                           && !Path.GetFileName(path).Equals(".ready", StringComparison.OrdinalIgnoreCase)
                           && !path.Contains($"{Path.DirectorySeparatorChar}.tmp{Path.DirectorySeparatorChar}",
                               StringComparison.OrdinalIgnoreCase))
            .Select(path =>
            {
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                long size;
                try { size = new FileInfo(path).Length; }
                catch (IOException) { size = 0; }

                return new PackageAsset
                {
                    Path = relative,
                    Name = Path.GetFileName(relative),
                    Folder = Path.GetDirectoryName(relative)?.Replace('\\', '/') ?? string.Empty,
                    Kind = KindOf(relative),
                    Size = size
                };
            })
            .OrderBy(asset => asset.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string KindOf(string path)
    {
        var extension = Path.GetExtension(path);

        if (extension.Equals(".prefab", StringComparison.OrdinalIgnoreCase)) return "prefab";
        if (ModelExtensions.Contains(extension)) return "model";
        if (TextureExtensions.Contains(extension)) return "texture";
        if (extension.Equals(".mat", StringComparison.OrdinalIgnoreCase)) return "material";
        if (extension is ".shader" or ".shadergraph" or ".cginc" or ".hlsl" or ".compute") return "shader";
        if (extension.Equals(".unity", StringComparison.OrdinalIgnoreCase)) return "scene";
        if (extension is ".anim" or ".animation" or ".playable") return "animation";
        if (extension is ".controller" or ".overridecontroller") return "controller";
        if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)) return "script";
        if (extension is ".wav" or ".ogg" or ".mp3" or ".aiff" or ".aif") return "audio";
        if (extension is ".mp4" or ".mov" or ".webm" or ".avi") return "video";
        if (extension is ".json" or ".xml" or ".txt" or ".md" or ".csv" or ".bytes" or ".cfg" or ".ini")
            return "text";
        if (extension is ".dll" or ".so" or ".dylib" or ".exe") return "binary";
        if (extension.Equals(".asset", StringComparison.OrdinalIgnoreCase)) return "asset";

        return Path.GetFileName(path).Equals("package.json", StringComparison.OrdinalIgnoreCase)
            ? "text"
            : "other";
    }

    public static bool IsPreviewable(string kind) =>
        kind is "prefab" or "model" or "texture" or "material";

    public static bool IsModelPath(string path) =>
        ModelExtensions.Contains(Path.GetExtension(path));

    private static void Extract(string packagePath, string root, CancellationToken ct)
    {
        Directory.CreateDirectory(root);

        var pending = new Dictionary<string, PendingAsset>(StringComparer.OrdinalIgnoreCase);
        var tempDir = Path.Combine(root, ".tmp");
        Directory.CreateDirectory(tempDir);

        using var stream = File.OpenRead(packagePath);
        using var gzip = new GZipInputStream(stream);
        using var reader = new TarInputStream(gzip, Encoding.UTF8) { IsStreamOwner = false };

        TarEntry? entry;
        while ((entry = reader.GetNextEntry()) is not null)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.IsDirectory || string.IsNullOrEmpty(entry.Name)) continue;

            HandleEntry(entry.Name, reader, entry.Size, pending, tempDir);
        }

        foreach (var asset in pending.Values)
        {
            ct.ThrowIfCancellationRequested();

            if (asset.Skipped || string.IsNullOrEmpty(asset.PathName) || !IsPreviewAsset(asset.PathName)) continue;

            var target = Path.Combine(root, asset.PathName.Replace('/', Path.DirectorySeparatorChar));
            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                continue;

            var parent = Path.GetDirectoryName(target);
            if (parent is not null) Directory.CreateDirectory(parent);

            if (asset.TempPath is not null && File.Exists(asset.TempPath))
                File.Move(asset.TempPath, target, overwrite: true);

            if (!string.IsNullOrEmpty(asset.Meta))
                File.WriteAllText(target + ".meta", asset.Meta, Encoding.UTF8);
        }

        try
        {
            Directory.Delete(tempDir, recursive: true);
        }
        catch (IOException)
        {
        }

        File.WriteAllText(Path.Combine(root, ".ready"), DateTime.UtcNow.ToString("O"));
        PreviewCachePaths.EnforceQuota();
    }

    private static void HandleEntry(string rawName, Stream data, long length,
        Dictionary<string, PendingAsset> pending, string tempDir)
    {
        var name = rawName.Replace('\\', '/').TrimStart('/');
        while (name.StartsWith("./", StringComparison.Ordinal)) name = name[2..];

        var split = name.Split('/', 2);
        if (split.Length != 2) return;

        var guid = split[0];
        var file = split[1];

        if (!pending.TryGetValue(guid, out var asset))
            pending[guid] = asset = new PendingAsset();

        if (file.Equals("pathname", StringComparison.OrdinalIgnoreCase))
        {
            asset.PathName = SanitizePath(ReadExactText(data, length));
        }
        else if (file.Equals("asset.meta", StringComparison.OrdinalIgnoreCase))
        {
            asset.Meta = ReadExactText(data, length);
        }
        else if (file.Equals("asset", StringComparison.OrdinalIgnoreCase))
        {
            if (length > MaxAssetBytes)
            {
                asset.Skipped = true;
                return;
            }

            var temp = Path.Combine(tempDir, guid + ".asset");
            using var output = File.Create(temp);
            CopyExact(data, output, length);
            asset.TempPath = temp;
        }
    }

    private static string ReadExactText(Stream stream, long length)
    {
        if (length <= 0) return string.Empty;

        var buffer = new byte[length];
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read <= 0) break;
            offset += read;
        }

        return Encoding.UTF8.GetString(buffer, 0, offset);
    }

    private static void CopyExact(Stream input, Stream output, long length)
    {
        var buffer = new byte[81920];
        var remaining = length;

        while (remaining > 0)
        {
            var read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read <= 0) break;
            output.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private static bool IsPreviewAsset(string? pathName)
    {
        if (string.IsNullOrWhiteSpace(pathName)) return false;

        var extension = Path.GetExtension(pathName);
        return extension.Length == 0 || !SkippedExtensions.Contains(extension);
    }

    private static string SanitizePath(string raw)
    {
        var path = raw.Replace('\\', '/').Trim();
        var end = path.IndexOfAny(['\r', '\n', '\0']);
        if (end >= 0) path = path[..end];

        path = path.TrimStart('/');
        while (path.Contains("..", StringComparison.Ordinal))
            path = path.Replace("..", string.Empty, StringComparison.Ordinal);

        return path;
    }

    private static string KeyOf(string path)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{CacheVersion}|{Path.GetFullPath(path)}"));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private static string KeyOf(string path, FileInfo info)
    {
        var raw = $"{CacheVersion}|{Path.GetFullPath(path)}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private sealed class PendingAsset
    {
        public string? PathName { get; set; }

        public string? Meta { get; set; }

        public string? TempPath { get; set; }

        public bool Skipped { get; set; }
    }
}
