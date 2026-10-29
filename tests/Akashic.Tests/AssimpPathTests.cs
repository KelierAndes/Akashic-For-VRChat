using Akashic.Core.Services.Preview;
using Assimp;
using Xunit;

namespace Akashic.Tests;

public sealed class AssimpPathTests
{
    private const string Cube = """
        v -1 -1 -1
        v -1 -1 1
        v -1 1 -1
        v -1 1 1
        v 1 -1 -1
        v 1 -1 1
        v 1 1 -1
        v 1 1 1
        f 1 3 7 5
        f 2 6 8 4
        f 1 5 6 2
        f 3 7 8 4
        f 1 2 4 3
        f 5 6 8 7
        """;

    [Fact]
    public void OpenableKeepsPlainPathsUntouched()
    {
        var path = Path.Combine(Path.GetTempPath(), "akashic", "model.fbx");
        Assert.Same(path, AssimpPaths.Openable(path));
    }

    [Fact]
    public void OpenableTurnsNonAsciiPathIntoOneAssimpCanOpen()
    {
        var root = Directory.CreateTempSubdirectory("akashic-assimp-").FullName;

        try
        {
            var folder = Path.Combine(root, "♥Prefab♥", "이루아_IruaStore");
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, "Airi.fbx");
            File.WriteAllText(file, "placeholder bytes");

            var openable = AssimpPaths.Openable(file);

            Assert.All(openable, character => Assert.True(character <= 127, $"仍有非 ASCII 字符：{openable}"));
            Assert.True(File.Exists(openable), $"转换后的路径打不开：{openable}");
            Assert.Equal(new FileInfo(file).Length, new FileInfo(openable).Length);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AssimpImportsThroughTheOpenablePath()
    {
        var root = Directory.CreateTempSubdirectory("akashic-assimp-").FullName;

        try
        {
            var folder = Path.Combine(root, "이루아_IruaStore", "♥Prefab♥");
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, "cube.obj");
            File.WriteAllText(file, Cube);

            var scene = new AssimpContext().ImportFile(AssimpPaths.Openable(file),
                PostProcessSteps.Triangulate | PostProcessSteps.GenerateNormals);

            Assert.NotNull(scene);
            Assert.Equal(1, scene.MeshCount);
            Assert.True(scene.Meshes[0].FaceCount > 0);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
