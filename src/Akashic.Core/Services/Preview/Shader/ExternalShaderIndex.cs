using System.Text.Json;
using Akashic.Core.Models;

namespace Akashic.Core.Services.Preview.Shader;

public sealed class ExternalShaderIndex
{
    private static readonly Lazy<ExternalShaderIndex> SharedIndex = new(() => new ExternalShaderIndex());

    private readonly object _gate = new();
    private readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);
    private bool _built;

    public static ExternalShaderIndex Shared => SharedIndex.Value;

    public string? Resolve(string guid)
    {
        if (string.IsNullOrWhiteSpace(guid)) return null;
        EnsureBuilt();
        lock (_gate) return _map.TryGetValue(guid, out var path) && File.Exists(path) ? path : null;
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _map.Clear();
            _built = false;
        }
    }

    private void EnsureBuilt()
    {
        lock (_gate)
        {
            if (_built) return;
            _built = true;

            foreach (var root in Roots())
            {
                try
                {
                    ScanRoot(root);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private void ScanRoot(string root)
    {
        if (!Directory.Exists(root)) return;

        foreach (var package in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(package);
            if (name.EndsWith('~') || name.StartsWith('.')) continue;

            ScanFiles(package, SearchOption.TopDirectoryOnly);
            foreach (var folder in new[] { "Shader", "Shaders", "Assets", "Runtime" })
            {
                var path = Path.Combine(package, folder);
                if (Directory.Exists(path)) ScanFiles(path, SearchOption.AllDirectories);
            }
        }
    }

    private void ScanFiles(string folder, SearchOption option)
    {
        try
        {
            foreach (var meta in Directory.EnumerateFiles(folder, "*.shader.meta", option))
            {
                var shaderPath = meta[..^".meta".Length];
                if (!File.Exists(shaderPath)) continue;

                var guid = ReadGuid(meta);
                if (guid.Length == 0 || _map.ContainsKey(guid)) continue;
                _map[guid] = shaderPath;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string ReadGuid(string metaPath)
    {
        try
        {
            using var reader = new StreamReader(metaPath);
            for (var i = 0; i < 12; i++)
            {
                var line = reader.ReadLine();
                if (line is null) break;
                if (!line.StartsWith("guid:", StringComparison.OrdinalIgnoreCase)) continue;

                var value = line["guid:".Length..].Trim();
                return value.Length == 32 && value.All(Uri.IsHexDigit) ? value : string.Empty;
            }
        }
        catch (IOException)
        {
        }

        return string.Empty;
    }

    private static IEnumerable<string> Roots()
    {
        var settings = LoadSettings();
        foreach (var project in settings.ProjectPaths)
        {
            if (Directory.Exists(Path.Combine(project, "Packages")))
                yield return Path.Combine(project, "Packages");
            if (Directory.Exists(Path.Combine(project, "Library", "PackageCache")))
                yield return Path.Combine(project, "Library", "PackageCache");
        }
    }

    private static AppSettings LoadSettings()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Akashic", "settings.json");

        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
        }

        return new AppSettings();
    }
}
