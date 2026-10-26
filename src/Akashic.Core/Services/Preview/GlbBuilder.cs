using Akashic.Core.Models;
using Assimp;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;
using Mesh = Assimp.Mesh;
using ModelRoot = SharpGLTF.Schema2.ModelRoot;
using Scene = Assimp.Scene;
using Matrix4x4 = System.Numerics.Matrix4x4;
using Quaternion = System.Numerics.Quaternion;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

namespace Akashic.Core.Services.Preview;

public sealed class GlbBuilder : IDisposable
{
    private static readonly HashSet<string> GltfTextures = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg"
    };

    private readonly AssimpContext _assimp = new();
    private readonly Dictionary<string, Scene?> _scenes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MaterialBuilder> _materials = new(StringComparer.OrdinalIgnoreCase);

    public int Build(PreviewScene scene, string outputPath, List<string> warnings)
    {
        var gltfScene = new SceneBuilder();
        var meshCount = 0;
        var meshNodes = Flatten(scene.Nodes).Count(item => item.Mesh is not null);

        foreach (var node in Flatten(scene.Nodes))
        {
            if (node.Mesh is null) continue;

            var builder = new MeshBuilder<VertexPositionNormal, VertexTexture1>(node.Name);

            if (node.Mesh.AssetPath.StartsWith("unity-builtin:", StringComparison.OrdinalIgnoreCase))
            {
                var id = long.TryParse(node.Mesh.AssetPath["unity-builtin:".Length..], out var parsed) ? parsed : 10202;
                AddBuiltIn(builder, ResolveMaterial(node.Materials.FirstOrDefault()), id);

                var builtInNode = new NodeBuilder(node.Id) { LocalMatrix = WorldMatrix(scene, node) };
                gltfScene.AddRigidMesh(builder, builtInNode);
                meshCount++;
                continue;
            }

            var assimpScene = LoadScene(node.Mesh.AssetPath);
            if (assimpScene is null) continue;

            var meshes = FindMeshes(assimpScene, node.Mesh.SubMesh, node.Name, meshNodes <= 1);
            if (meshes.Count == 0)
            {
                warnings.Add($"在 {Path.GetFileName(node.Mesh.AssetPath)} 里找不到 {node.Mesh.SubMesh}。");
                continue;
            }

            for (var index = 0; index < meshes.Count; index++)
            {
                var mesh = meshes[index];
                var indices = mesh.GetIndices();
                var triangleCount = indices.Length > 0 ? indices.Length / 3 : mesh.VertexCount / 3;
                if (triangleCount == 0) continue;

                var source = MaterialFor(node, mesh, assimpScene);
                var material = ResolveMaterial(source);
                var primitive = builder.UsePrimitive(material, 3);

                if (indices.Length > 0)
                {
                    for (var i = 0; i + 2 < indices.Length; i += 3)
                        AddTriangle(primitive, mesh, indices[i], indices[i + 1], indices[i + 2]);
                }
                else
                {
                    for (var i = 0; i + 2 < mesh.VertexCount; i += 3)
                        AddTriangle(primitive, mesh, i, i + 1, i + 2);
                }
            }

            if (!builder.Primitives.Any(primitive => primitive.Vertices.Count > 0)) continue;

            var gltfNode = new NodeBuilder(node.Id) { LocalMatrix = WorldMatrix(scene, node) };
            gltfScene.AddRigidMesh(builder, gltfNode);
            meshCount++;
        }

        gltfScene.ToGltf2().SaveGLB(outputPath);
        return meshCount;
    }

    private static void AddTriangle(
        PrimitiveBuilder<MaterialBuilder, VertexPositionNormal, VertexTexture1, VertexEmpty> primitive,
        Mesh mesh, int a, int b, int c)
    {
        var uvs = mesh.HasTextureCoords(0) ? mesh.TextureCoordinateChannels[0] : [];
        var normals = mesh.HasNormals ? mesh.Normals : [];

        VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty> Vertex(int index)
        {
            var position = mesh.Vertices[index];
            var normal = index < normals.Count
                ? normals[index]
                : new Vector3D(0, 1, 0);
            var uv = index < uvs.Count ? uvs[index] : new Vector3D(0, 0, 0);

            var p = new Vector3(position.X, position.Y, position.Z);
            var n = new Vector3(normal.X, normal.Y, normal.Z);
            var geometry = new VertexPositionNormal(p, n);
            var texture = new VertexTexture1(new System.Numerics.Vector2(uv.X, 1f - uv.Y));
            return new VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>(geometry, texture);
        }

        primitive.AddTriangle(Vertex(a), Vertex(b), Vertex(c));
    }

    private MaterialBuilder ResolveMaterial(PreviewMaterial? source)
    {
        var key = source?.Path ?? "$default";
        if (_materials.TryGetValue(key, out var cached)) return cached;

        var name = source?.Name ?? "default";
        var material = new MaterialBuilder(name);

        if (source is null)
        {
            material.WithBaseColor(new Vector4(0.72f, 0.74f, 0.78f, 1))
                .WithMetallicRoughness(0, 0.85f);
        }
        else
        {
            var color = new Vector4(
                Clamp01(source.BaseColor.ElementAtOrDefault(0)),
                Clamp01(source.BaseColor.ElementAtOrDefault(1)),
                Clamp01(source.BaseColor.ElementAtOrDefault(2)),
                source.BaseColor.ElementAtOrDefault(3) is var a && a > 0 ? Math.Clamp(a, 0f, 1f) : 1f);

            var image = LoadImage(source.BaseTexture);
            if (image is not null) material.WithBaseColor(image, color);
            else material.WithBaseColor(color);

            if (source.Procedural && image is null &&
                string.Equals(source.AlphaMode, "blend", StringComparison.OrdinalIgnoreCase))
            {
                material.WithBaseColor(new Vector4(color.X, color.Y, color.Z, 0.25f));
                material.AlphaMode = SharpGLTF.Materials.AlphaMode.BLEND;
                _materials[key] = material;
                return material;
            }

            var unlit = source.Kind == "unlit";
            if (unlit) material.WithUnlitShader();

            if (!unlit)
            {
                var toon = source.Kind is "toon" or "liltoon";
                material.WithMetallicRoughness(toon ? 0f : 0.05f, toon ? 0.95f : 0.65f);

                var normal = LoadImage(source.NormalTexture);
                if (normal is not null) material.WithNormal(normal, source.NormalScale);

                if (source.UseEmission)
                {
                    var emission = LoadImage(source.EmissionTexture);
                    if (emission is not null)
                    {
                        material.WithEmissive(emission,
                            new Vector3(source.EmissionColor.ElementAtOrDefault(0),
                                source.EmissionColor.ElementAtOrDefault(1),
                                source.EmissionColor.ElementAtOrDefault(2)));
                    }
                }
            }

            if (source.DoubleSided) material.DoubleSided = true;

            switch (source.AlphaMode)
            {
                case "mask":
                    material.AlphaMode = SharpGLTF.Materials.AlphaMode.MASK;
                    material.AlphaCutoff = source.Cutoff;
                    break;
                case "blend":
                    material.AlphaMode = SharpGLTF.Materials.AlphaMode.BLEND;
                    break;
            }
        }

        _materials[key] = material;
        return material;
    }

    private static ImageBuilder? LoadImage(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        if (!GltfTextures.Contains(Path.GetExtension(path))) return null;

        try
        {
            ImageBuilder image = File.ReadAllBytes(path);
            return image;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private Scene? LoadScene(string path)
    {
        if (_scenes.TryGetValue(path, out var cached)) return cached;

        Scene? scene = null;
        try
        {
            scene = _assimp.ImportFile(path,
                PostProcessSteps.Triangulate |
                PostProcessSteps.GenerateNormals |
                PostProcessSteps.JoinIdenticalVertices |
                PostProcessSteps.RemoveRedundantMaterials |
                PostProcessSteps.ValidateDataStructure);
        }
        catch (Exception)
        {
        }

        _scenes[path] = scene;
        return scene;
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

    private static Assimp.Node? FindNodeByName(Assimp.Node root, string wanted)
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

    private static void AddBuiltIn(
        MeshBuilder<VertexPositionNormal, VertexTexture1> builder, MaterialBuilder material, long fileId)
    {
        var primitive = builder.UsePrimitive(material, 3);

        switch (fileId)
        {
            case 10207:
                AddQuad(primitive);
                break;
            case 10206:
                AddPlane(primitive);
                break;
            case 10204:
                AddSphere(primitive);
                break;
            case 10203:
            case 10205:
                AddCylinder(primitive);
                break;
            default:
                AddCube(primitive);
                break;
        }
    }

    private static VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty> BuiltInVertex(
        float x, float y, float z, float nx, float ny, float nz, float u, float v)
    {
        var geometry = new VertexPositionNormal(new Vector3(x, y, z), Vector3.Normalize(new Vector3(nx, ny, nz)));
        var texture = new VertexTexture1(new System.Numerics.Vector2(u, v));
        return new VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>(geometry, texture);
    }

    private static void AddCube(PrimitiveBuilder<MaterialBuilder, VertexPositionNormal, VertexTexture1, VertexEmpty> p)
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
            var vertices = corners.Select((corner, index) => BuiltInVertex(
                corner.X, corner.Y, corner.Z, normal.X, normal.Y, normal.Z,
                index is 0 or 3 ? 0 : 1, index < 2 ? 0 : 1)).ToArray();

            p.AddTriangle(vertices[0], vertices[1], vertices[2]);
            p.AddTriangle(vertices[0], vertices[2], vertices[3]);
        }
    }

    private static void AddQuad(PrimitiveBuilder<MaterialBuilder, VertexPositionNormal, VertexTexture1, VertexEmpty> p)
    {
        var v0 = BuiltInVertex(-.5f, -.5f, 0, 0, 0, -1, 0, 0);
        var v1 = BuiltInVertex(.5f, -.5f, 0, 0, 0, -1, 1, 0);
        var v2 = BuiltInVertex(.5f, .5f, 0, 0, 0, -1, 1, 1);
        var v3 = BuiltInVertex(-.5f, .5f, 0, 0, 0, -1, 0, 1);

        p.AddTriangle(v0, v2, v1);
        p.AddTriangle(v0, v3, v2);
    }

    private static void AddPlane(PrimitiveBuilder<MaterialBuilder, VertexPositionNormal, VertexTexture1, VertexEmpty> p)
    {
        var v0 = BuiltInVertex(-5, 0, -5, 0, 1, 0, 0, 0);
        var v1 = BuiltInVertex(5, 0, -5, 0, 1, 0, 1, 0);
        var v2 = BuiltInVertex(5, 0, 5, 0, 1, 0, 1, 1);
        var v3 = BuiltInVertex(-5, 0, 5, 0, 1, 0, 0, 1);

        p.AddTriangle(v0, v2, v1);
        p.AddTriangle(v0, v3, v2);
    }

    private static void AddSphere(PrimitiveBuilder<MaterialBuilder, VertexPositionNormal, VertexTexture1, VertexEmpty> p)
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

                var a = BuiltInVertex(
                    MathF.Sin(v0) * MathF.Cos(u0) * .5f, MathF.Cos(v0) * .5f, MathF.Sin(v0) * MathF.Sin(u0) * .5f,
                    MathF.Sin(v0) * MathF.Cos(u0), MathF.Cos(v0), MathF.Sin(v0) * MathF.Sin(u0),
                    (float)segment / segments, (float)ring / rings);
                var b = BuiltInVertex(
                    MathF.Sin(v1) * MathF.Cos(u0) * .5f, MathF.Cos(v1) * .5f, MathF.Sin(v1) * MathF.Sin(u0) * .5f,
                    MathF.Sin(v1) * MathF.Cos(u0), MathF.Cos(v1), MathF.Sin(v1) * MathF.Sin(u0),
                    (float)segment / segments, (float)(ring + 1) / rings);
                var c = BuiltInVertex(
                    MathF.Sin(v1) * MathF.Cos(u1) * .5f, MathF.Cos(v1) * .5f, MathF.Sin(v1) * MathF.Sin(u1) * .5f,
                    MathF.Sin(v1) * MathF.Cos(u1), MathF.Cos(v1), MathF.Sin(v1) * MathF.Sin(u1),
                    (float)(segment + 1) / segments, (float)(ring + 1) / rings);
                var d = BuiltInVertex(
                    MathF.Sin(v0) * MathF.Cos(u1) * .5f, MathF.Cos(v0) * .5f, MathF.Sin(v0) * MathF.Sin(u1) * .5f,
                    MathF.Sin(v0) * MathF.Cos(u1), MathF.Cos(v0), MathF.Sin(v0) * MathF.Sin(u1),
                    (float)(segment + 1) / segments, (float)ring / rings);

                p.AddTriangle(a, b, c);
                p.AddTriangle(a, c, d);
            }
        }
    }

    private static void AddCylinder(PrimitiveBuilder<MaterialBuilder, VertexPositionNormal, VertexTexture1, VertexEmpty> p)
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

            var a = BuiltInVertex(x0, -1, z0, x0 * 2, 0, z0 * 2, (float)segment / segments, 0);
            var b = BuiltInVertex(x0, 1, z0, x0 * 2, 0, z0 * 2, (float)segment / segments, 1);
            var c = BuiltInVertex(x1, 1, z1, x1 * 2, 0, z1 * 2, (float)(segment + 1) / segments, 1);
            var d = BuiltInVertex(x1, -1, z1, x1 * 2, 0, z1 * 2, (float)(segment + 1) / segments, 0);
            p.AddTriangle(a, b, c);
            p.AddTriangle(a, c, d);

            var topCenter = BuiltInVertex(0, 1, 0, 0, 1, 0, .5f, .5f);
            var topA = BuiltInVertex(x0, 1, z0, 0, 1, 0, x0 + .5f, z0 + .5f);
            var topB = BuiltInVertex(x1, 1, z1, 0, 1, 0, x1 + .5f, z1 + .5f);
            p.AddTriangle(topCenter, topB, topA);

            var bottomCenter = BuiltInVertex(0, -1, 0, 0, -1, 0, .5f, .5f);
            var bottomA = BuiltInVertex(x0, -1, z0, 0, -1, 0, x0 + .5f, z0 + .5f);
            var bottomB = BuiltInVertex(x1, -1, z1, 0, -1, 0, x1 + .5f, z1 + .5f);
            p.AddTriangle(bottomCenter, bottomA, bottomB);
        }
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

    private static Matrix4x4 WorldMatrix(PreviewScene scene, PreviewNode target)
    {
        var chain = new List<PreviewNode>();
        if (!TryFindChain(scene.Nodes, target.Id, chain)) return Mirror;

        var matrix = Matrix4x4.Identity;
        for (var i = chain.Count - 1; i >= 0; i--)
            matrix *= LocalMatrix(chain[i]);

        return Mirror * matrix * Mirror;
    }

    private static readonly Matrix4x4 Mirror = Matrix4x4.CreateScale(-1, 1, 1);

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

    private static float Clamp01(float value) =>
        float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 1f;

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

    public void Dispose() => _assimp.Dispose();
}
