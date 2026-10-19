using System.Text.Json;
using System.Text.RegularExpressions;
using Akashic.Core.Models;

namespace Akashic.Core.Services;

public partial class UnityPackageLibraryService : IUnityPackageLibraryService
{
    private readonly string _settingsPath;
    private readonly string _ledgerPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public UnityPackageLibraryService(string? settingsPath = null, string? ledgerPath = null)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Akashic");

        _settingsPath = settingsPath ?? Path.Combine(dir, "settings.json");
        _ledgerPath = ledgerPath ?? Path.Combine(dir, "unitypackage-ledger.json");
    }

    [GeneratedRegex(@"^(?<name>.+?)[\s_\-]*\[v?(?<version>\d+(?:\.\d+){1,3}(?:[-+][0-9A-Za-z.\-]+)?)\]$", RegexOptions.IgnoreCase)]
    private static partial Regex BracketedVersionRegex();

    [GeneratedRegex(@"^(?<name>.+?)[\s_\-]*v?(?<version>\d+(?:\.\d+){1,3}(?:[-+][0-9A-Za-z.\-]+)?)$", RegexOptions.IgnoreCase)]
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

    private static UnityPackageFileInfo Describe(string path)
    {
        var file = new FileInfo(path);
        var stem = Path.GetFileNameWithoutExtension(path);
        var match = BracketedVersionRegex().Match(stem);
        if (!match.Success) match = TrailingVersionRegex().Match(stem);

        return new UnityPackageFileInfo
        {
            FilePath = file.FullName,
            DisplayName = match.Success ? match.Groups["name"].Value.Trim() : stem,
            Version = match.Success ? match.Groups["version"].Value : string.Empty,
            Size = file.Length,
            LastModified = file.LastWriteTime
        };
    }

    public Task<List<UnityPackageInstallRecord>> GetRecordsAsync(string projectPath, CancellationToken ct = default)
    {
        var ledger = LoadLedger();
        var records = ledger.TryGetValue(NormalizeKey(projectPath), out var list) ? list : [];
        return Task.FromResult(records);
    }

    public async Task SaveRecordAsync(string projectPath, UnityPackageInstallRecord record, CancellationToken ct = default)
    {
        var ledger = LoadLedger();
        var key = NormalizeKey(projectPath);

        if (!ledger.TryGetValue(key, out var records))
            ledger[key] = records = [];

        records.RemoveAll(r => string.Equals(r.PackageId, record.PackageId, StringComparison.OrdinalIgnoreCase));
        records.Add(record);

        await SaveLedgerAsync(ledger, ct);
    }

    public async Task RemoveRecordAsync(string projectPath, string packageId, CancellationToken ct = default)
    {
        var ledger = LoadLedger();
        var key = NormalizeKey(projectPath);

        if (ledger.TryGetValue(key, out var records))
        {
            records.RemoveAll(r => string.Equals(r.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
            if (records.Count == 0) ledger.Remove(key);
            await SaveLedgerAsync(ledger, ct);
        }
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

    private Dictionary<string, List<UnityPackageInstallRecord>> LoadLedger()
    {
        try
        {
            if (File.Exists(_ledgerPath))
            {
                var json = File.ReadAllText(_ledgerPath);
                return JsonSerializer.Deserialize<Dictionary<string, List<UnityPackageInstallRecord>>>(json, JsonOptions) ?? [];
            }
        }
        catch {  }

        return [];
    }

    private async Task SaveLedgerAsync(Dictionary<string, List<UnityPackageInstallRecord>> ledger, CancellationToken ct)
    {
        EnsureDirectory(_ledgerPath);

        var json = JsonSerializer.Serialize(ledger, JsonOptions);
        await File.WriteAllTextAsync(_ledgerPath, json, ct);
    }

    private static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }
}
