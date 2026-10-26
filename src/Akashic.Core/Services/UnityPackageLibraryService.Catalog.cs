using System.IO.Compression;
using Akashic.Core.Models;

namespace Akashic.Core.Services;

public partial class UnityPackageLibraryService
{
    private const string ExtractedSuffix = "-extracted";
    private const int MaxArchiveDepth = 3;
    private const long MaxNestedArchiveBytes = 256L * 1024 * 1024;

    public Task<List<ResourceRow>> ScanResourcesAsync(CancellationToken ct = default) =>
        Task.Run(() => ScanResources(ct), ct);

    private List<ResourceRow> ScanResources(CancellationToken ct)
    {
        var settings = LoadSettings();

        var origins = new Dictionary<string, PackageOrigin>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, origin) in settings.UnityPackageOrigins)
            if (!string.IsNullOrWhiteSpace(path))
                origins[Key(path)] = origin;

        var boothRoot = NormalizeRoot(settings.BoothDownloadDirectory);

        var roots = new List<string>();
        foreach (var folder in settings.UnityPackageLibraryPaths)
        {
            var root = NormalizeRoot(folder);
            if (root.Length > 0) roots.Add(root);
        }
        if (boothRoot.Length > 0 && !roots.Contains(boothRoot, StringComparer.OrdinalIgnoreCase))
            roots.Add(boothRoot);

        var containers = new Dictionary<string, ContainerBuild>(StringComparer.OrdinalIgnoreCase);
        var standalone = new List<ResourceRow>();
        var physical = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scannedPackages = new List<string>();
        var scannedArchives = new List<string>();

        foreach (var root in roots.Where(Directory.Exists))
            ScanRoot(root, containers, standalone, physical, scannedPackages, scannedArchives, ct);

        foreach (var added in settings.UnityPackageFiles.Distinct(StringComparer.OrdinalIgnoreCase))
            ScanAddedFile(added, containers, standalone, physical, scannedPackages, scannedArchives, ct);

        var rows = new List<ResourceRow>(standalone);
        foreach (var container in containers.Values)
        {
            var row = BuildContainer(container);
            if (row is not null) rows.Add(row);
        }

        foreach (var row in rows)
            ApplyOrigin(row, origins, boothRoot);

        _db.PruneScanCaches(scannedPackages, scannedArchives);

        return rows
            .OrderByDescending(row => row.LastModified)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ScanRoot(string root, Dictionary<string, ContainerBuild> containers, List<ResourceRow> standalone,
        HashSet<string> physical, List<string> scannedPackages, List<string> scannedArchives, CancellationToken ct)
    {
        string[] entries;
        try { entries = Directory.GetFileSystemEntries(root); }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }

        foreach (var entry in entries.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(entry);

            if (IsPackage(name))
            {
                var full = Path.GetFullPath(entry);
                scannedPackages.Add(full);
                if (!physical.Add(full)) continue;

                var leaf = BuildLeaf(full, null, (0, DateTime.MinValue), string.Empty, string.Empty);
                if (leaf is not null) standalone.Add(leaf);
                continue;
            }

            if (IsArchive(name))
            {
                var full = Path.GetFullPath(entry);
                scannedArchives.Add(full);

                var container = Container(containers, ProductKey(full));
                container.ZipPath = full;
                AddArchiveLeaves(containers, ProductKey(full), full, string.Empty, string.Empty, 0, ct);
                continue;
            }

            if (Directory.Exists(entry))
            {
                var full = Path.GetFullPath(entry);
                var container = Container(containers, ProductKey(full));
                container.FolderPath = full;
                AddFolderLeaves(containers, ProductKey(full), full, string.Empty, physical, ct);
            }
        }
    }

    private void ScanAddedFile(string path, Dictionary<string, ContainerBuild> containers, List<ResourceRow> standalone,
        HashSet<string> physical, List<string> scannedPackages, List<string> scannedArchives, CancellationToken ct)
    {
        if (!File.Exists(path)) return;

        var full = Path.GetFullPath(path);
        var name = Path.GetFileName(full);

        if (IsPackage(name))
        {
            scannedPackages.Add(full);
            if (!physical.Add(full)) return;

            var leaf = BuildLeaf(full, null, (0, DateTime.MinValue), string.Empty, string.Empty);
            if (leaf is not null) standalone.Add(leaf);
            return;
        }

        if (IsArchive(name))
        {
            scannedArchives.Add(full);
            Container(containers, ProductKey(full)).ZipPath = full;
            AddArchiveLeaves(containers, ProductKey(full), full, string.Empty, string.Empty, 0, ct);
        }
    }

    private void AddFolderLeaves(Dictionary<string, ContainerBuild> containers, string containerKey, string folder,
        string relativePrefix, HashSet<string> physical, CancellationToken ct)
    {
        string[] entries;
        try { entries = Directory.GetFileSystemEntries(folder); }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }

        foreach (var entry in entries.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(entry);
            var relative = JoinRelative(relativePrefix, name);

            if (Directory.Exists(entry))
            {
                AddFolderLeaves(containers, containerKey, Path.GetFullPath(entry), relative, physical, ct);
                continue;
            }

            if (IsPackage(name))
            {
                var full = Path.GetFullPath(entry);
                physical.Add(full);
                AddLeaf(containers, containerKey, full, null, (0, DateTime.MinValue), relative);
                continue;
            }

            if (IsArchive(name))
                AddArchiveLeaves(containers, containerKey, Path.GetFullPath(entry), relative, relative, 1, ct);
        }
    }

    private void AddArchiveLeaves(Dictionary<string, ContainerBuild> containers, string containerKey, string archivePath,
        string relativePrefix, string entryPrefix, int depth, CancellationToken ct)
    {
        var file = new FileInfo(archivePath);
        if (!file.Exists || depth > MaxArchiveDepth) return;

        try
        {
            using var zip = ZipFile.OpenRead(archivePath);

            foreach (var entry in zip.Entries)
            {
                ct.ThrowIfCancellationRequested();

                var name = Path.GetFileName(entry.FullName);
                if (name.Length == 0 || InJunk(entry.FullName)) continue;

                var entrySpec = Prefix(entryPrefix, entry.FullName);
                var relative = JoinRelative(relativePrefix, entry.FullName);

                if (IsPackage(name))
                {
                    AddLeaf(containers, containerKey, archivePath, entrySpec, Stamp(entry), relative);
                    continue;
                }

                if (!IsArchive(name) || depth >= MaxArchiveDepth || entry.Length > MaxNestedArchiveBytes) continue;

                if (!TryReadNested(entry, out var bytes)) continue;

                foreach (var nested in OpenNested(bytes))
                {
                    var nestedName = Path.GetFileName(nested.FullName);
                    if (nestedName.Length == 0 || InJunk(nested.FullName) || !IsPackage(nestedName)) continue;

                    AddLeaf(containers, containerKey, archivePath, Prefix(entrySpec, nested.FullName),
                        Stamp(nested), JoinRelative(relative, nested.FullName));
                }
            }
        }
        catch (IOException) { }
        catch (InvalidDataException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static (long Size, DateTime Modified) Stamp(ZipArchiveEntry entry) =>
        (entry.Length, entry.LastWriteTime.DateTime);

    private static IEnumerable<ZipArchiveEntry> OpenNested(byte[] bytes)
    {
        var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read, leaveOpen: false);
        try
        {
            foreach (var entry in archive.Entries) yield return entry;
        }
        finally
        {
            archive.Dispose();
        }
    }

    private static bool TryReadNested(ZipArchiveEntry entry, out byte[] bytes)
    {
        bytes = [];
        try
        {
            using var source = entry.Open();
            using var buffer = new MemoryStream((int)Math.Min(entry.Length, MaxNestedArchiveBytes));
            source.CopyTo(buffer);
            bytes = buffer.ToArray();
            return bytes.Length > 0;
        }
        catch (IOException) { return false; }
        catch (InvalidDataException) { return false; }
    }

    private void AddLeaf(Dictionary<string, ContainerBuild> containers, string containerKey, string filePath,
        string? entrySpec, (long Size, DateTime Modified) stamp, string relative)
    {
        var container = Container(containers, containerKey);
        var name = Path.GetFileName(entrySpec is null ? filePath : entrySpec.Split('!')[^1]);

        if (!IsPackage(name)) return;

        var identity = Identity(name, entrySpec is null ? PhysicalSize(filePath) : stamp.Size);

        if (entrySpec is not null)
        {
            // 同一个包既在压缩包里又以解包后的文件存在时，只保留磁盘上那份。
            if (container.Leaves.ContainsKey(relative) || container.Physical.Contains(identity)) return;
        }
        else
        {
            container.Physical.Add(identity);
        }

        var leaf = BuildLeaf(filePath, entrySpec, stamp, containerKey, relative);
        if (leaf is null) return;

        container.Leaves[relative] = leaf;
    }

    private static string Identity(string name, long size) => $"{name}|{size}";

    private static long PhysicalSize(string path)
    {
        try { return new FileInfo(path).Length; }
        catch (IOException) { return -1; }
        catch (UnauthorizedAccessException) { return -1; }
    }

    private ResourceRow? BuildLeaf(string filePath, string? entrySpec, (long Size, DateTime Modified) stamp,
        string containerKey, string relative)
    {
        var name = entrySpec is null
            ? Path.GetFileName(filePath)
            : Path.GetFileName(entrySpec.Split('!')[^1]);

        if (!IsPackage(name)) return null;

        var stem = ParseStem(Path.GetFileNameWithoutExtension(name));

        long size;
        DateTime modified;

        if (entrySpec is null)
        {
            try
            {
                var info = Describe(filePath);
                size = info.Size;
                modified = info.LastModified;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
        else
        {
            size = stamp.Size;
            modified = stamp.Modified;
        }

        return new ResourceRow
        {
            Key = entrySpec is null ? filePath : containerKey + "!" + relative,
            Kind = "unitypackage",
            Name = stem.Name,
            Version = stem.Version,
            FilePath = filePath,
            ArchivePath = entrySpec is null ? string.Empty : filePath,
            ArchiveEntry = entrySpec ?? string.Empty,
            ContainerPath = containerKey,
            RelativePath = relative,
            Size = size,
            LastModified = modified
        };
    }

    private ResourceRow? BuildContainer(ContainerBuild container)
    {
        if (container.Leaves.Count == 0) return null;

        var zip = container.ZipPath;
        var folder = container.FolderPath;
        var stem = ParseStem(Path.GetFileNameWithoutExtension(zip ?? folder ?? container.Key));

        long size;
        DateTime modified;

        if (zip is not null)
        {
            try
            {
                var info = new FileInfo(zip);
                size = info.Length;
                modified = info.LastWriteTime;
            }
            catch (IOException)
            {
                size = 0;
                modified = DateTime.MinValue;
            }
        }
        else
        {
            size = container.Leaves.Values.Sum(leaf => leaf.Size);
            try { modified = Directory.GetLastWriteTime(folder ?? container.Key); }
            catch (IOException) { modified = DateTime.MinValue; }
        }

        var children = container.Leaves.Values
            .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ResourceRow
        {
            Key = container.Key,
            Kind = zip is not null ? "archive" : "folder",
            Name = stem.Name,
            Version = stem.Version,
            FilePath = zip ?? folder ?? container.Key,
            Size = size,
            LastModified = modified,
            ChildCount = children.Count,
            Children = children
        };
    }

    private static ContainerBuild Container(Dictionary<string, ContainerBuild> containers, string key)
    {
        if (!containers.TryGetValue(key, out var container))
        {
            container = new ContainerBuild(key);
            containers[key] = container;
        }

        return container;
    }

    private static string ProductKey(string path)
    {
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full) ?? string.Empty;
        var name = Path.GetFileName(full);

        if (IsArchive(name))
            name = Path.GetFileNameWithoutExtension(name);
        else if (name.EndsWith(ExtractedSuffix, StringComparison.OrdinalIgnoreCase))
            name = name[..^ExtractedSuffix.Length];
        else if (name.EndsWith("extracted", StringComparison.OrdinalIgnoreCase))
            name = name[..^"extracted".Length].TrimEnd('-', '_', ' ');

        return Key(Path.Combine(directory, name));
    }

    private static string Prefix(string head, string tail) =>
        head.Length == 0 ? tail.Replace('\\', '/') : head + "!" + tail.Replace('\\', '/');

    private static string JoinRelative(string prefix, string tail)
    {
        var normalized = tail.Replace('\\', '/').TrimStart('/');
        return prefix.Length == 0 ? normalized : prefix.TrimEnd('/') + "/" + normalized;
    }

    private static bool InJunk(string entry) =>
        entry.Contains("__MACOSX", StringComparison.OrdinalIgnoreCase) ||
        entry.Contains("/.", StringComparison.OrdinalIgnoreCase);

    private static bool IsPackage(string name) => name.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase);

    private static bool IsArchive(string name) => name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    private sealed class ContainerBuild(string key)
    {
        public string Key { get; } = key;

        public string? ZipPath { get; set; }

        public string? FolderPath { get; set; }

        public Dictionary<string, ResourceRow> Leaves { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Physical { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
