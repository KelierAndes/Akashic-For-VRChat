using System.Numerics;
using Akashic.Core.Models;
using Akashic.Core.Services.Preview;
using Assimp;
using Mesh = Assimp.Mesh;
using Matrix4x4 = System.Numerics.Matrix4x4;
using Quaternion = System.Numerics.Quaternion;
using Scene = Assimp.Scene;

namespace Akashic.Host.Preview.Hlsl;

public sealed class HlslDrawItem
{
    public string Name { get; set; } = string.Empty;

    public byte[] Vertices { get; set; } = [];

    public uint[] Indices { get; set; } = [];

    public int VertexCount => Vertices.Length / HlslVertexLayout.Stride;

    public Matrix4x4 World { get; set; } = Matrix4x4.Identity;

    public PreviewMaterial? Material { get; set; }

    public Vector3 Center { get; set; }

    public float Radius { get; set; }

    public bool Transparent => Material is not null &&
                               (string.Equals(Material.AlphaMode, "blend", StringComparison.OrdinalIgnoreCase) ||
                                Material.ShaderName.Contains("Transparent", StringComparison.OrdinalIgnoreCase) ||
                                (Material.Floats.TryGetValue("_TransparentMode", out var mode) && mode >= 2) ||
                                (Material.Floats.TryGetValue("_SrcBlend", out var src) && src != 1) ||
                                (Material.Floats.TryGetValue("_DstBlend", out var dst) && dst != 0));
}

public sealed class HlslSceneData
{
    public string SessionId { get; set; } = string.Empty;

    public string Root { get; set; } = string.Empty;

    public List<HlslDrawItem> Items { get; set; } = [];

    public Vector3 Center { get; set; }

    public Vector3 BoundsMin { get; set; }

    public Vector3 BoundsMax { get; set; }

    public float Radius { get; set; } = 1;

    public List<PreviewMaterial> Materials { get; set; } = [];

    public List<string> Warnings { get; set; } = [];
}

public static class HlslVertexLayout
{
    public const int Stride = 128;

    public const uint PositionOffset = 0;
    public const uint NormalOffset = 16;
    public const uint TangentOffset = 32;
    public const uint Uv0Offset = 48;
    public const uint Uv1Offset = 64;
    public const uint Uv2Offset = 80;
    public const uint Uv3Offset = 96;
    public const uint ColorOffset = 112;
}

public static class HlslSceneBuilder
{
    public static HlslSceneData Build(string sessionId, PreviewScene scene, bool gizmo = false)
    {
        var result = new HlslSceneData { SessionId = sessionId, Root = scene.Root };
        using var assimp = new AssimpContext();
        var scenes = new Dictionary<string, Scene?>(StringComparer.OrdinalIgnoreCase);
        var synthetic = new Dictionary<string, PreviewMaterial>(StringComparer.OrdinalIgnoreCase);
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var hasBounds = false;
        var meshNodes = Flatten(scene.Nodes).Count(item => item.Mesh is not null);

        var packageMaterials = new Lazy<Dictionary<string, string>>(() => IndexPackageMaterials(scene.Root));
        PrefabParser? parser = null;

        PreviewMaterial? NamedPackageMaterial(string name)
        {
            var key = NormalizeMaterialName(name);
            if (key.Length == 0 || !packageMaterials.Value.TryGetValue(key, out var materialPath)) return null;

            parser ??= new PrefabParser(scene.Root, new UnityMetaIndex(scene.Root));
            var parsed = parser.ParseMaterial(materialPath);
            return parsed is not null && parsed.Shader.Length > 0 ? parsed : null;
        }

        foreach (var node in Flatten(scene.Nodes))
        {
            if (node.Mesh is null) continue;

            var world = WorldMatrix(scene, node);
            if (node.Mesh.AssetPath.StartsWith("unity-builtin:", StringComparison.OrdinalIgnoreCase))
            {
                var id = long.TryParse(node.Mesh.AssetPath["unity-builtin:".Length..], out var parsed) ? parsed : 10202;
                var material = node.Materials.FirstOrDefault();
                var (vertices, indices) = BuiltInGeometry(id);
                if (indices.Length == 0) continue;

                AddItem(result, new HlslDrawItem
                {
                    Name = node.Name,
                    Vertices = vertices,
                    Indices = indices,
                    World = world,
                    Material = material
                }, world, ref min, ref max, ref hasBounds);
                continue;
            }

            var path = node.Mesh.AssetPath;
            if (!File.Exists(path)) continue;

            if (!scenes.TryGetValue(path, out var model))
            {
                model = LoadScene(assimp, path);
                scenes[path] = model;
            }

            if (model is null)
            {
                result.Warnings.Add("模型读取失败：" + Path.GetFileName(path));
                continue;
            }

            var meshes = FindMeshes(model, node.Mesh.SubMesh, node.Name, meshNodes <= 1);
            foreach (var mesh in meshes)
            {
                var material = MaterialFor(node, mesh, model);
                if (material is null || string.IsNullOrEmpty(material.Shader))
                {
                    var key = path + "|" + mesh.MaterialIndex;
                    if (!synthetic.TryGetValue(key, out var generated))
                    {
                        var fallback = SyntheticMaterial(mesh, model, path);
                        generated = NamedPackageMaterial(fallback.Name) ?? fallback;
                        synthetic[key] = generated;
                    }

                    material = generated;
                    ApplyTextureHints(material, mesh, model, path);
                }
                else
                {
                    ApplyTextureHints(material, mesh, model, path);
                }

                var geometry = Geometry(mesh);
                if (geometry.Indices.Length == 0) continue;

                AddItem(result, new HlslDrawItem
                {
                    Name = node.Name,
                    Vertices = geometry.Vertices,
                    Indices = geometry.Indices,
                    World = world,
                    Material = material
                }, world, ref min, ref max, ref hasBounds);
            }
        }

        if (hasBounds)
        {
            result.Center = (min + max) * 0.5f;
            result.BoundsMin = min;
            result.BoundsMax = max;
            result.Radius = MathF.Max(0.0001f, (max - min).Length() * 0.5f);
        }

        if (gizmo && result.Items.Count > 0)
            AddGizmo(result);

        result.Materials = result.Items
            .Select(item => item.Material)
            .Where(material => material is not null)
            .Select(material => material!)
            .Distinct()
            .ToList();

        return result;
    }

    public static HlslSceneData BuildMaterialPreview(string sessionId, PreviewMaterial material)
    {
        var result = new HlslSceneData
        {
            SessionId = sessionId,
            Root = Path.GetDirectoryName(material.Path) ?? string.Empty
        };

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var hasBounds = false;

        var (vertices, indices) = BuiltInGeometry(10204);
        var scale = Matrix4x4.CreateScale(2f, 2f, 2f);

        AddItem(result, new HlslDrawItem
        {
            Name = material.Name,
            Vertices = vertices,
            Indices = indices,
            World = scale,
            Material = material
        }, scale, ref min, ref max, ref hasBounds);

        if (hasBounds)
        {
            result.Center = (min + max) * 0.5f;
            result.BoundsMin = min;
            result.BoundsMax = max;
            result.Radius = MathF.Max(0.0001f, (max - min).Length() * 0.5f);
        }

        result.Materials = [material];
        return result;
    }

    private static void AddGizmo(HlslSceneData result)
    {
        var (cube, indices) = BuiltInGeometry(10202);
        var length = result.Radius * 1.15f;
        var shaft = length * 0.045f;
        var origin = result.BoundsMin - new Vector3(shaft * 2f);

        AddAxis("gizmo-x", new Vector3(length, shaft, shaft), 0xe05050);
        AddAxis("gizmo-y", new Vector3(shaft, length, shaft), 0x50c860);
        AddAxis("gizmo-z", new Vector3(shaft, shaft, length), 0x5080e8);

        void AddAxis(string name, Vector3 scale, int rgb)
        {
            result.Items.Add(new HlslDrawItem
            {
                Name = name,
                Vertices = cube,
                Indices = indices,
                World = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(
                    origin + scale * 0.5f),
                Material = GizmoMaterial(name, rgb),
                Center = origin + scale * 0.5f,
                Radius = length
            });
        }
    }

    private static PreviewMaterial GizmoMaterial(string name, int rgb) => new()
    {
        Name = name,
        ShaderName = "Akashic/Gizmo",
        Kind = "unlit",
        AlphaMode = "blend",
        BaseColor = [
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >> 8) & 0xFF) / 255f,
            (rgb & 0xFF) / 255f,
            1f
        ]
    };

    private static void AddItem(HlslSceneData result, HlslDrawItem item, Matrix4x4 world, ref Vector3 min,
        ref Vector3 max, ref bool hasBounds)
    {
        var localMin = new Vector3(float.MaxValue);
        var localMax = new Vector3(float.MinValue);
        for (var i = 0; i < item.VertexCount; i++)
        {
            var offset = i * HlslVertexLayout.Stride;
            var position = new Vector3(
                BitConverter.ToSingle(item.Vertices, offset),
                BitConverter.ToSingle(item.Vertices, offset + 4),
                BitConverter.ToSingle(item.Vertices, offset + 8));
            localMin = Vector3.Min(localMin, position);
            localMax = Vector3.Max(localMax, position);
            var worldPosition = Vector3.Transform(position, world);
            min = Vector3.Min(min, worldPosition);
            max = Vector3.Max(max, worldPosition);
            hasBounds = true;
        }

        item.Center = Vector3.Transform((localMin + localMax) * 0.5f, world);
        item.Radius = Vector3.Distance(localMax, localMin) * 0.5f;
        if (item.Radius <= 0.0001f) item.Radius = result.Radius;
        result.Items.Add(item);
    }

    private static Scene? LoadScene(AssimpContext assimp, string path)
    {
        try
        {
            return assimp.ImportFile(path,
                PostProcessSteps.Triangulate |
                PostProcessSteps.GenerateNormals |
                PostProcessSteps.JoinIdenticalVertices |
                PostProcessSteps.RemoveRedundantMaterials |
                PostProcessSteps.ValidateDataStructure);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static List<Mesh> FindMeshes(Scene scene, string subMesh, string nodeName, bool allowAll)
    {
        foreach (var candidate in new[] { subMesh, nodeName })
        {
            var matched = MatchMeshes(scene, candidate);
            if (matched.Count > 0) return matched;
        }

        var usable = scene.Meshes.Where(mesh => mesh.VertexCount > 0).ToList();

        if (usable.Count == 1 || allowAll) return usable;

        return [];
    }

    private static List<Mesh> MatchMeshes(Scene scene, string name)
    {
        var result = new List<Mesh>();
        var wanted = NormalizeMeshName(name);
        if (wanted.Length == 0) return result;

        var node = scene.RootNode.FindNode(name) ?? FindNodeByName(scene.RootNode, wanted);
        if (node is not null)
        {
            foreach (var index in node.MeshIndices)
            {
                if (index < 0 || index >= scene.Meshes.Count) continue;
                var mesh = scene.Meshes[index];
                if (mesh.VertexCount > 0) result.Add(mesh);
            }

            if (result.Count > 0) return result;
        }

        foreach (var mesh in scene.Meshes)
        {
            if (mesh.VertexCount > 0 && NormalizeMeshName(mesh.Name) == wanted)
                result.Add(mesh);
        }

        return result;
    }

    private static Node? FindNodeByName(Node root, string wanted)
    {
        if (NormalizeMeshName(root.Name) == wanted && root.MeshIndices.Count > 0) return root;

        foreach (var child in root.Children)
        {
            var found = FindNodeByName(child, wanted);
            if (found is not null) return found;
        }

        return null;
    }

    private static string NormalizeMeshName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var text = new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        if (text.Length > 4 && text.EndsWith("mesh", StringComparison.Ordinal)) text = text[..^4];
        return text;
    }

    private static string NormalizeMaterialName(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static Dictionary<string, string> IndexPackageMaterials(string root)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return map;

        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.mat", SearchOption.AllDirectories))
            {
                if (map.Count >= 4096) break;
                var key = NormalizeMaterialName(Path.GetFileNameWithoutExtension(file));
                if (key.Length > 0) map.TryAdd(key, file);
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

    private static PreviewMaterial? MaterialFor(PreviewNode node, Mesh mesh, Scene scene)
    {
        if (node.Materials.Count == 0) return null;

        if (mesh.MaterialIndex >= 0 && mesh.MaterialIndex < scene.Materials.Count)
        {
            var name = scene.Materials[mesh.MaterialIndex]?.Name;
            if (!string.IsNullOrWhiteSpace(name))
            {
                var matched = node.Materials.FirstOrDefault(material =>
                    string.Equals(material.Name, name, StringComparison.OrdinalIgnoreCase));
                if (matched is not null) return matched;
            }
        }

        if (mesh.MaterialIndex >= 0 && mesh.MaterialIndex < node.Materials.Count)
            return node.Materials[mesh.MaterialIndex];

        return node.Materials[0];
    }

    private static PreviewMaterial SyntheticMaterial(Mesh mesh, Scene scene, string assetPath)
    {
        var source = mesh.MaterialIndex >= 0 && mesh.MaterialIndex < scene.Materials.Count
            ? scene.Materials[mesh.MaterialIndex]
            : null;

        var diffuse = source is not null && source.HasColorDiffuse ? source.ColorDiffuse : new Color4D(1, 1, 1, 1);
        var material = new PreviewMaterial
        {
            Name = string.IsNullOrWhiteSpace(source?.Name)
                ? Path.GetFileNameWithoutExtension(assetPath) + "_" + Math.Max(mesh.MaterialIndex, 0)
                : source.Name,
            ShaderName = "Standard",
            BuiltInShader = "Standard",
            ShaderFamily = "Standard",
            Kind = "lit",
            BaseColor = [diffuse.R, diffuse.G, diffuse.B, diffuse.A],
            DoubleSided = source?.IsTwoSided == true,
            AlphaMode = source is not null &&
                        ((source.HasOpacity && source.Opacity < 0.999f) ||
                         (source.HasTextureOpacity && !string.IsNullOrEmpty(source.TextureOpacity.FilePath)))
                ? "blend"
                : "opaque"
        };

        return material;
    }

    private static void ApplyTextureHints(PreviewMaterial material, Mesh mesh, Scene scene, string assetPath)
    {
        if (mesh.MaterialIndex < 0 || mesh.MaterialIndex >= scene.Materials.Count) return;
        var source = scene.Materials[mesh.MaterialIndex];
        var directory = Path.GetDirectoryName(assetPath) ?? string.Empty;

        var diffuse = ResolveTexture(source.HasTextureDiffuse ? source.TextureDiffuse.FilePath : string.Empty, directory);
        var normal = ResolveTexture(source.HasTextureNormal ? source.TextureNormal.FilePath : string.Empty, directory);
        var emissive = ResolveTexture(source.HasTextureEmissive ? source.TextureEmissive.FilePath : string.Empty, directory);

        if (diffuse.Length > 0)
        {
            if (material.BaseTexture.Length == 0) material.BaseTexture = diffuse;
            SetTexture(material, "_MainTex", diffuse);
            SetTexture(material, "_BaseMap", diffuse);
        }

        if (normal.Length > 0)
        {
            if (material.NormalTexture.Length == 0) material.NormalTexture = normal;
            SetTexture(material, "_BumpMap", normal);
            SetTexture(material, "_NormalMap", normal);
        }

        if (emissive.Length > 0)
        {
            if (material.EmissionTexture.Length == 0) material.EmissionTexture = emissive;
            SetTexture(material, "_EmissionMap", emissive);
        }
    }

    private static void SetTexture(PreviewMaterial material, string slot, string path)
    {
        if (!material.Textures.TryGetValue(slot, out var existing) || string.IsNullOrWhiteSpace(existing))
            material.Textures[slot] = path;
    }

    private static string ResolveTexture(string path, string directory)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('*', StringComparison.Ordinal)) return string.Empty;
        var full = Path.IsPathRooted(path) ? path : Path.Combine(directory, path);
        try
        {
            full = Path.GetFullPath(full);
        }
        catch (Exception)
        {
            return string.Empty;
        }

        return File.Exists(full) ? full : string.Empty;
    }

    private static (byte[] Vertices, uint[] Indices) Geometry(Mesh mesh)
    {
        var normals = mesh.HasNormals ? mesh.Normals : [];
        var tangents = mesh.HasTangentBasis ? mesh.Tangents : [];
        var colors = mesh.HasVertexColors(0) ? mesh.VertexColorChannels[0] : [];
        var uv0 = mesh.HasTextureCoords(0) ? mesh.TextureCoordinateChannels[0] : [];
        var uv1 = mesh.HasTextureCoords(1) ? mesh.TextureCoordinateChannels[1] : [];
        var uv2 = mesh.HasTextureCoords(2) ? mesh.TextureCoordinateChannels[2] : [];
        var uv3 = mesh.HasTextureCoords(3) ? mesh.TextureCoordinateChannels[3] : [];
        var vertices = new byte[mesh.VertexCount * HlslVertexLayout.Stride];

        for (var i = 0; i < mesh.VertexCount; i++)
        {
            var position = mesh.Vertices[i];
            var normal = i < normals.Count ? normals[i] : new Vector3D(0, 1, 0);
            if (normal.X == 0 && normal.Y == 0 && normal.Z == 0) normal = new Vector3D(0, 1, 0);
            var tangent = i < tangents.Count ? tangents[i] : new Vector3D(1, 0, 0);
            if (tangent.X == 0 && tangent.Y == 0 && tangent.Z == 0) tangent = new Vector3D(1, 0, 0);
            var color = i < colors.Count ? colors[i] : new Color4D(1, 1, 1, 1);
            var offset = i * HlslVertexLayout.Stride;

            WriteVector4(vertices, offset + (int)HlslVertexLayout.PositionOffset,
                -position.X, position.Y, position.Z, 1);
            WriteVector4(vertices, offset + (int)HlslVertexLayout.NormalOffset,
                -normal.X, normal.Y, normal.Z, 0);
            WriteVector4(vertices, offset + (int)HlslVertexLayout.TangentOffset,
                -tangent.X, tangent.Y, tangent.Z, -1);
            WriteUv(vertices, offset + (int)HlslVertexLayout.Uv0Offset, uv0, i);
            WriteUv(vertices, offset + (int)HlslVertexLayout.Uv1Offset, uv1, i);
            WriteUv(vertices, offset + (int)HlslVertexLayout.Uv2Offset, uv2, i);
            WriteUv(vertices, offset + (int)HlslVertexLayout.Uv3Offset, uv3, i);
            WriteVector4(vertices, offset + (int)HlslVertexLayout.ColorOffset,
                color.R, color.G, color.B, color.A);
        }

        var sourceIndices = mesh.GetIndices();
        uint[] indices;
        if (sourceIndices is { Length: > 0 })
        {
            indices = new uint[sourceIndices.Length];
            for (var i = 0; i < sourceIndices.Length; i++) indices[i] = (uint)sourceIndices[i];
            for (var i = 0; i + 2 < indices.Length; i += 3)
                (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
        }
        else
        {
            indices = new uint[mesh.VertexCount];
            for (var i = 0; i < indices.Length; i++) indices[i] = (uint)i;
        }

        return (vertices, indices);
    }

    private static void WriteUv(byte[] buffer, int offset, List<Vector3D> uvs, int index)
    {
        var uv = index < uvs.Count ? uvs[index] : new Vector3D(0, 0, 0);
        WriteVector4(buffer, offset, uv.X, 1f - uv.Y, 0, 0);
    }

    private static void WriteVector4(byte[] buffer, int offset, float x, float y, float z, float w)
    {
        BitConverter.TryWriteBytes(buffer.AsSpan(offset), x);
        BitConverter.TryWriteBytes(buffer.AsSpan(offset + 4), y);
        BitConverter.TryWriteBytes(buffer.AsSpan(offset + 8), z);
        BitConverter.TryWriteBytes(buffer.AsSpan(offset + 12), w);
    }

    private static (byte[] Vertices, uint[] Indices) BuiltInGeometry(long fileId)
    {
        var vertices = new List<byte>();
        var indices = new List<uint>();

        void Vertex(float x, float y, float z, float nx, float ny, float nz, float u, float v)
        {
            var buffer = new byte[HlslVertexLayout.Stride];
            var length = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
            if (length < 0.0001f) { nx = 0; ny = 1; nz = 0; length = 1; }
            WriteVector4(buffer, (int)HlslVertexLayout.PositionOffset, x, y, z, 1);
            WriteVector4(buffer, (int)HlslVertexLayout.NormalOffset, nx / length, ny / length, nz / length, 0);
            WriteVector4(buffer, (int)HlslVertexLayout.TangentOffset, 1, 0, 0, 1);
            WriteVector4(buffer, (int)HlslVertexLayout.Uv0Offset, u, 1 - v, 0, 0);
            WriteVector4(buffer, (int)HlslVertexLayout.ColorOffset, 1, 1, 1, 1);
            vertices.AddRange(buffer);
        }

        switch (fileId)
        {
            case 10207:
                Vertex(-.5f, -.5f, 0, 0, 0, -1, 0, 0);
                Vertex(.5f, -.5f, 0, 0, 0, -1, 1, 0);
                Vertex(.5f, .5f, 0, 0, 0, -1, 1, 1);
                Vertex(-.5f, .5f, 0, 0, 0, -1, 0, 1);
                indices.AddRange([0, 2, 1, 0, 3, 2]);
                break;
            case 10206:
                Vertex(-5, 0, -5, 0, 1, 0, 0, 0);
                Vertex(5, 0, -5, 0, 1, 0, 1, 0);
                Vertex(5, 0, 5, 0, 1, 0, 1, 1);
                Vertex(-5, 0, 5, 0, 1, 0, 0, 1);
                indices.AddRange([0, 2, 1, 0, 3, 2]);
                break;
            case 10204:
            {
                const int segments = 24;
                const int rings = 16;
                for (var ring = 0; ring < rings; ring++)
                {
                    var v0 = MathF.PI * ring / rings;
                    var v1 = MathF.PI * (ring + 1) / rings;
                    for (var segment = 0; segment < segments; segment++)
                    {
                        var u0 = 2 * MathF.PI * segment / segments;
                        var u1 = 2 * MathF.PI * (segment + 1) / segments;
                        Vertex(MathF.Sin(v0) * MathF.Cos(u0) * .5f, MathF.Cos(v0) * .5f,
                            MathF.Sin(v0) * MathF.Sin(u0) * .5f, MathF.Sin(v0) * MathF.Cos(u0), MathF.Cos(v0),
                            MathF.Sin(v0) * MathF.Sin(u0), (float)segment / segments, (float)ring / rings);
                        Vertex(MathF.Sin(v1) * MathF.Cos(u0) * .5f, MathF.Cos(v1) * .5f,
                            MathF.Sin(v1) * MathF.Sin(u0) * .5f, MathF.Sin(v1) * MathF.Cos(u0), MathF.Cos(v1),
                            MathF.Sin(v1) * MathF.Sin(u0), (float)segment / segments, (float)(ring + 1) / rings);
                        Vertex(MathF.Sin(v1) * MathF.Cos(u1) * .5f, MathF.Cos(v1) * .5f,
                            MathF.Sin(v1) * MathF.Sin(u1) * .5f, MathF.Sin(v1) * MathF.Cos(u1), MathF.Cos(v1),
                            MathF.Sin(v1) * MathF.Sin(u1), (float)(segment + 1) / segments, (float)(ring + 1) / rings);
                        Vertex(MathF.Sin(v0) * MathF.Cos(u1) * .5f, MathF.Cos(v0) * .5f,
                            MathF.Sin(v0) * MathF.Sin(u1) * .5f, MathF.Sin(v0) * MathF.Cos(u1), MathF.Cos(v0),
                            MathF.Sin(v0) * MathF.Sin(u1), (float)(segment + 1) / segments, (float)ring / rings);
                        var start = (uint)(vertices.Count / HlslVertexLayout.Stride) - 4;
                        indices.Add(start);
                        indices.Add(start + 1);
                        indices.Add(start + 2);
                        indices.Add(start);
                        indices.Add(start + 2);
                        indices.Add(start + 3);
                    }
                }

                break;
            }
            case 10203:
            case 10205:
            {
                const int segments = 24;
                for (var segment = 0; segment < segments; segment++)
                {
                    var u0 = 2 * MathF.PI * segment / segments;
                    var u1 = 2 * MathF.PI * (segment + 1) / segments;
                    var x0 = MathF.Cos(u0) * .5f;
                    var z0 = MathF.Sin(u0) * .5f;
                    var x1 = MathF.Cos(u1) * .5f;
                    var z1 = MathF.Sin(u1) * .5f;
                    Vertex(x0, -1, z0, x0 * 2, 0, z0 * 2, (float)segment / segments, 0);
                    Vertex(x0, 1, z0, x0 * 2, 0, z0 * 2, (float)segment / segments, 1);
                    Vertex(x1, 1, z1, x1 * 2, 0, z1 * 2, (float)(segment + 1) / segments, 1);
                    Vertex(x1, -1, z1, x1 * 2, 0, z1 * 2, (float)(segment + 1) / segments, 0);
                    var start = (uint)(vertices.Count / HlslVertexLayout.Stride) - 4;
                    indices.Add(start);
                    indices.Add(start + 1);
                    indices.Add(start + 2);
                    indices.Add(start);
                    indices.Add(start + 2);
                    indices.Add(start + 3);
                }

                break;
            }
            default:
            {
                var faces = new (Vector3 Normal, Vector3[] Corners)[]
                {
                    (new Vector3(0, 0, 1), [new(-.5f, -.5f, .5f), new(.5f, -.5f, .5f), new(.5f, .5f, .5f), new(-.5f, .5f, .5f)]),
                    (new Vector3(0, 0, -1), [new(.5f, -.5f, -.5f), new(-.5f, -.5f, -.5f), new(-.5f, .5f, -.5f), new(.5f, .5f, -.5f)]),
                    (new Vector3(1, 0, 0), [new(.5f, -.5f, .5f), new(.5f, -.5f, -.5f), new(.5f, .5f, -.5f), new(.5f, .5f, .5f)]),
                    (new Vector3(-1, 0, 0), [new(-.5f, -.5f, -.5f), new(-.5f, -.5f, .5f), new(-.5f, .5f, .5f), new(-.5f, .5f, -.5f)]),
                    (new Vector3(0, 1, 0), [new(-.5f, .5f, .5f), new(.5f, .5f, .5f), new(.5f, .5f, -.5f), new(-.5f, .5f, -.5f)]),
                    (new Vector3(0, -1, 0), [new(-.5f, -.5f, -.5f), new(.5f, -.5f, -.5f), new(.5f, -.5f, .5f), new(-.5f, -.5f, .5f)])
                };

                foreach (var (normal, corners) in faces)
                {
                    for (var i = 0; i < 4; i++)
                    {
                        var corner = corners[i];
                        Vertex(corner.X, corner.Y, corner.Z, normal.X, normal.Y, normal.Z,
                            i is 0 or 3 ? 0 : 1, i < 2 ? 0 : 1);
                    }

                    var start = (uint)(vertices.Count / HlslVertexLayout.Stride) - 4;
                    indices.Add(start);
                    indices.Add(start + 1);
                    indices.Add(start + 2);
                    indices.Add(start);
                    indices.Add(start + 2);
                    indices.Add(start + 3);
                }

                break;
            }
        }

        return (vertices.ToArray(), indices.ToArray());
    }

    private static Matrix4x4 WorldMatrix(PreviewScene scene, PreviewNode target)
    {
        var chain = new List<PreviewNode>();
        if (!TryFindChain(scene.Nodes, target.Id, chain)) return Matrix4x4.Identity;

        var matrix = Matrix4x4.Identity;
        for (var i = chain.Count - 1; i >= 0; i--) matrix *= LocalMatrix(chain[i]);
        return matrix;
    }

    private static bool TryFindChain(IEnumerable<PreviewNode> nodes, string id, List<PreviewNode> chain)
    {
        foreach (var node in nodes)
        {
            chain.Add(node);
            if (node.Id == id) return true;
            if (TryFindChain(node.Children, id, chain)) return true;
            chain.RemoveAt(chain.Count - 1);
        }

        return false;
    }

    private static Matrix4x4 LocalMatrix(PreviewNode node)
    {
        var px = Safe(node.Position.ElementAtOrDefault(0), 0);
        var py = Safe(node.Position.ElementAtOrDefault(1), 0);
        var pz = Safe(node.Position.ElementAtOrDefault(2), 0);
        var sx = NonZero(Safe(node.Scale.ElementAtOrDefault(0), 1));
        var sy = NonZero(Safe(node.Scale.ElementAtOrDefault(1), 1));
        var sz = NonZero(Safe(node.Scale.ElementAtOrDefault(2), 1));
        var rotation = new Quaternion(
            Safe(node.Rotation.ElementAtOrDefault(0), 0),
            Safe(node.Rotation.ElementAtOrDefault(1), 0),
            Safe(node.Rotation.ElementAtOrDefault(2), 0),
            Safe(node.Rotation.ElementAtOrDefault(3), 1));

        if (rotation.LengthSquared() < 1e-8f) rotation = Quaternion.Identity;
        else rotation = Quaternion.Normalize(rotation);

        return Matrix4x4.CreateScale(sx, sy, sz)
               * Matrix4x4.CreateFromQuaternion(rotation)
               * Matrix4x4.CreateTranslation(px, py, pz);
    }

    private static float Safe(float value, float fallback) =>
        float.IsFinite(value) ? value : fallback;

    private static float NonZero(float value) =>
        MathF.Abs(value) < 1e-5f ? MathF.CopySign(1e-5f, value == 0 ? 1 : value) : value;

    private static IEnumerable<PreviewNode> Flatten(IEnumerable<PreviewNode> roots)
    {
        foreach (var node in roots)
        {
            yield return node;
            foreach (var child in Flatten(node.Children)) yield return child;
        }
    }
}
