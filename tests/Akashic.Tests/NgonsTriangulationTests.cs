using System.Numerics;
using Akashic.Core.Services.Preview;
using Assimp;
using Xunit;

namespace Akashic.Tests;

public class NgonsTriangulationTests
{
    [Fact]
    public void PentagonsAndQuadsExpandWithoutLosingArea()
    {
        var scene = new Scene();
        var mesh = new Mesh("cap") { MaterialIndex = 3 };
        mesh.Vertices.Add(new Vector3D(0, 0, 0));
        mesh.Vertices.Add(new Vector3D(2, 0, 0));
        mesh.Vertices.Add(new Vector3D(2, 0, 1));
        mesh.Vertices.Add(new Vector3D(0, 0, 1));
        mesh.Vertices.Add(new Vector3D(-1, 0, 0.5f));
        mesh.Normals.Add(new Vector3D(0, 1, 0));
        mesh.Normals.Add(new Vector3D(0, 1, 0));
        mesh.Normals.Add(new Vector3D(0, 1, 0));
        mesh.Normals.Add(new Vector3D(0, 1, 0));
        mesh.Normals.Add(new Vector3D(0, 1, 0));
        mesh.Faces.Add(new Face(new[] { 0, 1, 2 }));
        mesh.Faces.Add(new Face(new[] { 0, 2, 3, 4 }));
        scene.Meshes.Add(mesh);

        var result = Ngons.Triangulate(scene);

        Assert.Equal(3, result.ExpectedTriangles);
        Assert.Equal(3, result.ProducedTriangles);
        Assert.Equal(0, result.UnresolvedPolygons);
        Assert.All(mesh.Faces, face => Assert.Equal(3, face.IndexCount));
        
        Assert.Equal(2.5f, mesh.Faces.Sum(f => Area(mesh, f)), 4);
        AssertSameSide(mesh);
    }

    [Fact]
    public void ConcaveHexagonIsSplitWithoutInvertedTriangles()
    {
        var scene = new Scene();
        var mesh = new Mesh("concave");
        Span<float> x = [0, 2, 2, 1, 0, 0];
        Span<float> z = [0, 0, 2, 1, 2, 2];
        for (var i = 0; i < 6; i++)
        {
            mesh.Vertices.Add(new Vector3D(x[i], 0, z[i]));
            mesh.Normals.Add(new Vector3D(0, 1, 0));
        }
        mesh.Faces.Add(new Face(new[] { 0, 1, 2, 3, 4, 5 }));
        scene.Meshes.Add(mesh);

        var result = Ngons.Triangulate(scene);

        Assert.Equal(4, result.ProducedTriangles);
        Assert.Equal(3f, mesh.Faces.Sum(f => Area(mesh, f)), 4);
        AssertSameSide(mesh);
    }

    [Fact]
    public void RepeatedVertexInLoopIsCompacted()
    {
        var scene = new Scene();
        var mesh = new Mesh("duplicated");
        mesh.Vertices.Add(new Vector3D(0, 0, 0));
        mesh.Vertices.Add(new Vector3D(1, 0, 0));
        mesh.Vertices.Add(new Vector3D(1, 0, 1));
        mesh.Vertices.Add(new Vector3D(0, 0, 1));
        mesh.Faces.Add(new Face(new[] { 0, 1, 2, 2, 3 }));
        scene.Meshes.Add(mesh);

        var result = Ngons.Triangulate(scene);

        Assert.Equal(2, result.ProducedTriangles);
        Assert.Equal(1f, mesh.Faces.Sum(f => Area(mesh, f)), 4);
    }

    private static void AssertSameSide(Mesh mesh)
    {
        var signs = mesh.Faces
            .Select(f => Math.Sign(Normal(mesh, f).Y))
            .Where(s => s != 0)
            .Distinct()
            .ToList();
        Assert.Single(signs);
    }

    private static float Area(Mesh mesh, Face face)
    {
        var a = ToVector(mesh.Vertices[(int)face.Indices[0]]);
        var b = ToVector(mesh.Vertices[(int)face.Indices[1]]);
        var c = ToVector(mesh.Vertices[(int)face.Indices[2]]);
        return Vector3.Cross(b - a, c - a).Length() * 0.5f;
    }

    private static Vector3 Normal(Mesh mesh, Face face)
    {
        var a = ToVector(mesh.Vertices[(int)face.Indices[0]]);
        var b = ToVector(mesh.Vertices[(int)face.Indices[1]]);
        var c = ToVector(mesh.Vertices[(int)face.Indices[2]]);
        return Vector3.Cross(b - a, c - a);
    }

    private static Vector3 ToVector(Vector3D v) => new(v.X, v.Y, v.Z);
}
