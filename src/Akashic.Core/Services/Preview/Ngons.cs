using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Assimp;

namespace Akashic.Core.Services.Preview;

public sealed record NgonsResult(int ExpectedTriangles, int ProducedTriangles, int UnresolvedPolygons);

public static class Ngons
{
    public static NgonsResult Triangulate(Scene scene)
    {
        int expected = 0, produced = 0, unresolved = 0;
        foreach (var mesh in scene.Meshes)
        {
            var faces = mesh.Faces;
            if (faces.Count == 0 || faces.All(f => f.IndexCount == 3)) continue;

            var positions = mesh.Vertices.Select(v => new Vector3(v.X, v.Y, v.Z)).ToArray();
            var normals = mesh.HasNormals && mesh.Normals.Count == mesh.VertexCount
                ? mesh.Normals.Select(v => new Vector3(v.X, v.Y, v.Z)).ToArray()
                : null;

            var rebuilt = new List<Face>(faces.Sum(f => Math.Max(0, f.IndexCount - 2)));
            foreach (var face in faces)
            {
                expected += Math.Max(0, face.IndexCount - 2);
                var before = rebuilt.Count;
                Split(face, positions, normals, rebuilt);
                if (face.IndexCount > 3 && rebuilt.Count == before) unresolved++;
            }

            faces.Clear();
            foreach (var face in rebuilt) faces.Add(face);
            produced += rebuilt.Count;
        }
        return new NgonsResult(expected, produced, unresolved);
    }

    private static void Split(Face face, Vector3[] positions, Vector3[]? normals, List<Face> output)
    {
        var indices = face.Indices;
        if (indices.Count == 3)
        {
            output.Add(NewFace(indices[0], indices[1], indices[2]));
            return;
        }
        if (indices.Count < 4) return;

        var loop = Compact(indices, positions);
        if (loop.Count < 3) return;

        if (loop.Count == 4)
        {
            var direct = new[]
            {
                FaceNormal(positions[loop[0]], positions[loop[1]], positions[loop[2]]),
                FaceNormal(positions[loop[0]], positions[loop[2]], positions[loop[3]]),
            };
            var flipped = new[]
            {
                FaceNormal(positions[loop[0]], positions[loop[1]], positions[loop[3]]),
                FaceNormal(positions[loop[1]], positions[loop[2]], positions[loop[3]]),
            };
            var directGood = !IsDegenerate(direct[0]) && !IsDegenerate(direct[1]);
            var flippedGood = !IsDegenerate(flipped[0]) && !IsDegenerate(flipped[1]);
            var useDirect = directGood && (!flippedGood ||
                Vector3.Dot(direct[0], direct[1]) >= Vector3.Dot(flipped[0], flipped[1]));

            if (useDirect)
            {
                output.Add(NewFace(loop[0], loop[1], loop[2]));
                output.Add(NewFace(loop[0], loop[2], loop[3]));
            }
            else
            {
                output.Add(NewFace(loop[0], loop[1], loop[3]));
                output.Add(NewFace(loop[1], loop[2], loop[3]));
            }
            return;
        }

        EarClip(loop, positions, normals, output);
    }

    private static List<int> Compact(IReadOnlyList<int> indices, Vector3[] positions)
    {
        var loop = new List<int>(indices.Count);
        foreach (var index in indices)
        {
            if (index < 0 || index >= positions.Length) continue;
            if (loop.Count > 0 && loop[^1] == index) continue;
            loop.Add(index);
        }
        if (loop.Count > 2 && loop[0] == loop[^1]) loop.RemoveAt(loop.Count - 1);
        return loop;
    }

    private static void EarClip(List<int> loop, Vector3[] positions, Vector3[]? normals, List<Face> output)
    {
        var plane = NewellNormal(loop, positions);
        if (plane.LengthSquared() < 1e-12f && normals != null)
        {
            var sum = Vector3.Zero;
            foreach (var i in loop) sum += normals[i];
            plane = sum;
        }
        if (plane.LengthSquared() < 1e-12f) plane = Vector3.UnitZ;
        plane = Vector3.Normalize(plane);
        if (MathF.Abs(plane.Z) > 0.9f)
            plane = Vector3.Normalize(plane + new Vector3(0.05f, 0.05f, 0f));

        var u = Vector3.Normalize(Vector3.Cross(plane, Vector3.UnitZ));
        var v = Vector3.Normalize(Vector3.Cross(plane, u));

        var remaining = new List<int>(loop);
        var flat = loop.Select(i =>
        {
            var p = positions[i];
            return new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, v));
        }).ToList();

        var guard = remaining.Count * 4 + 8;
        while (remaining.Count > 3 && guard-- > 0)
        {
            var count = remaining.Count;
            var ear = -1;
            for (var attempt = 0; attempt < count && ear < 0; attempt++)
            {
                var k = attempt;
                var prev = (k + count - 1) % count;
                var next = (k + 1) % count;
                var a = flat[prev]; var b = flat[k]; var c = flat[next];
                if (Cross(a, b, c) <= 1e-12f) continue;
                var occupied = false;
                for (var m = 0; m < count; m++)
                {
                    if (m == prev || m == k || m == next) continue;
                    if (PointInTriangle(flat[m], a, b, c)) { occupied = true; break; }
                }
                if (!occupied) ear = k;
            }

            if (ear < 0)
            {
                while (remaining.Count > 3)
                {
                    var last = remaining.Count - 1;
                    output.Add(NewFace(remaining[0], remaining[last - 1], remaining[last]));
                    remaining.RemoveAt(last - 1);
                }
                if (remaining.Count == 3) output.Add(NewFace(remaining[0], remaining[1], remaining[2]));
                return;
            }

            output.Add(NewFace(
                remaining[(ear + remaining.Count - 1) % remaining.Count],
                remaining[ear],
                remaining[(ear + 1) % remaining.Count]));
            flat.RemoveAt(ear);
            remaining.RemoveAt(ear);
        }

        if (remaining.Count == 3) output.Add(NewFace(remaining[0], remaining[1], remaining[2]));
    }

    private static Face NewFace(int a, int b, int c) => new(new[] { a, b, c });

    private static float Cross(Vector2 a, Vector2 b, Vector2 c)
        => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        var d1 = Cross(a, b, p);
        var d2 = Cross(b, c, p);
        var d3 = Cross(c, a, p);
        return (d1 >= 0f && d2 >= 0f && d3 >= 0f) || (d1 <= 0f && d2 <= 0f && d3 <= 0f);
    }

    private static Vector3 FaceNormal(Vector3 a, Vector3 b, Vector3 c) => Vector3.Cross(b - a, c - a);

    private static bool IsDegenerate(Vector3 normal) => normal.LengthSquared() < 1e-14f;

    private static Vector3 NewellNormal(IReadOnlyList<int> loop, Vector3[] positions)
    {
        var n = Vector3.Zero;
        for (var i = 0; i < loop.Count; i++)
        {
            var cur = positions[loop[i]];
            var nxt = positions[loop[(i + 1) % loop.Count]];
            n.X += (cur.Y - nxt.Y) * (cur.Z + nxt.Z);
            n.Y += (cur.Z - nxt.Z) * (cur.X + nxt.X);
            n.Z += (cur.X - nxt.X) * (cur.Y + nxt.Y);
        }
        return n;
    }
}
