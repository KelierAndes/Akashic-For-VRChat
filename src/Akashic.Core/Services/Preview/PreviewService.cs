using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Akashic.Core.Data;
using Akashic.Core.Models;

namespace Akashic.Core.Services.Preview;

public interface IPreviewService
{
    Task<List<PreviewPrefabInfo>> ListAsync(string resourceKey, string? entry, CancellationToken ct = default);

    Task<PreviewBuildResult> BuildAsync(PreviewRequest request, CancellationToken ct = default);

    Task<PreviewMaterial> MaterialAsync(string resourceKey, string? entry, string path,
        CancellationToken ct = default);

    Task<string> AssetAsync(string resourceKey, string? entry, string path, CancellationToken ct = default);

    PreviewBuildArtifacts? GetArtifacts(string sessionId);

    string BuildsRoot { get; }
}

public sealed partial class PreviewService : IPreviewService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly AkashicDatabase _db;
    private readonly PackageContentCache _content;
    private readonly string _cacheRoot;
    private readonly object _gate = new();
    private readonly Dictionary<string, PrefabParser> _parsers = new(StringComparer.OrdinalIgnoreCase);

    public string BuildsRoot { get; }

    public PreviewService(AkashicDatabase? database = null, PackageContentCache? content = null,
        string? cacheRoot = null)
    {
        _db = database ?? new AkashicDatabase();
        _cacheRoot = cacheRoot ?? PreviewCachePaths.Root;
        _content = content ?? new PackageContentCache(_cacheRoot);
        Directory.CreateDirectory(_cacheRoot);
        BuildsRoot = PreviewCachePaths.VersionedUnder(_cacheRoot, "builds", BuildVersion);
    }

    public async Task<List<PreviewPrefabInfo>> ListAsync(string resourceKey, string? entry,
        CancellationToken ct = default)
    {
        var row = _db.GetResource(resourceKey)
                  ?? throw new InvalidOperationException("资源不在目录里，先刷新资源页。");

        var content = await _content.ResolveAsync(row, entry, ct);

        return content.Assets.Select(asset => new PreviewPrefabInfo
        {
            Path = asset.Path,
            Name = asset.Name,
            Folder = asset.Folder,
            Kind = asset.Kind,
            Size = asset.Size
        }).ToList();
    }

    public async Task<PreviewMaterial> MaterialAsync(string resourceKey, string? entry, string path,
        CancellationToken ct = default)
    {
        var row = _db.GetResource(resourceKey)
                  ?? throw new InvalidOperationException("资源不在目录里，先刷新资源页。");

        var content = await _content.ResolveAsync(row, entry, ct);
        var full = ResolveInside(content.Root, path)
                   ?? throw new InvalidOperationException("材质路径不在包里。");

        if (!File.Exists(full)) throw new FileNotFoundException("材质文件不存在。", full);

        var head = new byte[5];
        int read;
        await using (var probe = File.OpenRead(full))
            read = await probe.ReadAsync(head, ct);

        if (read < head.Length || Encoding.ASCII.GetString(head) != "%YAML")
            throw new InvalidOperationException("这个 .mat 是二进制序列化的，读不出里面的属性。");

        var material = ParserFor(content.Root).ParseMaterial(full)
                       ?? throw new InvalidOperationException("这个 .mat 解析不出材质。");

        return material;
    }

    public async Task<string> AssetAsync(string resourceKey, string? entry, string path,
        CancellationToken ct = default)
    {
        var row = _db.GetResource(resourceKey)
                  ?? throw new InvalidOperationException("资源不在目录里，先刷新资源页。");

        if (path.Contains("..", StringComparison.Ordinal))
            throw new InvalidOperationException("资产路径越界。");

        var content = await _content.ResolveAsync(row, entry, ct);
        var full = ResolveInside(content.Root, path)
                   ?? throw new InvalidOperationException("资产路径不在包里。");

        if (!File.Exists(full)) throw new FileNotFoundException("资产文件不存在。", full);
        return full;
    }

    private PrefabParser ParserFor(string root)
    {
        lock (_gate)
        {
            if (_parsers.TryGetValue(root, out var cached)) return cached;
            var parser = new PrefabParser(root, new UnityMetaIndex(root), _db);
            _parsers[root] = parser;
            return parser;
        }
    }

    private static string? ResolveInside(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    public async Task<PreviewBuildResult> BuildAsync(PreviewRequest request, CancellationToken ct = default)
    {
        var row = _db.GetResource(request.ResourceKey)
                  ?? throw new InvalidOperationException("资源不在目录里，先刷新资源页。");

        var content = await _content.ResolveAsync(row, request.Entry, ct);
        var relative = request.PrefabPath.Replace('\\', '/').TrimStart('/');

        if (!content.Prefabs.Contains(relative, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("这个 prefab 不在包里。");

        var full = Path.GetFullPath(Path.Combine(content.Root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(content.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("prefab 路径越界。");

        var info = new FileInfo(full);
        if (!info.Exists) throw new FileNotFoundException("prefab 文件不存在。", full);

        var key = BuildKey(content.Root, relative, info);
        var directory = Path.Combine(BuildsRoot, key);
        var scenePath = Path.Combine(directory, "scene.json");
        var glbPath = Path.Combine(directory, "model.glb");

        PreviewScene? scene = null;
        if (File.Exists(scenePath) && File.Exists(glbPath))
        {
            try
            {
                scene = JsonSerializer.Deserialize<PreviewScene>(await File.ReadAllTextAsync(scenePath, ct), JsonOptions);
            }
            catch (JsonException)
            {
            }
        }

        if (scene is null)
        {
            lock (_gate)
            {
                if (!File.Exists(scenePath))
                {
                    Directory.CreateDirectory(directory);

                    if (PackageContentCache.IsModelPath(relative))
                    {
                        scene = new PreviewScene
                        {
                            PrefabPath = relative,
                            Name = Path.GetFileNameWithoutExtension(relative),
                            Root = content.Root,
                            NodeCount = 1,
                            MeshCount = 1,
                            Nodes =
                            [
                                new PreviewNode
                                {
                                    Id = "model",
                                    Name = Path.GetFileNameWithoutExtension(relative),
                                    Mesh = new PreviewMesh
                                    {
                                        AssetPath = full,
                                        SubMesh = string.Empty
                                    }
                                }
                            ]
                        };
                    }
                    else
                    {
                        var index = new UnityMetaIndex(content.Root);
                        var parser = new PrefabParser(content.Root, index, _db);
                        scene = parser.Parse(relative);
                        scene.Root = content.Root;
                    }

                    var warnings = new List<string>();
                    var meshes = 0;
                    try
                    {
                        using var builder = new GlbBuilder();
                        meshes = builder.Build(scene, glbPath, warnings);
                    }
                    catch (Exception ex) when (ex is IOException or InvalidOperationException or DllNotFoundException)
                    {
                        warnings.Add("模型转换失败：" + ex.Message);
                    }

                    if (scene.MeshCount > 0 && meshes == 0)
                        warnings.Add("没有成功输出可渲染的网格。");

                    ExportTextures(scene, directory);

                    scene.Warnings.AddRange(warnings);
                    File.WriteAllText(scenePath, JsonSerializer.Serialize(scene, JsonOptions));
                }
                else
                {
                    try
                    {
                        scene = JsonSerializer.Deserialize<PreviewScene>(File.ReadAllText(scenePath), JsonOptions);
                    }
                    catch (JsonException)
                    {
                    }
                }
            }
        }

        scene ??= new PreviewScene { PrefabPath = relative, Name = Path.GetFileNameWithoutExtension(relative) };

        var bytes = File.Exists(glbPath) ? new FileInfo(glbPath).Length : 0;
        return new PreviewBuildResult
        {
            Scene = scene,
            SessionId = key,
            ModelBytes = bytes,
            Warnings = scene.Warnings
        };
    }

    private static void ExportTextures(PreviewScene scene, string directory)
    {
        var target = Path.Combine(directory, "textures");
        var copied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in Flatten(scene.Nodes))
        {
            foreach (var material in node.Materials)
            {
                if (material.MatCapTexture.Length == 0 || !File.Exists(material.MatCapTexture)) continue;

                var source = material.MatCapTexture;
                if (!copied.TryGetValue(source, out var relative))
                {
                    var hash = Convert.ToHexString(
                        System.Security.Cryptography.SHA256.HashData(
                            System.Text.Encoding.UTF8.GetBytes(source)))[..12].ToLowerInvariant();
                    var name = hash + Path.GetExtension(source).ToLowerInvariant();
                    Directory.CreateDirectory(target);
                    File.Copy(source, Path.Combine(target, name), overwrite: true);
                    relative = "textures/" + name;
                    copied[source] = relative;
                }

                material.MatCapTexture = relative;
            }
        }
    }

    private static IEnumerable<PreviewNode> Flatten(IEnumerable<PreviewNode> roots)
    {
        foreach (var node in roots)
        {
            yield return node;
            foreach (var child in Flatten(node.Children)) yield return child;
        }
    }

    private const string BuildVersion = "build-v23";

    public PreviewBuildArtifacts? GetArtifacts(string sessionId)
    {
        if (sessionId.Length != 20 || !sessionId.All(Uri.IsHexDigit)) return null;

        var directory = Path.Combine(BuildsRoot, sessionId);
        var scenePath = Path.Combine(directory, "scene.json");
        var glbPath = Path.Combine(directory, "model.glb");
        if (!File.Exists(scenePath) || !File.Exists(glbPath)) return null;

        PreviewScene? scene;
        try
        {
            scene = JsonSerializer.Deserialize<PreviewScene>(File.ReadAllText(scenePath), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (scene is null) return null;

        return new PreviewBuildArtifacts
        {
            SessionId = sessionId,
            Directory = directory,
            ModelPath = glbPath,
            Scene = scene
        };
    }

    private static string BuildKey(string root, string relative, FileInfo info)
    {
        var raw = $"{BuildVersion}|{root}|{relative}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..20].ToLowerInvariant();
    }
}
