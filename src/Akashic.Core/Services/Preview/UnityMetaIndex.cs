using System.Text.RegularExpressions;

namespace Akashic.Core.Services.Preview;

public sealed partial class UnityMetaIndex
{
    private readonly string _root;
    private readonly Dictionary<string, string> _guidToPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<long, string>> _subAssets = new(StringComparer.OrdinalIgnoreCase);

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

    [GeneratedRegex(@"-\s*first:\s*\r?\n\s*(\d+):\s*(-?\d+)\s*\r?\n\s*second:\s*(.+?)\s*$",
        RegexOptions.Multiline)]
    private static partial Regex InternalIdRegex();

    [GeneratedRegex(@"^\s*(-?\d+):\s*(.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex RecycleRegex();
}
