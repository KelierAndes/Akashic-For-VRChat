using Akashic.Core.Services.Preview.Shader;
using Xunit;

namespace Akashic.Tests;

public class ShaderIncludeResolverTests
{
    [Fact]
    public void ExpandsNestedIncludesInsideRoot()
    {
        var root = Fixture(["main.hlsl|#include \"a.hlsl\"\nfloat mainValue;",
            "a.hlsl|#include \"b.hlsl\"\nfloat aValue;",
            "b.hlsl|float bValue;"]);

        var result = ShaderIncludeResolver.Expand("#include \"main.hlsl\"\nfloat tail;", [root]);

        Assert.True(result.Ok);
        Assert.Contains("float bValue;", result.Source);
        Assert.Contains("float aValue;", result.Source);
        Assert.Contains("float tail;", result.Source);
    }

    [Fact]
    public void ExpandsShimIncludeFromSecondRoot()
    {
        var shim = Fixture(["UnityShim.hlsl|#define SHIM_BODY 1"]);
        var shaderDir = Fixture(["only.hlsl|float v;"]);

        var result = ShaderIncludeResolver.Expand("#include \"UnityShim.hlsl\"", [shaderDir, shim]);

        Assert.True(result.Ok);
        Assert.Contains("#define SHIM_BODY 1", result.Source);
    }

    [Fact]
    public void ResolvesParentTraversalThatStaysInsideTheRoot()
    {
        var package = Directory.CreateTempSubdirectory("akashic-inc-");
        try
        {
            var shaders = Path.Combine(package.FullName, "Assets", "Shaders");
            var shared = Path.Combine(package.FullName, "Assets", "Common");
            Directory.CreateDirectory(shaders);
            Directory.CreateDirectory(shared);
            File.WriteAllText(Path.Combine(shared, "common.hlsl"), "float sharedValue;");

            var result = ShaderIncludeResolver.Expand("#include \"../Common/common.hlsl\"",
                [shaders, package.FullName]);

            Assert.True(result.Ok);
            Assert.Contains("float sharedValue;", result.Source);
        }
        finally
        {
            package.Delete(true);
        }
    }

    [Fact]
    public void RejectsParentTraversalWithoutReadingTheFile()
    {
        var outer = Directory.CreateTempSubdirectory("akashic-inc-");
        try
        {
            File.WriteAllText(Path.Combine(outer.FullName, "secret.hlsl"), "SECRET_MARKER");
            var package = Path.Combine(outer.FullName, "package");
            var shaders = Path.Combine(package, "Shaders");
            Directory.CreateDirectory(shaders);

            var result = ShaderIncludeResolver.Expand("#include \"../../secret.hlsl\"", [shaders, package]);

            Assert.False(result.Ok);
            Assert.Contains("../../secret.hlsl", result.Rejected);
            Assert.DoesNotContain("SECRET_MARKER", result.Source);
        }
        finally
        {
            outer.Delete(true);
        }
    }

    [Fact]
    public void RejectsAbsolutePath()
    {
        var root = Fixture(["a.hlsl|float a;"]);

        var result = ShaderIncludeResolver.Expand("#include \"C:\\Windows\\win.ini\"", [root]);

        Assert.False(result.Ok);
        Assert.Single(result.Rejected);
        Assert.DoesNotContain("win.ini", result.Source);
    }

    [Fact]
    public void KeepsMissingIncludeForTheCompilerToReport()
    {
        var root = Fixture(["a.hlsl|float a;"]);

        var result = ShaderIncludeResolver.Expand("#include \"nope.hlsl\"\nfloat tail;", [root]);

        Assert.True(result.Ok);
        Assert.Contains("nope.hlsl", result.Missing);
        Assert.Contains("#include \"nope.hlsl\"", result.Source);
        Assert.Contains("float tail;", result.Source);
    }

    [Fact]
    public void StopsAtIncludeCycle()
    {
        var root = Fixture(["a.hlsl|#include \"b.hlsl\"\nfloat a;",
            "b.hlsl|#include \"a.hlsl\"\nfloat b;"]);

        var result = ShaderIncludeResolver.Expand("#include \"a.hlsl\"", [root]);

        Assert.True(result.Ok);
        Assert.Contains("include cycle", result.Source);
        Assert.True(result.Source.Length < 10_000);
    }

    [Fact]
    public void RejectsBeyondDepthLimit()
    {
        var files = Enumerable.Range(0, 12)
            .Select(index => $"f{index}.hlsl|#include \"f{index + 1}.hlsl\"\nfloat v{index};")
            .Append("f12.hlsl|float end;")
            .ToArray();
        var root = Fixture(files);

        var result = ShaderIncludeResolver.Expand("#include \"f0.hlsl\"", [root]);

        Assert.NotEmpty(result.Rejected);
    }

    private static string Fixture(string[] files)
    {
        var directory = Directory.CreateTempSubdirectory("akashic-inc-");
        foreach (var entry in files)
        {
            var separator = entry.IndexOf('|');
            var name = entry[..separator];
            var text = entry[(separator + 1)..];
            File.WriteAllText(Path.Combine(directory.FullName, name), text);
        }

        return directory.FullName;
    }
}
