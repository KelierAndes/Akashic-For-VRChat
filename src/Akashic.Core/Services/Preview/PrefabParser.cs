using System.Globalization;
using System.Text.RegularExpressions;
using Akashic.Core.Data;
using Akashic.Core.Models;
using Akashic.Core.Services.Preview.Shader;
using YamlDotNet.RepresentationModel;

namespace Akashic.Core.Services.Preview;

public sealed partial class PrefabParser
{
    private static readonly HashSet<string> InternalScriptFields = new(StringComparer.Ordinal)
    {
        "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset",
        "m_GameObject", "m_Enabled", "m_EditorHideFlags", "m_Script", "m_Name", "m_EditorClassIdentifier"
    };

    private const string MaterialCacheVersion = "material-v7";

    private readonly UnityMetaIndex _index;
    private readonly string _root;
    private readonly AkashicDatabase? _db;
    private readonly Dictionary<string, PreviewMaterial?> _materials = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<PreviewNode>> _prefabCache = new(StringComparer.OrdinalIgnoreCase);

    public PrefabParser(string root, UnityMetaIndex index, AkashicDatabase? database = null)
    {
        _root = root;
        _index = index;
        _db = database;
    }

    public PreviewScene Parse(string prefabRelativePath)
    {
        var scene = new PreviewScene
        {
            PrefabPath = prefabRelativePath.Replace('\\', '/'),
            Name = Path.GetFileNameWithoutExtension(prefabRelativePath)
        };

        var full = Path.Combine(_root, prefabRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full))
        {
            scene.Warnings.Add("找不到 prefab 文件。");
            return scene;
        }

        var parsed = ParseFile(full, depth: 0, scene.Warnings);
        scene.Nodes = parsed;

        var flat = Flatten(scene.Nodes).ToList();
        scene.NodeCount = flat.Count;
        scene.MeshCount = flat.Count(node => node.Mesh is not null);
        scene.MaterialCount = flat.Sum(node => node.Materials.Count);
        return scene;
    }

    private List<PreviewNode> ParseFile(string fullPath, int depth, List<string> warnings)
    {
        if (depth > 3)
        {
            warnings.Add("嵌套 prefab 超过 3 层，已截断。");
            return [];
        }

        if (_prefabCache.TryGetValue(fullPath, out var cached)) return cached;

        var blocks = ReadBlocks(fullPath);
        if (blocks.Count == 0)
        {
            warnings.Add($"{Path.GetFileName(fullPath)} 里没有可解析的 YAML 对象。");
            return [];
        }

        var byId = new Dictionary<long, RawBlock>();
        foreach (var block in blocks) byId[block.FileId] = block;
        var gameObjects = blocks.Where(block => block.ClassId == 1 && !block.Stripped).ToList();
        var transforms = blocks.Where(block => block.ClassId is 4 or 224 && !block.Stripped).ToList();

        var transformByGameObject = new Dictionary<long, RawBlock>();
        foreach (var transform in transforms)
        {
            var go = GetRef(transform.Content, "m_GameObject");
            if (go.FileId != 0) transformByGameObject[go.FileId] = transform;
        }

        var nodes = new Dictionary<long, PreviewNode>();
        var nodeByTransform = new Dictionary<long, PreviewNode>();

        foreach (var go in gameObjects)
        {
            var node = new PreviewNode
            {
                Id = go.FileId.ToString(CultureInfo.InvariantCulture),
                Name = GetScalar(go.Content, "m_Name") ?? "GameObject",
                Active = GetScalar(go.Content, "m_IsActive") != "0",
                FileIds = [go.FileId.ToString(CultureInfo.InvariantCulture)]
            };

            if (transformByGameObject.TryGetValue(go.FileId, out var transform))
            {
                ApplyTransform(node, transform);
                transformByGameObject[go.FileId] = transform;
                nodeByTransform[transform.FileId] = node;
                node.FileIds.Add(transform.FileId.ToString(CultureInfo.InvariantCulture));
            }

            foreach (var componentRef in ReadComponentRefs(go.Content))
            {
                if (!byId.TryGetValue(componentRef, out var block) || block.Stripped) continue;
                AttachComponent(node, block, warnings);
            }

            nodes[go.FileId] = node;
        }

        foreach (var transform in transforms)
        {
            var go = GetRef(transform.Content, "m_GameObject");
            if (!nodes.TryGetValue(go.FileId, out var node)) continue;
            ApplyTransform(node, transform);

            var father = GetRef(transform.Content, "m_Father");
            if (father.FileId != 0 && nodeByTransform.TryGetValue(father.FileId, out var parent))
                parent.Children.Add(node);
        }

        var roots = nodes.Values
            .Where(node => !IsChildOfAny(node, nodes.Values))
            .ToList();

        var instances = blocks.Where(block => block.ClassId == 1001 && !block.Stripped).ToList();
        foreach (var instance in instances)
        {
            var node = BuildPrefabInstance(instance, depth, warnings);
            if (node is null) continue;

            var parentRef = GetRef(instance.Content, "m_TransformParent");
            PreviewNode? parent = null;
            if (parentRef.FileId != 0)
            {
                if (nodeByTransform.TryGetValue(parentRef.FileId, out var byTransform)) parent = byTransform;
                else if (nodes.TryGetValue(parentRef.FileId, out var byGo)) parent = byGo;
            }

            if (parent is not null) parent.Children.Add(node);
            else roots.Add(node);
        }

        foreach (var transform in transforms)
        {
            var go = GetRef(transform.Content, "m_GameObject");
            if (!nodes.TryGetValue(go.FileId, out var node) || node.Children.Count == 0) continue;

            var order = ReadChildrenRefs(transform.Content);
            if (order.Count == 0) continue;

            var lookup = node.Children.ToDictionary(child => child.Id, StringComparer.Ordinal);
            var sorted = new List<PreviewNode>();
            foreach (var childId in order)
            {
                if (lookup.Remove(childId.ToString(CultureInfo.InvariantCulture), out var child))
                    sorted.Add(child);
            }
            sorted.AddRange(lookup.Values);
            node.Children = sorted;
        }

        _prefabCache[fullPath] = roots;
        return roots;
    }

    private static bool IsChildOfAny(PreviewNode node, IEnumerable<PreviewNode> all) =>
        all.Any(other => other != node && other.Children.Contains(node));

    private PreviewNode? BuildPrefabInstance(RawBlock instance, int depth, List<string> warnings)
    {
        var source = GetRef(instance.Content, "m_SourcePrefab");
        var sourcePath = _index.Resolve(source.Guid);
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            warnings.Add("嵌套 prefab 的源文件不在这个包里，已跳过。");
            return null;
        }

        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension is ".fbx" or ".obj" or ".gltf" or ".glb")
            return BuildModelInstance(instance, sourcePath);

        var baseRoots = ParseFile(sourcePath, depth + 1, warnings);
        if (baseRoots.Count == 0) return null;

        foreach (var root in baseRoots) PrefixIds(root, "pi:" + instance.FileId + "/");

        var map = new Dictionary<string, PreviewNode>(StringComparer.Ordinal);
        foreach (var node in baseRoots.SelectMany(root => Flatten([root])))
            foreach (var id in node.FileIds)
                map.TryAdd(id, node);

        foreach (var modification in ReadModifications(instance.Content))
        {
            if (!map.TryGetValue(modification.TargetFileId.ToString(CultureInfo.InvariantCulture), out var node))
                continue;

            ApplyModification(node, modification);
        }

        if (baseRoots.Count == 1) return baseRoots[0];

        var container = new PreviewNode
        {
            Id = "pi:" + instance.FileId,
            Name = Path.GetFileNameWithoutExtension(sourcePath)
        };
        container.Children.AddRange(baseRoots);
        return container;
    }

    private static PreviewNode BuildModelInstance(RawBlock instance, string sourcePath)
    {
        var node = new PreviewNode
        {
            Id = "pi:" + instance.FileId,
            Name = Path.GetFileNameWithoutExtension(sourcePath),
            Mesh = new PreviewMesh
            {
                AssetPath = sourcePath,
                FileId = 100100000,
                SubMesh = string.Empty
            }
        };

        foreach (var modification in ReadModifications(instance.Content))
            ApplyModification(node, modification);

        return node;
    }

    private static void PrefixIds(PreviewNode node, string prefix)
    {
        node.Id = prefix + node.Id;
        foreach (var child in node.Children) PrefixIds(child, prefix);
    }

    private static void ApplyModification(PreviewNode node, Modification modification)
    {
        var value = modification.Value;
        switch (modification.PropertyPath)
        {
            case "m_Name":
                if (!string.IsNullOrWhiteSpace(value)) node.Name = value;
                break;
            case "m_IsActive":
                node.Active = value != "0";
                break;
            case "m_LocalPosition.x":
                node.Position[0] = ParseFloat(value, node.Position[0]);
                break;
            case "m_LocalPosition.y":
                node.Position[1] = ParseFloat(value, node.Position[1]);
                break;
            case "m_LocalPosition.z":
                node.Position[2] = ParseFloat(value, node.Position[2]);
                break;
            case "m_LocalRotation.x":
                node.Rotation[0] = ParseFloat(value, node.Rotation[0]);
                break;
            case "m_LocalRotation.y":
                node.Rotation[1] = ParseFloat(value, node.Rotation[1]);
                break;
            case "m_LocalRotation.z":
                node.Rotation[2] = ParseFloat(value, node.Rotation[2]);
                break;
            case "m_LocalRotation.w":
                node.Rotation[3] = ParseFloat(value, node.Rotation[3]);
                break;
            case "m_LocalScale.x":
                node.Scale[0] = ParseFloat(value, node.Scale[0]);
                break;
            case "m_LocalScale.y":
                node.Scale[1] = ParseFloat(value, node.Scale[1]);
                break;
            case "m_LocalScale.z":
                node.Scale[2] = ParseFloat(value, node.Scale[2]);
                break;
            case "m_Enabled":
                foreach (var component in node.Components)
                    if (component.FileId.ToString(CultureInfo.InvariantCulture) == modification.TargetFileId.ToString(CultureInfo.InvariantCulture))
                        component.Enabled = value != "0";
                break;
        }
    }

    private static float ParseFloat(string value, float fallback) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && float.IsFinite(parsed)
            ? parsed
            : fallback;

    private void AttachComponent(PreviewNode node, RawBlock block, List<string> warnings)
    {
        node.FileIds.Add(block.FileId.ToString(CultureInfo.InvariantCulture));

        switch (block.ClassId)
        {
            case 33:
            case 137:
            {
                var mesh = GetRef(block.Content, "m_Mesh");
                if (mesh.IsExternal)
                {
                    var asset = _index.Resolve(mesh.Guid);
                    if (asset is not null)
                    {
                        node.Mesh = new PreviewMesh
                        {
                            AssetPath = asset,
                            FileId = mesh.FileId,
                            SubMesh = _index.ResolveSubAsset(mesh.Guid, mesh.FileId) ?? string.Empty,
                            Skinned = block.ClassId == 137
                        };
                    }
                    else if (string.Equals(mesh.Guid, BuiltInExtraGuid, StringComparison.OrdinalIgnoreCase))
                    {
                        node.Mesh = new PreviewMesh
                        {
                            AssetPath = "unity-builtin:" + mesh.FileId,
                            FileId = mesh.FileId,
                            SubMesh = string.Empty,
                            Skinned = false
                        };
                    }
                }

                if (block.ClassId == 33) AddComponent(node, block, "MeshFilter");
                else AddRenderer(node, block, warnings);
                break;
            }
            case 23:
                AddRenderer(node, block, warnings);
                break;
            case 4:
            case 224:
                AddComponent(node, block, block.ClassId == 4 ? "Transform" : "RectTransform");
                break;
            default:
                AddComponent(node, block, block.RootTypeName);
                break;
        }
    }

    private void AddRenderer(PreviewNode node, RawBlock block, List<string> warnings)
    {
        AddComponent(node, block, block.ClassId == 137 ? "SkinnedMeshRenderer" : "MeshRenderer");

        foreach (var item in ReadSequence(block.Content, "m_Materials"))
        {
            var reference = ParseRef(item);
            if (!reference.IsExternal) continue;

            var path = _index.Resolve(reference.Guid);
            if (path is null)
            {
                warnings.Add("有一个材质引用不在这个包里。");
                continue;
            }

            var material = LoadMaterial(path);
            if (material is not null) node.Materials.Add(material);
        }
    }

    public PreviewMaterial? ParseMaterial(string materialPath) => LoadMaterial(materialPath);

    private PreviewMaterial? LoadMaterial(string path)
    {
        if (_materials.TryGetValue(path, out var cached)) return cached;

        var file = new FileInfo(path);
        var cacheKey = MaterialCacheVersion + ":" + path;
        if (_db is not null && file.Exists
            && _db.TryGetPreviewMaterial(cacheKey, file.Length, file.LastWriteTime, out var stored))
        {
            _materials[path] = stored;
            return stored;
        }

        PreviewMaterial? material = null;
        try
        {
            var blocks = ReadBlocks(path);
            var block = blocks.FirstOrDefault(item => item.ClassId == 21 || item.RootTypeName == "Material");
            if (block is not null)
            {
                material = new PreviewMaterial
                {
                    Path = path,
                    Name = GetScalar(block.Content, "m_Name") ?? Path.GetFileNameWithoutExtension(path)
                };

                var shaderRef = GetRef(block.Content, "m_Shader");
                if (shaderRef.IsExternal)
                {
                    material.Shader = ResolveShader(shaderRef.Guid);
                    ApplyShader(material);
                }

                var keywords = ReadKeywords(block.Content);
                material.Keywords = keywords;
                ReadMaterialProperties(block.Content, material, shaderRef.Guid, keywords);
                ApplyShaderProperties(material);
                FinishMaterial(material);
            }
        }
        catch (Exception ex) when (ex is IOException or YamlDotNet.Core.YamlException)
        {
        }

        _materials[path] = material;
        if (material is not null && file.Exists)
            _db?.PutPreviewMaterial(cacheKey, file.Length, file.LastWriteTime, material);

        return material;
    }

    private void ReadMaterialProperties(YamlMappingNode content, PreviewMaterial material, string? shaderGuid,
        List<string> keywords)
    {
        if (!TryGet(content, "m_SavedProperties", out var saved) || saved is not YamlMappingNode properties)
            return;

        foreach (var entry in ReadSequence(properties, "m_TexEnvs"))
        {
            var (slot, value) = ReadKeyValue(entry);
            if (slot is null || value is not YamlMappingNode map) continue;

            var texture = GetRef(map, "m_Texture");
            if (!texture.IsExternal) continue;

            material.Slots.Add(slot);
            material.Textures[slot] = _index.Resolve(texture.Guid) ?? texture.Guid ?? string.Empty;

            var scaleX = 1f;
            var scaleY = 1f;
            var offsetX = 0f;
            var offsetY = 0f;
            if (TryGet(map, "m_Scale", out var scaleNode) && scaleNode is YamlMappingNode scale)
            {
                scaleX = ParseFloat(GetScalar(scale, "x") ?? "1", 1);
                scaleY = ParseFloat(GetScalar(scale, "y") ?? "1", 1);
            }

            if (TryGet(map, "m_Offset", out var offsetNode) && offsetNode is YamlMappingNode offset)
            {
                offsetX = ParseFloat(GetScalar(offset, "x") ?? "0", 0);
                offsetY = ParseFloat(GetScalar(offset, "y") ?? "0", 0);
            }

            material.TextureST[slot] = [scaleX, scaleY, offsetX, offsetY];
        }

        foreach (var entry in ReadSequence(properties, "m_Colors"))
        {
            var (slot, value) = ReadKeyValue(entry);
            if (slot is null || value is not YamlMappingNode map) continue;

            var color = new[]
            {
                ParseFloat(GetScalar(map, "r") ?? "0", 0),
                ParseFloat(GetScalar(map, "g") ?? "0", 0),
                ParseFloat(GetScalar(map, "b") ?? "0", 0),
                ParseFloat(GetScalar(map, "a") ?? "1", 1)
            };

            material.Colors[slot] = color;
            if (slot is "_BaseColor" or "_Color" or "_MainColor" or "_Tint")
                material.BaseColor = color;
        }

        foreach (var entry in ReadSequence(properties, "m_Floats"))
        {
            var (slot, value) = ReadKeyValue(entry);
            if (slot is null || value is not YamlScalarNode scalar) continue;

            var number = ParseFloat(scalar.Value ?? "0", 0);
            material.Floats[slot] = number;
            if (slot == "_Cutoff") material.Cutoff = number;
        }

        ApplyLilToon(material, shaderGuid);
        ApplyRenderState(material, shaderGuid, keywords);
    }

    private static List<string> ReadKeywords(YamlMappingNode content)
    {
        var keywords = new List<string>();
        foreach (var item in ReadSequence(content, "m_ValidKeywords"))
            if (item is YamlScalarNode scalar && !string.IsNullOrWhiteSpace(scalar.Value))
                keywords.Add(scalar.Value);

        if (keywords.Count == 0 && TryGet(content, "m_ShaderKeywords", out var node) &&
            node is YamlScalarNode text && !string.IsNullOrWhiteSpace(text.Value))
        {
            keywords.AddRange(text.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        return keywords.Distinct(StringComparer.Ordinal).ToList();
    }

    private const string BuiltInStandardGuid = "0000000000000000f000000000000000";
    private const string BuiltInExtraGuid = "0000000000000000e000000000000000";

    private void ApplyRenderState(PreviewMaterial material, string? shaderGuid, List<string> keywords)
    {
        var builtInStandard = string.Equals(shaderGuid, BuiltInStandardGuid, StringComparison.OrdinalIgnoreCase)
                              || material.ShaderName.EndsWith("Standard", StringComparison.OrdinalIgnoreCase);
        if (builtInStandard)
        {
            material.ShaderFamily = "Standard";
            material.Kind = "lit";
        }

        var mode = material.Floats.TryGetValue("_Mode", out var modeValue) ? (int)Math.Round(modeValue) : -1;
        var dstBlend = material.Floats.TryGetValue("_DstBlend", out var dstValue) ? (int)Math.Round(dstValue) : 0;
        var zWrite = material.Floats.TryGetValue("_ZWrite", out var zwValue) ? (int)Math.Round(zwValue) : 1;
        var alphaClip = keywords.Any(keyword =>
            keyword.Contains("ALPHATEST_ON", StringComparison.OrdinalIgnoreCase) ||
            keyword.Contains("AlphaClip", StringComparison.OrdinalIgnoreCase));

        var alphaBlend = mode is 2 or 3
                         || (dstBlend != 0 && dstBlend != 1)
                         || zWrite == 0
                         || keywords.Any(keyword =>
                             keyword.Contains("ALPHABLEND_ON", StringComparison.OrdinalIgnoreCase) ||
                             keyword.Contains("ALPHAPREMULTIPLY_ON", StringComparison.OrdinalIgnoreCase) ||
                             keyword.Contains("_SURFACE_TYPE_TRANSPARENT", StringComparison.OrdinalIgnoreCase));

        var customShader = !builtInStandard && material.ShaderFamily != "lilToon";
        var cutout = mode == 1 || alphaClip || material.UseCutout
                     || (customShader && material.AlphaMode == "mask");
        var shaderBlend = customShader && material.AlphaMode == "blend";

        if (cutout)
        {
            material.AlphaMode = "mask";
            material.UseCutout = true;
        }
        else if (alphaBlend || shaderBlend || material.TransparentMode >= 2)
        {
            material.AlphaMode = "blend";
        }
        else if (customShader && material.BaseColor[3] < 0.999f)
        {
            material.AlphaMode = "blend";
        }

        material.PremultipliedAlpha = keywords.Any(keyword =>
            keyword.Contains("ALPHAPREMULTIPLY_ON", StringComparison.OrdinalIgnoreCase));

        if (builtInStandard && material.Floats.TryGetValue("_Cull", out var cull))
            material.DoubleSided = (int)Math.Round(cull) == 0;
    }

    private void ApplyLilToon(PreviewMaterial material, string? shaderGuid)
    {
        if (string.Equals(shaderGuid, BuiltInStandardGuid, StringComparison.OrdinalIgnoreCase)) return;

        var probe = $"{material.ShaderName} {Path.GetFileNameWithoutExtension(material.Shader)}";
        var isLil = material.Floats.ContainsKey("_TransparentMode")
                    || material.Colors.ContainsKey("_1st_ShadeColor")
                    || material.Floats.ContainsKey("_LightMinLimit")
                    || probe.Contains("liltoon", StringComparison.OrdinalIgnoreCase)
                    || probe.Contains("lil/lil", StringComparison.OrdinalIgnoreCase);

        if (!isLil) return;

        material.ShaderFamily = "lilToon";
        material.Kind = "liltoon";

        if (material.Colors.TryGetValue("_Color", out var color)) material.BaseColor = color;
        material.ShadeColor1 = FirstColor(["_1st_ShadeColor", "_ShadowColor"], [0.85f, 0.85f, 0.85f, 1]);
        material.ShadeColor2 = FirstColor(
            ["_2nd_ShadeColor", "_Shadow2ndColor", "_3rd_ShadeColor", "_Shadow3rdColor"],
            [0.65f, 0.65f, 0.65f, 1]);

        if (material.ShadeColor1[3] <= 0.001f) material.ShadeColor1 = [0.85f, 0.85f, 0.85f, 1];
        if (material.ShadeColor2[3] <= 0.001f) material.ShadeColor2 = material.ShadeColor1;

        material.ShadowStrength = Number("_ShadowStrength", 1);
        material.LightMinLimit = Number("_LightMinLimit", 0.05f);
        material.LightMaxLimit = Number("_LightMaxLimit", 1);
        material.AsUnlit = Number("_AsUnlit", 0) > 0.001f;
        material.MonochromeLighting = Number("_MonochromeLighting", 0) > 0.001f;

        material.TransparentMode = (int)Math.Round(Number("_TransparentMode", 0));
        material.UseCutout = material.TransparentMode == 1
                             || Number("_UseCutout", 0) > 0.001f
                             || Number("_AlphaCutoutEnable", 0) > 0.001f;
        material.AlphaMode = material.UseCutout
            ? "mask"
            : material.TransparentMode >= 2
                ? "blend"
                : "opaque";
        material.Cutoff = Number("_Cutoff", 0.5f);

        material.DoubleSided = (int)Math.Round(Number("_Cull", 2)) == 0;

        material.UseEmission = Number("_UseEmission", 0) > 0.001f;
        material.EmissionColor = Color("_EmissionColor", [0, 0, 0, 1]);
        material.EmissionTexture = Texture("_EmissionMap");

        material.UseMatCap = Number("_UseMatCap", 0) > 0.001f;
        material.MatCapColor = Color("_MatCapColor", [1, 1, 1, 1]);
        material.MatCapTexture = Texture("_MatCapTex");

        material.UseOutline = Number("_UseOutline", 0) > 0.001f;
        material.OutlineColor = Color("_OutlineColor", [0, 0, 0, 1]);
        material.OutlineWidth = Number("_OutlineWidth", 0);

        material.NormalTexture = Texture("_BumpMap");
        material.NormalScale = Number("_BumpScale", 1);

        material.BaseTexture = Texture("_BaseMap") is { Length: > 0 } basemap
            ? basemap
            : Texture("_MainTex") is { Length: > 0 } main
                ? main
                : Texture("_BaseColorMap");

        float[] Color(string key, float[] fallback) =>
            material.Colors.TryGetValue(key, out var value) ? value : fallback;

        float[] FirstColor(string[] keys, float[] fallback)
        {
            foreach (var key in keys)
                if (material.Colors.TryGetValue(key, out var value)) return value;
            return fallback;
        }

        float Number(string key, float fallback) =>
            material.Floats.TryGetValue(key, out var value) ? value : fallback;

        string Texture(string key) =>
            material.Textures.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private string ResolveShader(string? guid)
    {
        if (string.IsNullOrWhiteSpace(guid)) return string.Empty;
        if (string.Equals(guid, BuiltInStandardGuid, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(guid, BuiltInExtraGuid, StringComparison.OrdinalIgnoreCase))
            return guid;

        return _index.Resolve(guid) ?? ExternalShaderIndex.Shared.Resolve(guid) ?? guid;
    }

    private void ApplyShader(PreviewMaterial material)
    {
        if (string.IsNullOrWhiteSpace(material.Shader) || !File.Exists(material.Shader)) return;

        string text;
        try
        {
            text = File.ReadAllText(material.Shader);
        }
        catch (IOException)
        {
            return;
        }

        var nameMatch = ShaderNameRegex().Match(text);
        if (nameMatch.Success && !string.IsNullOrWhiteSpace(nameMatch.Groups[1].Value))
            material.ShaderName = nameMatch.Groups[1].Value.Trim();

        if (CullOffRegex().IsMatch(text)) material.DoubleSided = true;

        if (AlphaClipRegex().IsMatch(text))
        {
            material.AlphaMode = "mask";
            material.UseCutout = true;
        }
        else if (BlendAlphaRegex().IsMatch(text))
        {
            material.AlphaMode = "blend";
        }

        var probe = $"{material.ShaderName} {Path.GetFileNameWithoutExtension(material.Shader)}";
        if (ToonRegex().IsMatch(probe)) material.Kind = "toon";
        else if (UnlitRegex().IsMatch(probe)) material.Kind = "unlit";

        material.Pipeline = text.Contains("com.unity.render-pipelines", StringComparison.OrdinalIgnoreCase) ||
                            text.Contains("Universal", StringComparison.Ordinal)
            ? "urp"
            : "builtin";
    }

    private void ApplyShaderProperties(PreviewMaterial material)
    {
        string? text = null;
        if (!string.IsNullOrWhiteSpace(material.Shader) && File.Exists(material.Shader))
        {
            try
            {
                text = File.ReadAllText(material.Shader);
            }
            catch (IOException)
            {
            }
        }

        var mainTexture = string.Empty;
        var mainColor = string.Empty;
        var shaderName = material.ShaderName;

        if (text is not null)
        {
            var block = ExtractBlock(text, "Properties");
            if (block.Length > 0)
            {
                foreach (Match match in ShaderPropertyRegex().Matches(block))
                {
                    var name = match.Groups["name"].Value;
                    var type = match.Groups["type"].Value;
                    var flags = match.Groups["flags"].Value;

                    if (type.Equals("2D", StringComparison.OrdinalIgnoreCase))
                    {
                        if (mainTexture.Length == 0 &&
                            (flags.Contains("MainTexture", StringComparison.OrdinalIgnoreCase) ||
                             name is "_BaseMap" or "_MainTex" or "_BaseColorMap" or "_Albedo" or "_Diffuse"))
                            mainTexture = name;

                        if (material.NormalTexture.Length == 0 &&
                            (flags.Contains("Normal", StringComparison.OrdinalIgnoreCase) ||
                             name.Contains("Bump", StringComparison.OrdinalIgnoreCase) ||
                             name.Contains("Normal", StringComparison.OrdinalIgnoreCase)))
                            material.NormalTexture = Texture(name);
                    }
                    else if (type.Equals("Color", StringComparison.OrdinalIgnoreCase) && mainColor.Length == 0 &&
                             (flags.Contains("MainColor", StringComparison.OrdinalIgnoreCase) ||
                              name is "_Color" or "_BaseColor" or "_MainColor"))
                    {
                        mainColor = name;
                    }
                }
            }

            if (material.UseCutout || (AlphaClipRegex().IsMatch(text) && material.Floats.ContainsKey("_Cutoff")))
            {
                material.UseCutout = true;
                material.AlphaMode = "mask";
            }

            if (material.AlphaMode == "opaque" &&
                material.Floats.TryGetValue("_DstBlend", out var dst) && dst != 0)
                material.AlphaMode = "blend";
        }

        if (mainTexture.Length > 0 && Texture(mainTexture).Length > 0)
            material.BaseTexture = Texture(mainTexture);
        if (material.BaseTexture.Length == 0 && mainTexture.Length > 0)
            material.BaseTexture = Texture(mainTexture);
        if (material.BaseTexture.Length == 0)
            material.BaseTexture = PickBaseTexture(material);

        if (mainColor.Length > 0 && material.Colors.TryGetValue(mainColor, out var color))
            material.BaseColor = color;

        if (text is not null && mainColor.Length == 0 && material.BaseTexture.Length == 0)
            material.Procedural = true;

        if (material.NormalTexture.Length == 0)
            material.NormalTexture = Texture("_BumpMap");

        var shaderProbe = $"{shaderName} {material.ShaderName}";
        if (UnlitRegex().IsMatch(shaderProbe))
        {
            material.Kind = "unlit";
        }
        else if (material.ShaderFamily != "lilToon" && material.ShaderFamily != "Standard")
        {
            var toonHint = material.Textures.ContainsKey("_Ramp")
                           || material.Colors.ContainsKey("_1st_ShadeColor")
                           || material.Colors.ContainsKey("_ShadowColor")
                           || material.Floats.ContainsKey("_RampOffset")
                           || (shaderName.Length > 0 && ToonRegex().IsMatch(shaderName));

            if (toonHint)
            {
                material.Kind = "toon";
                material.ShaderFamily = "toon";
                ApplyToonFields(material);
            }
        }

        string Texture(string name) =>
            material.Textures.TryGetValue(name, out var value) ? value : string.Empty;
    }

    private void ApplyToonFields(PreviewMaterial material)
    {
        material.ShadeColor1 = FirstColor(["_1st_ShadeColor", "_ShadowColor"], [0.85f, 0.85f, 0.85f, 1]);
        material.ShadeColor2 = FirstColor(
            ["_2nd_ShadeColor", "_Shadow2ndColor", "_3rd_ShadeColor", "_Shadow3rdColor"],
            [0.65f, 0.65f, 0.65f, 1]);

        if (material.ShadeColor1[3] <= 0.001f) material.ShadeColor1 = [0.85f, 0.85f, 0.85f, 1];
        if (material.ShadeColor2[3] <= 0.001f) material.ShadeColor2 = material.ShadeColor1;

        material.ShadowStrength = Number("_ShadowStrength", 1);
        material.LightMinLimit = Number("_LightMinLimit", 0.05f);
        material.LightMaxLimit = Number("_LightMaxLimit", 1);
        material.AsUnlit = Number("_AsUnlit", 0) > 0.001f;
        material.DoubleSided = material.DoubleSided
                               || (int)Math.Round(Number("_Cull", 2)) == 0;

        material.UseMatCap = Number("_UseMatCap", 0) > 0.001f
                             || material.Textures.ContainsKey("_MatCapTex");
        material.MatCapColor = FirstColor(["_MatCapColor"], [1, 1, 1, 1]);
        material.MatCapTexture = Texture("_MatCapTex");

        material.UseEmission = Number("_UseEmission", 0) > 0.001f;
        material.EmissionColor = FirstColor(["_EmissionColor"], [0, 0, 0, 1]);
        material.EmissionTexture = Texture("_EmissionMap");

        material.UseOutline = Number("_UseOutline", 0) > 0.001f;
        material.OutlineColor = FirstColor(["_OutlineColor"], [0, 0, 0, 1]);
        material.OutlineWidth = Number("_OutlineWidth", 0);

        material.NormalTexture = material.NormalTexture.Length > 0
            ? material.NormalTexture
            : Texture("_BumpMap");
        material.NormalScale = Number("_BumpScale", 1);

        float[] FirstColor(string[] keys, float[] fallback)
        {
            foreach (var key in keys)
                if (material.Colors.TryGetValue(key, out var value)) return value;
            return fallback;
        }

        float Number(string key, float fallback) =>
            material.Floats.TryGetValue(key, out var value) ? value : fallback;

        string Texture(string key) =>
            material.Textures.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private static string ExtractBlock(string text, string keyword)
    {
        var index = text.IndexOf(keyword, StringComparison.Ordinal);
        if (index < 0) return string.Empty;

        var open = text.IndexOf('{', index);
        if (open < 0) return string.Empty;

        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0) return text[(open + 1)..i];
            }
        }

        return string.Empty;
    }

    private void FinishMaterial(PreviewMaterial material)
    {
        if (material.BaseTexture.Length == 0) material.BaseTexture = PickBaseTexture(material);

        if (material.Kind == "lit" &&
            (material.Slots.Any(slot => slot.Contains("ramp", StringComparison.OrdinalIgnoreCase)) ||
             material.Textures.ContainsKey("_Ramp")))
            material.Kind = "toon";
    }

    private static string PickBaseTexture(PreviewMaterial material)
    {
        string[] priority =
        [
            "_BaseMap", "_MainTex", "_BaseColorMap", "_Albedo", "_Diffuse", "_ColorMap", "_BaseColorTex",
            "_Tex", "_Texture", "_MainTexture"
        ];

        foreach (var slot in priority)
        {
            if (material.Textures.TryGetValue(slot, out var guid) && guid.Length > 0) return guid;
        }

        foreach (var (slot, guid) in material.Textures)
        {
            if (slot.Contains("normal", StringComparison.OrdinalIgnoreCase) ||
                slot.Contains("mask", StringComparison.OrdinalIgnoreCase) ||
                slot.Contains("bump", StringComparison.OrdinalIgnoreCase) ||
                slot.Contains("noise", StringComparison.OrdinalIgnoreCase) ||
                slot.Contains("ramp", StringComparison.OrdinalIgnoreCase))
                continue;

            if (guid.Length > 0) return guid;
        }

        return string.Empty;
    }

    private void AddComponent(PreviewNode node, RawBlock block, string type)
    {
        var component = new PreviewComponent
        {
            ClassId = block.ClassId,
            Type = type,
            FileId = block.FileId,
            Enabled = GetScalar(block.Content, "m_Enabled") != "0"
        };

        if (block.ClassId == 114)
        {
            var script = GetRef(block.Content, "m_Script");
            var path = _index.Resolve(script.Guid);
            component.Script = path ?? script.Guid ?? string.Empty;
            if (path is not null) component.Type = Path.GetFileNameWithoutExtension(path);
        }

        if (block.ClassId is 114 or 1001)
        {
            foreach (var (key, value) in block.Content.Children)
            {
                var name = (key as YamlScalarNode)?.Value ?? string.Empty;
                if (name.Length == 0 || InternalScriptFields.Contains(name)) continue;
                component.Properties[name] = Simplify(value, depth: 0);
            }
        }

        node.Components.Add(component);
    }

    private static object? Simplify(YamlNode node, int depth)
    {
        if (depth > 3) return "…";

        switch (node)
        {
            case YamlScalarNode scalar:
                var value = scalar.Value ?? string.Empty;
                return value.Length > 500 ? value[..500] + "…" : value;
            case YamlSequenceNode sequence:
                return sequence.Children.Take(50).Select(child => Simplify(child, depth + 1)).ToList();
            case YamlMappingNode mapping:
                if (mapping.Children.Count > 40) return $"{{{mapping.Children.Count} 项}}";
                var result = new Dictionary<string, object?>();
                foreach (var (key, childValue) in mapping.Children)
                {
                    var name = (key as YamlScalarNode)?.Value;
                    if (name is null) continue;
                    result[name] = Simplify(childValue, depth + 1);
                }
                return result;
            default:
                return null;
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

    private static void ApplyTransform(PreviewNode node, RawBlock transform)
    {
        node.Position = ReadVector(transform.Content, "m_LocalPosition", [0, 0, 0]);
        node.Rotation = ReadVector(transform.Content, "m_LocalRotation", [0, 0, 0, 1]);
        node.Scale = ReadVector(transform.Content, "m_LocalScale", [1, 1, 1]);
    }

    private static float[] ReadVector(YamlMappingNode content, string key, float[] fallback)
    {
        if (!TryGet(content, key, out var node) || node is not YamlMappingNode map)
            return [.. fallback];

        var names = fallback.Length == 4 ? new[] { "x", "y", "z", "w" } : new[] { "x", "y", "z" };
        var result = new float[names.Length];
        for (var i = 0; i < names.Length; i++)
            result[i] = ParseFloat(GetScalar(map, names[i]) ?? fallback[i].ToString("R", CultureInfo.InvariantCulture), fallback[i]);

        return result;
    }

    private static List<long> ReadComponentRefs(YamlMappingNode content)
    {
        var refs = new List<long>();
        foreach (var item in ReadSequence(content, "m_Component"))
        {
            if (item is not YamlMappingNode map || !TryGet(map, "component", out var component)) continue;
            refs.Add(ParseRef(component).FileId);
        }
        return refs;
    }

    private static List<long> ReadChildrenRefs(YamlMappingNode content)
    {
        var refs = new List<long>();
        foreach (var item in ReadSequence(content, "m_Children"))
            refs.Add(ParseRef(item).FileId);
        return refs;
    }

    private static IEnumerable<Modification> ReadModifications(YamlMappingNode content)
    {
        foreach (var item in ReadSequence(content, "m_Modifications"))
        {
            if (item is not YamlMappingNode map) continue;

            var target = GetRef(map, "target");
            var property = GetScalar(map, "propertyPath");
            if (property is null) continue;

            yield return new Modification(target.FileId, property, GetScalar(map, "value") ?? string.Empty);
        }
    }

    private static IEnumerable<YamlNode> ReadSequence(YamlMappingNode content, string key)
    {
        if (!TryGet(content, key, out var node) || node is not YamlSequenceNode sequence)
            yield break;

        foreach (var child in sequence.Children) yield return child;
    }

    private static (string? Key, YamlNode? Value) ReadKeyValue(YamlNode node)
    {
        if (node is not YamlMappingNode map) return (null, null);

        if (TryGet(map, "first", out var first))
        {
            var key = (first as YamlScalarNode)?.Value;
            TryGet(map, "second", out var second);
            return (key, second);
        }

        if (map.Children.Count == 1)
        {
            var pair = map.Children.First();
            return ((pair.Key as YamlScalarNode)?.Value, pair.Value);
        }

        return (null, null);
    }

    private static bool TryGet(YamlMappingNode map, string key, out YamlNode? node)
    {
        foreach (var (mapKey, value) in map.Children)
        {
            if (mapKey is YamlScalarNode scalar && string.Equals(scalar.Value, key, StringComparison.Ordinal))
            {
                node = value;
                return true;
            }
        }

        node = null;
        return false;
    }

    private static string? GetScalar(YamlMappingNode map, string key) =>
        TryGet(map, key, out var node) && node is YamlScalarNode scalar ? scalar.Value : null;

    private static Ref GetRef(YamlMappingNode map, string key) =>
        TryGet(map, key, out var node) ? ParseRef(node) : default;

    private static Ref ParseRef(YamlNode? node)
    {
        if (node is not YamlMappingNode map) return default;

        var fileId = long.TryParse(GetScalar(map, "fileID"), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var id)
            ? id
            : 0;

        var guid = GetScalar(map, "guid");
        if (guid == "00000000000000000000000000000000") guid = null;

        return new Ref(fileId, guid);
    }

    private static List<RawBlock> ReadBlocks(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException)
        {
            return [];
        }

        var matches = BlockHeaderRegex().Matches(text);
        var blocks = new List<RawBlock>(matches.Count);

        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var classId = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var fileId = long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            var stripped = match.Groups[3].Success;

            var start = match.Index + match.Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            var body = text[start..end].Trim();
            if (body.Length == 0) continue;

            if (TryParseBlock(body, classId, fileId, stripped) is { } block)
            {
                blocks.Add(block);
                continue;
            }

            if (TryParseBlock(FlattenLegacyData(body), classId, fileId, stripped) is { } legacy) blocks.Add(legacy);
        }

        return blocks;
    }

    private static RawBlock? TryParseBlock(string body, int classId, long fileId, bool stripped)
    {
        try
        {
            var stream = new YamlStream();
            using (var reader = new StringReader(body)) stream.Load(reader);
            if (stream.Documents.Count == 0) return null;
            if (stream.Documents[0].RootNode is not YamlMappingNode root || root.Children.Count == 0) return null;

            var first = root.Children.First();
            if (first.Value is not YamlMappingNode content) return null;

            return new RawBlock
            {
                ClassId = classId,
                FileId = fileId,
                Stripped = stripped,
                RootTypeName = (first.Key as YamlScalarNode)?.Value ?? string.Empty,
                Content = content
            };
        }
        catch (YamlDotNet.Core.YamlException)
        {
            return null;
        }
    }

    private static readonly Regex LegacyDataEntry = new(
        @"^([ \t]*)data:[ \t]*\r?\n[ \t]+first:", RegexOptions.Multiline);

    private static string FlattenLegacyData(string body) =>
        LegacyDataEntry.Replace(body, match => match.Groups[1].Value + "- first:");

    private readonly record struct Ref(long FileId, string? Guid)
    {
        public bool IsExternal => FileId != 0 && !string.IsNullOrEmpty(Guid);
    }

    private sealed record Modification(long TargetFileId, string PropertyPath, string Value);

    private sealed class RawBlock
    {
        public int ClassId { get; init; }
        public long FileId { get; init; }
        public bool Stripped { get; init; }
        public string RootTypeName { get; init; } = string.Empty;
        public YamlMappingNode Content { get; init; } = new();
    }

    [GeneratedRegex(@"^--- !u!(\d+)\s+&(-?\d+)(?:\s+(stripped))?\s*$", RegexOptions.Multiline)]
    private static partial Regex BlockHeaderRegex();

    [GeneratedRegex(@"Shader\s+""([^""]+)""")]
    private static partial Regex ShaderNameRegex();

    [GeneratedRegex(@"(?m)^\s*(?:\[(?<flags>[^\]]+)\]\s*)*(?<name>_\w+)\s*\(\s*""[^""]*""\s*,\s*(?<type>2D|3D|Cube|Color|Range|Float|Int)\b")]
    private static partial Regex ShaderPropertyRegex();

    [GeneratedRegex(@"(?im)^\s*Cull\s+Off\b")]
    private static partial Regex CullOffRegex();

    [GeneratedRegex(@"(?im)^\s*Blend\s+")]
    private static partial Regex BlendRegex();

    [GeneratedRegex(@"(?im)^\s*Blend\s+(?:SrcAlpha|OneMinusSrcAlpha|One\s+OneMinusSrcAlpha|SrcAlpha\s+OneMinusSrcAlpha|One\s+One)\b")]
    private static partial Regex BlendAlphaRegex();

    [GeneratedRegex(@"(?i)_Cutoff|clip\s*\(|_ALPHATEST_ON")]
    private static partial Regex AlphaClipRegex();

    [GeneratedRegex(@"(?i)toon|liltoon|poiyomi|utopia|uts2|npr|anime|cel")]
    private static partial Regex ToonRegex();

    [GeneratedRegex(@"(?i)unlit|ui/|sprites/|particle")]
    private static partial Regex UnlitRegex();
}
