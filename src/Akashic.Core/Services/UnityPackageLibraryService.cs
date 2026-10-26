using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Akashic.Core.Data;
using Akashic.Core.Models;

namespace Akashic.Core.Services;

public partial class UnityPackageLibraryService : IUnityPackageLibraryService
{
    private readonly string _settingsPath;
    private readonly AkashicDatabase _db;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public UnityPackageLibraryService(string? settingsPath = null, string? ledgerPath = null,
        AkashicDatabase? database = null)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Akashic");

        _settingsPath = settingsPath ?? Path.Combine(dir, "settings.json");
        _db = database ?? new AkashicDatabase(
            ledgerPath is null ? Path.GetDirectoryName(_settingsPath) : Path.GetDirectoryName(ledgerPath));
    }

    [GeneratedRegex(@"^(?<name>.+?)[\s_\-]*\[\s*(?:(?:v|ver|version)\.?\s*)?(?<version>\d+(?:\.\d+){1,3}[0-9A-Za-z.\-+]*)\s*\]$", RegexOptions.IgnoreCase)]
    private static partial Regex BracketedVersionRegex();

    [GeneratedRegex(@"^(?<name>.+?)[\s_\-]*(?:(?:v|ver|version)\.?\s*)?(?<version>\d+(?:\.\d+){1,3}[0-9A-Za-z.\-+]*)$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingVersionRegex();

    public List<string> GetLibraryFolders() => LoadSettings().UnityPackageLibraryPaths;

    public List<string> GetAddedFiles() => LoadSettings().UnityPackageFiles;

    public async Task AddLibraryFolderAsync(string folderPath, CancellationToken ct = default)
    {
        var full = Path.GetFullPath(folderPath);
        var settings = LoadSettings();

        if (!settings.UnityPackageLibraryPaths.Contains(full, StringComparer.OrdinalIgnoreCase))
        {
            settings.UnityPackageLibraryPaths.Add(full);
            await SaveSettingsAsync(settings, ct);
        }
    }

    public async Task RemoveLibraryFolderAsync(string folderPath, CancellationToken ct = default)
    {
        var settings = LoadSettings();
        settings.UnityPackageLibraryPaths.RemoveAll(p => PathEquals(p, folderPath));
        await SaveSettingsAsync(settings, ct);
    }

    public async Task AddPackageFileAsync(string filePath, PackageOrigin origin = PackageOrigin.Local,
        CancellationToken ct = default)
    {
        var full = Path.GetFullPath(filePath);
        var settings = LoadSettings();

        var covered = settings.UnityPackageLibraryPaths.Any(folder =>
            full.StartsWith(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase));

        if (!covered && !settings.UnityPackageFiles.Contains(full, StringComparer.OrdinalIgnoreCase))
            settings.UnityPackageFiles.Add(full);

        if (origin == PackageOrigin.Local)
            settings.UnityPackageOrigins.Remove(Key(full));
        else
            settings.UnityPackageOrigins[Key(full)] = origin;

        await SaveSettingsAsync(settings, ct);
    }

    public async Task RemovePackageFileAsync(string filePath, CancellationToken ct = default)
    {
        var settings = LoadSettings();
        settings.UnityPackageFiles.RemoveAll(p => PathEquals(p, filePath));
        settings.UnityPackageOrigins.Remove(Key(Path.GetFullPath(filePath)));
        await SaveSettingsAsync(settings, ct);
    }

    public async Task<List<UnityPackageFileInfo>> ScanAsync(CancellationToken ct = default)
    {
        var settings = LoadSettings();
        var candidates = new List<string>();

        foreach (var folder in settings.UnityPackageLibraryPaths)
            candidates.AddRange(EnumeratePackages(folder));

        candidates.AddRange(settings.UnityPackageFiles.Where(File.Exists));

        var origins = new Dictionary<string, PackageOrigin>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, origin) in settings.UnityPackageOrigins)
            if (!string.IsNullOrWhiteSpace(path))
                origins[Key(path)] = origin;

        var boothRoot = NormalizeRoot(settings.BoothDownloadDirectory);

        var result = new List<UnityPackageFileInfo>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();

            UnityPackageFileInfo info;
            try
            {
                info = Describe(path);
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            if (origins.TryGetValue(Key(path), out var origin))
                info.Origin = origin;
            else if (boothRoot.Length > 0 && UnderRoot(path, boothRoot))
                info.Origin = PackageOrigin.Booth;

            if (!seenIds.Add(info.PackageId)) continue;
            result.Add(info);
        }

        return result
            .OrderByDescending(f => f.LastModified)
            .ThenBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> EnumeratePackages(string folder)
    {
        if (!Directory.Exists(folder)) yield break;

        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(folder));

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            string[] files;
            try
            {
                files = Directory.GetFiles(current, "*.unitypackage");
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            foreach (var file in files)
                yield return file;

            try
            {
                foreach (var sub in Directory.GetDirectories(current))
                    pending.Push(sub);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public UnityPackageFileInfo DescribePackage(string filePath) => Describe(filePath);

    public async Task<List<string>> ExtractArchiveAsync(string archivePath, IReadOnlyList<string>? entries = null,
        CancellationToken ct = default)
    {
        var full = Path.GetFullPath(archivePath);
        if (!File.Exists(full)) throw new FileNotFoundException("压缩包不存在。", full);

        var root = NormalizeRoot(Path.Combine(
            Path.GetDirectoryName(full) ?? ".", Path.GetFileNameWithoutExtension(full) + "-extracted"));
        Directory.CreateDirectory(root);

        var wanted = entries is { Count: > 0 }
            ? new HashSet<string>(entries, StringComparer.OrdinalIgnoreCase)
            : null;

        var extracted = new List<string>();

        using (var archive = ZipFile.OpenRead(full))
        {
            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (!entry.FullName.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase)) continue;
                if (entry.FullName.Contains("__MACOSX", StringComparison.OrdinalIgnoreCase)) continue;
                if (wanted is not null && !wanted.Contains(entry.FullName)) continue;

                var name = entry.FullName.Replace('\\', '/').TrimStart('/');
                var destination = Path.GetFullPath(Path.Combine(root, name));
                if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    continue;

                var parent = Path.GetDirectoryName(destination);
                if (parent is not null) Directory.CreateDirectory(parent);

                entry.ExtractToFile(destination, overwrite: true);
                extracted.Add(destination);
            }
        }

        var settings = LoadSettings();
        var origin = PackageOrigin.Local;
        if (settings.UnityPackageOrigins.TryGetValue(Key(full), out var stored)) origin = stored;
        else if (UnderRoot(full, NormalizeRoot(settings.BoothDownloadDirectory))) origin = PackageOrigin.Booth;

        foreach (var path in extracted)
            await AddPackageFileAsync(path, origin, ct);

        return extracted;
    }

    private static void ApplyOrigin(ResourceRow row, Dictionary<string, PackageOrigin> origins, string boothRoot)
    {
        var origin = PackageOrigin.Local;
        if (origins.TryGetValue(Key(row.Key), out var stored)) origin = stored;
        else if (boothRoot.Length > 0 && UnderRoot(row.FilePath, boothRoot)) origin = PackageOrigin.Booth;

        row.Origin = origin.ToString();
        row.OriginLabel = origin.Display();

        foreach (var child in row.Children)
        {
            child.Origin = row.Origin;
            child.OriginLabel = row.OriginLabel;
        }
    }

    private UnityPackageFileInfo Describe(string path)
    {
        var file = new FileInfo(path);
        var cached = _db.GetPackageFile(file.FullName, file.Length, file.LastWriteTime);
        if (cached is not null) return cached;

        var parsed = ParseStem(Path.GetFileNameWithoutExtension(path));
        var info = new UnityPackageFileInfo
        {
            FilePath = file.FullName,
            DisplayName = parsed.Name,
            Version = parsed.Version,
            Size = file.Length,
            LastModified = file.LastWriteTime
        };

        _db.PutPackageFile(info);
        return info;
    }

    private static (string Name, string Version) ParseStem(string stem)
    {
        var trimmed = stem.Trim().Trim('_', '-', ' ').Trim();
        if (trimmed.Length == 0) trimmed = stem;

        var match = BracketedVersionRegex().Match(trimmed);
        if (!match.Success) match = TrailingVersionRegex().Match(trimmed);

        return match.Success
            ? (match.Groups["name"].Value.Trim().Trim('_', '-', ' '), match.Groups["version"].Value)
            : (trimmed, string.Empty);
    }

    private static IEnumerable<string> EnumerateByPattern(string folder, string pattern)
    {
        if (!Directory.Exists(folder)) yield break;

        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(folder));

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            string[] files;
            try
            {
                files = Directory.GetFiles(current, pattern);
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            foreach (var file in files)
                yield return file;

            try
            {
                foreach (var sub in Directory.GetDirectories(current))
                    pending.Push(sub);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public Task<List<UnityPackageInstallRecord>> GetRecordsAsync(string projectPath, CancellationToken ct = default)
    {
        var ledger = _db.LoadInstallLedger();
        var records = ledger.TryGetValue(NormalizeKey(projectPath), out var list) ? list : [];
        return Task.FromResult(records);
    }

    public Task SaveRecordAsync(string projectPath, UnityPackageInstallRecord record, CancellationToken ct = default)
    {
        var ledger = _db.LoadInstallLedger();
        var key = NormalizeKey(projectPath);

        if (!ledger.TryGetValue(key, out var records))
            ledger[key] = records = [];

        records.RemoveAll(r => string.Equals(r.PackageId, record.PackageId, StringComparison.OrdinalIgnoreCase));
        records.Add(record);

        _db.SaveInstallRecords(key, records);
        return Task.CompletedTask;
    }

    public Task RemoveRecordAsync(string projectPath, string packageId, CancellationToken ct = default)
    {
        var ledger = _db.LoadInstallLedger();
        var key = NormalizeKey(projectPath);

        if (ledger.TryGetValue(key, out var records))
        {
            records.RemoveAll(r => string.Equals(r.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
            _db.SaveInstallRecords(key, records);
        }

        return Task.CompletedTask;
    }

    private static string NormalizeKey(string projectPath) =>
        Path.GetFullPath(projectPath).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();

    private static string Key(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string NormalizeRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) return string.Empty;
        try
        {
            return Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static bool UnderRoot(string path, string root)
    {
        try
        {
            var full = Path.GetFullPath(path);
            return full.Length > root.Length
                   && full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                   && (full[root.Length] == Path.DirectorySeparatorChar || full[root.Length] == Path.AltDirectorySeparatorChar);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool PathEquals(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch {  }

        return new AppSettings();
    }

    private async Task SaveSettingsAsync(AppSettings settings, CancellationToken ct)
    {
        EnsureDirectory(_settingsPath);

        var current = LoadSettings();
        current.UnityPackageLibraryPaths = settings.UnityPackageLibraryPaths;
        current.UnityPackageFiles = settings.UnityPackageFiles;
        current.UnityPackageOrigins = settings.UnityPackageOrigins;

        var json = JsonSerializer.Serialize(current, JsonOptions);
        await File.WriteAllTextAsync(_settingsPath, json, ct);
    }


    private static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }
}
