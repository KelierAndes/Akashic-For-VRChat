using System.Text.Json;
using System.Text.RegularExpressions;
using Akashic.Core.Models;

namespace Akashic.Core.Services;

public sealed class BuiltinTemplate
{
    public required string Key { get; init; }

    public required string Name { get; init; }

    public required string Kind { get; init; }

    public required string UnityVersion { get; init; }

    public string Directory { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, string> PackageRanges { get; init; } =
        new Dictionary<string, string>();

    public string Description { get; init; } = string.Empty;
}

public static partial class BuiltinTemplates
{
    public const string Base = "com.vrchat.base";
    public const string Worlds = "com.vrchat.worlds";
    public const string Avatars = "com.vrchat.avatars";
    public const string Resolver = "com.vrchat.core.vpm-resolver";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static string VccTemplatesRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VRChatCreatorCompanion", "VRCTemplates");

    public static List<BuiltinTemplate> Discover()
    {
        var fromVcc = DiscoverFromVcc();
        return fromVcc.Count > 0 ? fromVcc : Fallback.ToList();
    }

    public static BuiltinTemplate? Find(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : Discover().FirstOrDefault(t => t.Key == key);

    private static List<BuiltinTemplate> DiscoverFromVcc()
    {
        var found = new List<BuiltinTemplate>();

        try
        {
            if (!Directory.Exists(VccTemplatesRoot)) return found;

            foreach (var folder in Directory.EnumerateDirectories(VccTemplatesRoot))
            {
                var template = ReadTemplate(folder);
                if (template is not null) found.Add(template);
            }
        }
        catch (Exception)
        {
            return [];
        }

        return found
            .OrderBy(t => t.Kind, StringComparer.Ordinal)
            .ThenByDescending(t => t.UnityVersion, StringComparer.Ordinal)
            .ToList();
    }

    private static BuiltinTemplate? ReadTemplate(string folder)
    {
        var manifest = Path.Combine(folder, "Packages", "manifest.json");
        if (!File.Exists(manifest)) return null;

        var name = Path.GetFileName(folder);
        var unity = ReadProjectVersion(folder);
        var ranges = ReadVpmRanges(folder);

        var (displayName, description) = ReadPackageInfo(folder, name);

        return new BuiltinTemplate
        {
            Key = "vcc:" + name,
            Name = $"{displayName} ({unity})",
            Kind = KindOf(name),
            UnityVersion = unity,
            Directory = folder,
            Description = description,
            PackageRanges = ranges
        };
    }

    private static string ReadProjectVersion(string folder)
    {
        try
        {
            var file = Path.Combine(folder, "ProjectSettings", "ProjectVersion.txt");
            if (!File.Exists(file)) return "未知版本";

            var match = ProjectVersion().Match(File.ReadAllText(file));
            return match.Success ? match.Groups[1].Value.Trim() : "未知版本";
        }
        catch (Exception)
        {
            return "未知版本";
        }
    }

    private static Dictionary<string, string> ReadVpmRanges(string folder)
    {
        var ranges = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            var file = Path.Combine(folder, "Packages", "vpm-manifest.json");
            if (!File.Exists(file)) return ranges;

            using var doc = JsonDocument.Parse(File.ReadAllText(file));

            if (!doc.RootElement.TryGetProperty("dependencies", out var deps) ||
                deps.ValueKind != JsonValueKind.Object)
                return ranges;

            foreach (var dep in deps.EnumerateObject())
                ranges[dep.Name] = ReadRange(dep.Value);
        }
        catch (Exception)
        {
            return [];
        }

        if (!ranges.ContainsKey(Resolver)) ranges[Resolver] = "*";

        return ranges;
    }

    private static string ReadRange(JsonElement dependency)
    {
        if (dependency.ValueKind != JsonValueKind.Object) return "*";

        if (dependency.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String)
            return version.GetString() ?? "*";

        return "*";
    }

    private static (string Name, string Description) ReadPackageInfo(string folder, string fallbackName)
    {
        try
        {
            var file = Path.Combine(folder, "package.json");
            if (!File.Exists(file)) return (fallbackName, string.Empty);

            var info = JsonSerializer.Deserialize<TemplatePackageInfo>(File.ReadAllText(file), JsonOptions);

            return (string.IsNullOrWhiteSpace(info?.DisplayName) ? fallbackName : info.DisplayName!,
                    info?.Description ?? string.Empty);
        }
        catch (Exception)
        {
            return (fallbackName, string.Empty);
        }
    }

    private static string KindOf(string folder) => folder.ToLowerInvariant() switch
    {
        var f when f.Contains("world") => "世界",
        var f when f.Contains("avatar") => "虚拟形象",
        _ => "空白"
    };

    private static readonly BuiltinTemplate[] Fallback =
    [
        new()
        {
            Key = "built-in:worlds-2022",
            Name = "VRChat 世界 (2022.3.22f1)",
            Kind = "世界",
            UnityVersion = "2022.3.22f1",
            PackageRanges = new Dictionary<string, string>
            {
                [Base] = "~3.10.x", [Worlds] = "~3.10.x", [Resolver] = "*"
            }
        },
        new()
        {
            Key = "built-in:avatars-2022",
            Name = "VRChat 虚拟形象 (2022.3.22f1)",
            Kind = "虚拟形象",
            UnityVersion = "2022.3.22f1",
            PackageRanges = new Dictionary<string, string>
            {
                [Base] = "~3.10.x", [Avatars] = "~3.10.x", [Resolver] = "*"
            }
        },
        new()
        {
            Key = "built-in:worlds-2019",
            Name = "VRChat 世界 (2019.4.31f1)",
            Kind = "世界",
            UnityVersion = "2019.4.31f1",
            PackageRanges = new Dictionary<string, string>
            {
                [Base] = "~3.4.x", [Worlds] = "~3.4.x", [Resolver] = "*"
            }
        },
        new()
        {
            Key = "built-in:avatars-2019",
            Name = "VRChat 虚拟形象 (2019.4.31f1)",
            Kind = "虚拟形象",
            UnityVersion = "2019.4.31f1",
            PackageRanges = new Dictionary<string, string>
            {
                [Avatars] = "~3.4.x", [Resolver] = "*"
            }
        }
    ];

    public static string ManifestJson { get; } = BuildManifest();

    private static string BuildManifest()
    {
        string[] modules =
        [
            "ai", "androidjni", "animation", "assetbundle", "audio", "cloth", "director",
            "imageconversion", "imgui", "jsonserialize", "particlesystem", "physics",
            "physics2d", "screencapture", "terrain", "terrainphysics", "tilemap", "ui",
            "uielements", "umbra", "unityanalytics", "unitywebrequest",
            "unitywebrequestassetbundle", "unitywebrequestaudio", "unitywebrequesttexture",
            "unitywebrequestwww", "vehicles", "video", "vr", "wind", "xr"
        ];

        var deps = modules.Select(m => $"    \"com.unity.modules.{m}\": \"1.0.0\"");

        return "{\n  \"dependencies\": {\n" + string.Join(",\n", deps) + "\n  }\n}\n";
    }

    public static string? ChooseVersion(VpmPackage package, string? range, string? targetUnityVersion)
    {
        if (package.Versions.Count == 0) return package.Version;

        var candidates = package.Versions
            .Select(v => (Parsed: ParseVersion(v.Key), Raw: v.Key, Unity: ParseMinor(v.Value.Unity)))
            .Where(c => !string.IsNullOrEmpty(c.Raw))
            .ToList();

        var wanted = ParseMinor(range);

        if (wanted is not null)
        {
            var exact = candidates
                .Where(c => c.Parsed.Major == wanted.Value.Major && c.Parsed.Minor == wanted.Value.Minor)
                .OrderByDescending(c => c.Parsed)
                .FirstOrDefault();

            if (!string.IsNullOrEmpty(exact.Raw)) return exact.Raw;
        }

        var target = ParseMinor(targetUnityVersion);

        if (target is not null)
        {
            var fitting = candidates
                .Where(c => c.Unity is null || Compare(c.Unity, target) <= 0)
                .OrderByDescending(c => c.Parsed)
                .FirstOrDefault();

            if (!string.IsNullOrEmpty(fitting.Raw)) return fitting.Raw;
        }

        return package.Version;
    }

    public static int CompareUnityVersions(string? left, string? right)
    {
        var a = ParseMinor(left);
        var b = ParseMinor(right);

        if (a is null || b is null) return (a is null, b is null) switch
        {
            (true, true) => string.CompareOrdinal(left, right),
            (true, _) => 1,
            _ => -1
        };

        return Compare(a, b);
    }

    public static bool MatchesUnity(string? templateVersion, string? editorVersion)
    {
        var template = ParseMinor(templateVersion);
        var editor = ParseMinor(editorVersion);

        return template is null || editor is null || Compare(template, editor) == 0;
    }

    private static (int Major, int Minor)? ParseMinor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var trimmed = value.Trim().TrimStart('~', '^', 'v', '=');
        var parts = trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2) return null;
        if (!int.TryParse(Digits(parts[0]), out var major)) return null;
        if (!int.TryParse(Digits(parts[1]), out var minor)) return null;

        return parts[1].Contains('x') || parts[1].Contains('*') ? null : (major, minor);
    }

    private static string Digits(string chunk) => new(chunk.TakeWhile(char.IsDigit).ToArray());

    private static Version ParseVersion(string raw)
    {
        var core = raw.Split('-', '+')[0];
        return Version.TryParse(core, out var parsed) ? parsed : new Version(0, 0);
    }

    private static int Compare((int Major, int Minor)? a, (int Major, int Minor)? b) =>
        a!.Value.Major != b!.Value.Major
            ? a.Value.Major.CompareTo(b.Value.Major)
            : a.Value.Minor.CompareTo(b.Value.Minor);

    [GeneratedRegex(@"m_EditorVersion:\s*(.+)")]
    private static partial Regex ProjectVersion();

    private sealed class TemplatePackageInfo
    {
        public string? DisplayName { get; set; }

        public string? Description { get; set; }
    }
}
