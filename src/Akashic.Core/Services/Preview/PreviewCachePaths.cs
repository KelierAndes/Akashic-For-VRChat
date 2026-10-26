namespace Akashic.Core.Services.Preview;

public static class PreviewCachePaths
{
    private static readonly object Gate = new();
    private static readonly string[] GcSections = ["packages", "builds", "archives", "textures"];

    public const long QuotaBytes = 8L * 1024 * 1024 * 1024;

    private static string _root = string.Empty;

    public static string Root
    {
        get
        {
            lock (Gate)
            {
                if (_root.Length > 0) return _root;
                _root = Ensure(DefaultDirectory());
                return _root;
            }
        }
    }

    public static string Versioned(string section, string version) => VersionedUnder(Root, section, version);

    public static string VersionedUnder(string root, string section, string version)
    {
        var container = Path.Combine(root, section);
        var current = Path.Combine(container, version);
        var family = FamilyOf(version);

        try
        {
            Directory.CreateDirectory(current);

            foreach (var sibling in Directory.EnumerateDirectories(container))
            {
                var name = Path.GetFileName(sibling);
                if (string.Equals(sibling, current, StringComparison.OrdinalIgnoreCase)) continue;
                if (family.Length == 0 || FamilyOf(name) != family) continue;
                try { Directory.Delete(sibling, recursive: true); } catch (Exception) { }
            }
        }
        catch (Exception)
        {
        }

        return current;
    }

    private static string FamilyOf(string version)
    {
        var cut = version.LastIndexOf("-v", StringComparison.Ordinal);
        return cut <= 0 ? version : version[..cut];
    }

    public static void EnforceQuota(long quota = QuotaBytes)
    {
        var total = MeasureBytes(Root);
        if (total <= quota) return;

        var entries = new List<(string Path, long Size, DateTime Modified)>();

        foreach (var section in GcSections)
        {
            var container = Path.Combine(Root, section);
            if (!Directory.Exists(container)) continue;

            foreach (var version in Enumerate(container, Directory.EnumerateDirectories))
                foreach (var leaf in Enumerate(version, Directory.EnumerateDirectories))
                    entries.Add((leaf, MeasureBytes(leaf), WriteTime(leaf)));
        }

        foreach (var entry in entries.OrderBy(item => item.Modified))
        {
            if (total <= quota) break;
            try
            {
                Directory.Delete(entry.Path, recursive: true);
                total -= entry.Size;
            }
            catch (Exception)
            {
            }
        }
    }

    private static IEnumerable<string> Enumerate(string path, Func<string, IEnumerable<string>> selector)
    {
        try { return selector(path); }
        catch (Exception) { return []; }
    }

    private static DateTime WriteTime(string path)
    {
        try { return Directory.GetLastWriteTime(path); }
        catch (Exception) { return DateTime.MinValue; }
    }

    public static string Configure(string? configured)
    {
        lock (Gate)
        {
            _root = Ensure(string.IsNullOrWhiteSpace(configured)
                ? DefaultDirectory()
                : configured);
            return _root;
        }
    }

    public static string DefaultDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "cache", "preview");

    public static string LegacyDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Akashic", "preview");

    public static long MeasureBytes(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return 0;

        long total = 0;
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            try
            {
                foreach (var file in Directory.EnumerateFiles(current))
                    try { total += new FileInfo(file).Length; } catch (Exception) { }
                foreach (var child in Directory.EnumerateDirectories(current))
                    stack.Push(child);
            }
            catch (Exception)
            {
            }
        }

        return total;
    }

    public static void Clear(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;

        foreach (var child in Directory.EnumerateDirectories(root))
            Directory.Delete(child, recursive: true);
        foreach (var file in Directory.EnumerateFiles(root))
            File.Delete(file);
    }

    private static string Ensure(string path)
    {
        var full = Path.GetFullPath(path);
        if (Writable(full)) return full;

        var legacy = LegacyDirectory();
        return Writable(legacy) ? legacy : Path.GetTempPath();
    }

    private static bool Writable(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var probe = Path.Combine(path, ".write-probe");
            File.WriteAllText(probe, "1");
            File.Delete(probe);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
