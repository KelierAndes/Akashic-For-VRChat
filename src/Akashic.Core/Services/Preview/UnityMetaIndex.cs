using System.Globalization;
using System.Text.RegularExpressions;

namespace Akashic.Core.Services.Preview;

public sealed partial class UnityMetaIndex
{
    private readonly string _root;
    private readonly Dictionary<string, string> _guidToPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<long, string>> _subAssets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> _modelMaterials = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float[]> _textureWrap = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string>? _materialsByName;

    public UnityMetaIndex(string root)
    {
        _root = root;
        Build();
    }

    public string? Resolve(string? guid)
    {
        if (string.IsNullOrWhiteSpace(guid)) return null;
        if (!_guidToPath.TryGetValue(guid, out var relative)) return null;
        return Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    public float[] GetTextureWrap(string? texturePath)
    {
        if (string.IsNullOrWhiteSpace(texturePath)) return [0, 0];
        if (_textureWrap.TryGetValue(texturePath, out var cached)) return cached;

        var wrap = ReadTextureWrap(texturePath + ".meta");
        _textureWrap[texturePath] = wrap;
        return wrap;
    }

    private static float[] ReadTextureWrap(string metaPath)
    {
        var u = 0f;
        var v = 0f;
        try
        {
            if (!File.Exists(metaPath)) return [0, 0];
            foreach (var line in File.ReadLines(metaPath))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("wrapU:", StringComparison.Ordinal))
                {
                    float.TryParse(trimmed.AsSpan(6).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out u);
                }
                else if (trimmed.StartsWith("wrapV:", StringComparison.Ordinal))
                {
                    float.TryParse(trimmed.AsSpan(6).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
                    break;
                }
            }
        }
        catch (IOException)
        {
            return [0, 0];
        }

        return [u, v];
    }

    public string? ResolveModelMaterial(string modelPath, string? materialName)
    {
        if (string.IsNullOrWhiteSpace(materialName)) return null;

        var metaPath = modelPath + ".meta";
        if (!_modelMaterials.TryGetValue(metaPath, out var table))
        {
            table = ParseExternalMaterials(metaPath);
            _modelMaterials[metaPath] = table;
        }

        return table.TryGetValue(materialName, out var guid) ? Resolve(guid) : null;
    }

    public string? FindMaterialByName(string? materialName)
    {
        var key = Normalize(materialName);
        if (key.Length == 0) return null;

        var index = _materialsByName ??= BuildMaterialIndex();
        return index.TryGetValue(key, out var relative)
            ? Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar))
            : null;
    }

    private Dictionary<string, string> BuildMaterialIndex()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(_root)) return map;

        try
        {
            var files = Directory.EnumerateFiles(_root, "*.mat", SearchOption.AllDirectories)
                .OrderBy(path => path.Length)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                if (map.Count >= 8192) break;
                var key = Normalize(Path.GetFileNameWithoutExtension(file));
                if (key.Length == 0) continue;
                map.TryAdd(key, Path.GetRelativePath(_root, file).Replace('\\', '/'));
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return map;
    }

    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static Dictionary<string, string> ParseExternalMaterials(string metaPath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(metaPath)) return result;

        string text;
        try
        {
            text = File.ReadAllText(metaPath);
        }
        catch (IOException)
        {
            return result;
        }

        foreach (Match match in ExternalMaterialRegex().Matches(text))
        {
            var name = CleanName(match.Groups[1].Value);
            var guid = match.Groups[2].Value;
            if (name.Length > 0) result.TryAdd(name, guid);
        }

        return result;
    }

    public string? ResolveSubAsset(string? guid, long fileId)
    {
        if (string.IsNullOrWhiteSpace(guid)) return null;
        if (!_subAssets.TryGetValue(guid, out var table))
        {
            var path = Resolve(guid);
            table = path is null ? [] : ParseSubAssets(path + ".meta");
            _subAssets[guid] = table;
        }

        return table.TryGetValue(fileId, out var name) ? name : null;
    }

    private void Build()
    {
        if (!Directory.Exists(_root)) return;

        foreach (var meta in Directory.EnumerateFiles(_root, "*.meta", SearchOption.AllDirectories))
        {
            string? guid = null;

            try
            {
                using var reader = new StreamReader(meta);
                for (var i = 0; i < 60; i++)
                {
                    var line = reader.ReadLine();
                    if (line is null) break;

                    var match = GuidRegex().Match(line);
                    if (!match.Success) continue;

                    guid = match.Groups[1].Value;
                    break;
                }
            }
            catch (IOException)
            {
                continue;
            }

            if (guid is null) continue;

            var asset = meta[..^5];
            _guidToPath[guid] = Path.GetRelativePath(_root, asset).Replace('\\', '/');
        }
    }

    private static Dictionary<long, string> ParseSubAssets(string metaPath)
    {
        var result = new Dictionary<long, string>();
        if (!File.Exists(metaPath)) return result;

        string text;
        try
        {
            text = File.ReadAllText(metaPath);
        }
        catch (IOException)
        {
            return result;
        }

        var tableIndex = text.IndexOf("internalIDToNameTable:", StringComparison.Ordinal);
        if (tableIndex >= 0)
        {
            var block = text[tableIndex..];

            foreach (Match match in InternalIdRegex().Matches(block))
            {
                if (!long.TryParse(match.Groups[2].Value, out var fileId)) continue;
                var name = CleanName(match.Groups[3].Value);
                if (name.Length > 0) result.TryAdd(fileId, name);
            }
        }

        var recycleIndex = text.IndexOf("fileIDToRecycleName:", StringComparison.Ordinal);
        if (recycleIndex >= 0 && result.Count == 0)
        {
            var block = text[recycleIndex..];
            foreach (Match match in RecycleRegex().Matches(block))
            {
                if (!long.TryParse(match.Groups[1].Value, out var fileId)) continue;
                var name = CleanName(match.Groups[2].Value);
                if (name.Length > 0) result.TryAdd(fileId, name);
            }
        }

        return result;
    }

    private static string CleanName(string raw)
    {
        var text = raw.Trim();
        if (text.StartsWith('\'') && text.EndsWith('\'')) text = text[1..^1];
        if (text.StartsWith('"') && text.EndsWith('"')) text = text[1..^1];
        return text.Trim();
    }

    [GeneratedRegex(@"^guid:\s*([0-9a-fA-F]{32})\b")]
    private static partial Regex GuidRegex();

    [GeneratedRegex(
        @"(?m)^  - first:\s*\r?\n(?:[^\n]*\r?\n)*?[ \t]+name:\s*(.+?)\s*\r?\n[ \t]+second:\s*\{[^}]*?guid:\s*([0-9a-fA-F]{32})")]
    private static partial Regex ExternalMaterialRegex();

    [GeneratedRegex(@"-\s*first:\s*\r?\n\s*(\d+):\s*(-?\d+)\s*\r?\n\s*second:\s*(.+?)\s*$",
        RegexOptions.Multiline)]
    private static partial Regex InternalIdRegex();

    [GeneratedRegex(@"^\s*(-?\d+):\s*(.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex RecycleRegex();
}
